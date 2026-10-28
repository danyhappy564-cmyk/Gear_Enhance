using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace GearEnhance;

// 1.3.0 transcendence effects. The recoil hooks run only when a weapon is drawn or
// modified. The per-shot and per-hit hooks (fire rate, malfunction, overheat, armor class,
// blunt damage) leave at once while nothing is transcended, and otherwise do one cached
// lookup per weapon or a bounded parent walk per armor part. No new per-frame loops.
internal static class TranscendPatches
{
    private static EffectRegistry Registry => StatPatches.Registry;
    private static TranscendTuning Tuning => Registry.TranscendTuning;

    // Returns the effects whose game hook could not be installed; the others still work.
    internal static List<string> Install(Harmony harmony)
    {
        var failed = new List<string>();
        void Try(string name, Action install)
        {
            try { install(); }
            catch (Exception ex) { failed.Add(name + ": " + ex.GetBaseException().Message); }
        }
        var weapon = Required("EFT.InventoryLogic.Weapon");
        var armor = Required("EFT.InventoryLogic.ArmorComponent");
        Try("수평 반동 / horizontal recoil", () =>
        {
            var shot = Required("ShotEffector");
            harmony.Patch(GameBridge.RequiredMethod(shot, "OnWeaponParametersChanged", 0), postfix: Hook(nameof(RecoilBack)));
            harmony.Patch(GameBridge.RequiredMethod(shot, "SubscribeToWeapon", 4), postfix: Hook(nameof(RecoilAngle)));
            harmony.Patch(GameBridge.RequiredMethod(shot, "CalculateRecoilParameters", 0), postfix: Hook(nameof(RecoilAngle)));
        });
        // Fire rate and armor class are one-line getters that the game's JIT inlines into its
        // callers, so a postfix on the getter is skipped in the shot and hit code (only the
        // inspection value moved in game). The callers' reads are rewritten instead.
        Try("발사속도 / fire rate", () => WrapReads(harmony, Getter(weapon, "FireRate"),
            Nested(Required("EFT.Player+FirearmController")), nameof(FireRateReads), "AutomaticFireOperation"));
        // In-game report (2026-10-24): full auto still fired at the base rate after the
        // rewrite. The burst operation's shot interval is checked and set once it is built.
        Try("발사속도 확인 / fire rate check", () =>
        {
            var operation = Nested(Required("EFT.Player+FirearmController")).FirstOrDefault(t => t.Name == "AutomaticFireOperation")
                ?? throw new TypeLoadException("AutomaticFireOperation");
            shotsTime = AccessTools.Field(operation, "_shotsTime") ?? throw new MissingFieldException(operation.Name, "_shotsTime");
            var ctor = operation.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SingleOrDefault(c => c.GetParameters().Length == 1) ?? throw new MissingMethodException(operation.Name, ".ctor(controller)");
            harmony.Patch(ctor, postfix: Hook(nameof(AutomaticCreated)));
            // Overheating resets the interval from the base rate in Update; corrected ones redo it.
            harmony.Patch(GameBridge.RequiredMethod(operation, "Update", 1), postfix: Hook(nameof(AutomaticUpdated)));
            // In-game 2026-10-24: interval 61.5 ms in the log, yet "feels the same" in a raid.
            // Each shot of a transcended weapon is timed so the log shows the real rate.
            harmony.Patch(GameBridge.RequiredMethod(operation, "InternalOnFireEvent", 0), postfix: Hook(nameof(AutomaticShot)));
        });
        Try("과열 / overheat", () => harmony.Patch(GameBridge.RequiredMethod(weapon, "GetShotOverheat", 3), postfix: Hook(nameof(Overheat))));
        Try("고장 / malfunction", () => harmony.Patch(GameBridge.RequiredMethod(Required("EFT.Player+FirearmController"),
            "GetTotalMalfunctionChance", 7), postfix: Hook(nameof(Malfunction))));
        Try("방어등급 / armor class", () => WrapReads(harmony, Getter(armor, "ArmorClass"),
            Nested(armor, Required("EFT.InventoryLogic.ArmorHolderComponent"), Required("EFT.InventoryLogic.ArmorPlatesFormatter"),
                Required("EFT.UI.MultiLineInfo")).Append(Required("EFT.Player")), nameof(ArmorClassReads), "SetPenetrationStatus", "ApplyDamage"));
        Try("둔탁상 / blunt damage", () => harmony.Patch(GameBridge.RequiredMethod(armor, "ApplyDamage", 7), transpiler: Hook(nameof(BluntRead))));
        Try("상인 판매가 / trader price", () =>
        {
            var price = Required("EFT.Trading.PriceCalculator");
            harmony.Patch(GameBridge.RequiredMethod(price, "CalculateBasePriceForSingleItem", 3), transpiler: Hook(nameof(PlainDurabilityReads)));
            harmony.Patch(GameBridge.RequiredMethod(price, "CalculateBuyoutBasePriceForSingleItem", 4), transpiler: Hook(nameof(PlainDurabilityReads)));
        });
        Try("검사 화면 수치 / inspection values", () =>
        {
            var attributeIds = Required("EFT.InventoryLogic.EItemAttributeId");
            recoilBackId = Enum.Parse(attributeIds, "RecoilBack");
            fireRateId = Enum.Parse(attributeIds, "FireRate");
            var ctor = weapon.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SingleOrDefault(c => c.GetParameters().Length == 2 && c.GetParameters()[0].ParameterType == typeof(string))
                ?? throw new MissingMethodException(weapon.FullName, ".ctor(string, WeaponTemplate)");
            harmony.Patch(ctor, postfix: Hook(nameof(WeaponCreated)));
        });
        return failed;
    }

    // Realism ballistics replaces ApplyDamage with its own prefix that reads the same template
    // value. Called from Start(): a plugin loaded after us is not found during our Awake (kb 2.58).
    // Returns null when Realism is absent or the hook went in, else the reason.
    internal static string InstallRealism(Harmony harmony, out bool installed)
    {
        installed = false;
        var realism = AccessTools.TypeByName("RealismMod.ApplyArmorDamagePatch");
        var prefix = realism == null ? null : AccessTools.Method(realism, "Prefix");
        if (prefix == null) return null;
        try
        {
            harmony.Patch(prefix, transpiler: Hook(nameof(BluntRead)));
            // Its class reads are inlined like the game's; a prefix without any is left as it is.
            harmony.Patch(prefix, transpiler: Hook(nameof(ArmorClassReads)));
            installed = true;
            return null;
        }
        catch (Exception ex) { return ex.GetBaseException().Message; }
    }

    private static Type Required(string name) => GameBridge.RuntimeType(name) ?? throw new TypeLoadException(name);

    private static MethodInfo Getter(Type type, string name) =>
        AccessTools.PropertyGetter(type, name) ?? throw new MissingMemberException(type.FullName, name);

    // The types and their nested types (closures, fire operations). Bounded: nesting is finite
    // and each type is visited once.
    internal static IEnumerable<Type> Nested(params Type[] roots)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>(roots);
        while (queue.Count > 0 && seen.Count < 4096)
        {
            var type = queue.Dequeue();
            if (!seen.Add(type)) continue;
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) queue.Enqueue(nested);
        }
        return seen;
    }

    // Methods declared on the types whose IL calls the getter: a call or callvirt opcode followed
    // by the getter's token. Only same-module callers carry that token, which is all of them
    // here (the getters and the callers are in Assembly-CSharp).
    internal static List<MethodBase> Callers(IEnumerable<Type> types, MethodInfo getter)
    {
        var token = getter.MetadataToken;
        var found = new List<MethodBase>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            if (type.IsGenericTypeDefinition) continue;
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.Module != getter.Module) continue;
                byte[] il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                catch (Exception) { continue; }
                if (il != null && CallsToken(il, token)) found.Add(method);
            }
        }
        return found;
    }

    internal static bool CallsToken(byte[] il, int token)
    {
        for (var i = 0; i + 4 < il.Length; i++)
            if ((il[i] == 0x28 || il[i] == 0x6F) && BitConverter.ToInt32(il, i + 1) == token) return true;
        return false;
    }

    // Rewrites every caller; the named callers (the shot and hit code) must be among them.
    private static void WrapReads(Harmony harmony, MethodInfo getter, IEnumerable<Type> types, string transpiler, params string[] required)
    {
        var callers = Callers(types, getter);
        foreach (var name in required)
            if (!callers.Any(m => m.Name == name || m.DeclaringType?.Name == name))
                throw new MissingMethodException("caller of " + getter.DeclaringType?.Name + "." + getter.Name, name);
        var failed = new List<string>();
        foreach (var caller in callers)
        {
            try { harmony.Patch(caller, transpiler: Hook(transpiler)); }
            catch (Exception ex) { failed.Add(caller.DeclaringType?.Name + "." + caller.Name + ": " + ex.GetBaseException().Message); }
        }
        if (failed.Count > 0) throw new InvalidOperationException(string.Join("; ", failed));
        Plugin.LogInfo($"초월 {getter.DeclaringType?.Name}.{getter.Name} 읽기 치환 {callers.Count}곳: "
            + string.Join(", ", callers.Select(m => m.DeclaringType?.Name + "." + m.Name)));
    }

    // `x.Getter` → Read(x, x.Getter): the owner is kept on the stack with Dup, like BluntRead.
    private static IEnumerable<CodeInstruction> WrapCalls(IEnumerable<CodeInstruction> instructions, string owner, string getter, string read)
    {
        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && instruction.operand is MethodInfo method
                && method.Name == getter && method.DeclaringType?.FullName == owner)
            {
                var duplicate = new CodeInstruction(OpCodes.Dup);
                duplicate.MoveLabelsFrom(instruction);
                duplicate.MoveBlocksFrom(instruction);
                yield return duplicate;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Call, Method(read));
            }
            else yield return instruction;
        }
    }

    private static IEnumerable<CodeInstruction> FireRateReads(IEnumerable<CodeInstruction> instructions) =>
        WrapCalls(instructions, "EFT.InventoryLogic.Weapon", "get_FireRate", nameof(ShotFireRate));

    private static IEnumerable<CodeInstruction> ArmorClassReads(IEnumerable<CodeInstruction> instructions) =>
        WrapCalls(instructions, "EFT.InventoryLogic.ArmorComponent", "get_ArmorClass", nameof(ReadArmorClass));
    private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(TranscendPatches), name);
    private static MethodInfo Method(string name) => typeof(TranscendPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

    // Weapon instance → its transcendence, refreshed when the records change. A weapon's
    // id and fire modes never change, so shots after the first need no reflection.
    internal sealed class WeaponInfo { internal int Version = -1, Second, Third; internal bool Automatic; }
    private static readonly ConditionalWeakTable<object, WeaponInfo> weapons = new ConditionalWeakTable<object, WeaponInfo>();
    private static readonly ConditionalWeakTable<object, WeaponInfo>.CreateValueCallback newInfo = _ => new WeaponInfo();

    internal static WeaponInfo Weapon(object weapon)
    {
        if (weapon == null || !Registry.HasTranscend) return null;
        var info = weapons.GetValue(weapon, newInfo);
        var version = Registry.Version;
        if (info.Version != version)
        {
            var id = CloneLinks.EffectId(weapon, out var settled);
            info.Second = id == null ? 0 : Registry.TranscendLevel(id, TranscendTrack.Second);
            info.Third = id == null ? 0 : Registry.TranscendLevel(id, TranscendTrack.Third);
            info.Automatic = info.Third > 0 && Automatic(weapon);
            // A hideout copy not linked yet is looked up again on the next read.
            if (settled) info.Version = version;
        }
        return info.Second > 0 || info.Third > 0 ? info : null;
    }

    // Same rule as the server: an automatic or burst mode means the fire rate track.
    internal static bool Automatic(object weapon)
    {
        if (!(GameBridge.Get(weapon, "WeapFireType") is Array modes)) return false;
        foreach (var mode in modes)
        {
            var name = mode?.ToString();
            if (name == "fullauto" || name == "burst") return true;
        }
        return false;
    }

    // Backward kick reduction to apply now: the 2nd track within the 80% limit shared
    // with the enhancement recoil track and stars.
    internal static double Horizontal(object weapon)
    {
        var info = Weapon(weapon);
        if (info == null || info.Second <= 0) return 0;
        return TranscendEffects.HorizontalWithCap(StatPatches.Amount(weapon, EnhancePath.Recoil),
            TranscendEffects.HorizontalRecoil(info.Second, Tuning));
    }

    // ShotEffector._weapon is an IWeapon. Weapon implements IWeapon.Item explicitly
    // (`Item IWeapon.Item => this`), so the name lookup finds nothing and it is the item itself.
    internal static object ShotWeapon(object shot)
    {
        var held = GameBridge.Get(shot, "_weapon");
        return GameBridge.Get(held, "Item") ?? held;
    }

    private static void RecoilBack(object __instance)
    {
        if (!Registry.HasTranscend) return;
        var reduction = Horizontal(ShotWeapon(__instance));
        if (reduction <= 0) return;
        var effect = GameBridge.Get(__instance, "CurrentRecoilEffect");
        if (GameBridge.Get(effect, "BasicPlayerRecoilPositionStrength") is Vector2 strength)
            GameBridge.Set(effect, "BasicPlayerRecoilPositionStrength", strength * (float)(1 - reduction / 100));
    }

    private static void RecoilAngle(object __instance)
    {
        if (!Registry.HasTranscend) return;
        var info = Weapon(ShotWeapon(__instance));
        if (info == null || info.Second <= 0) return;
        var effect = GameBridge.Get(__instance, "CurrentRecoilEffect");
        if (!(GameBridge.Get(effect, "BasicPlayerRecoilDegreeRange") is Vector2 range)) return;
        var (low, high) = TranscendEffects.Narrow(range.x, range.y, TranscendEffects.HorizontalRecoil(info.Second, Tuning));
        var narrowed = new Vector2(low, high);
        GameBridge.Set(effect, "BasicPlayerRecoilDegreeRange", narrowed);
        GameBridge.Set(effect, "BasicRecoilRadian", narrowed * (Mathf.PI / 180f));
    }

    internal static int FireRateBonus(object weapon, double baseRate)
    {
        var info = Weapon(weapon);
        return info == null || info.Third <= 0 || !info.Automatic ? 0
            : (int)Math.Round(TranscendEffects.FireRateBonus(baseRate, info.Third, Tuning));
    }

    private static void FireRate(object __instance, ref int __result)
    {
        if (!Registry.HasTranscend) return;
        __result += FireRateBonus(__instance, __result);
    }

    // Called from the rewritten game code (FireRateReads).
    internal static int ShotFireRate(object weapon, int rate)
    {
        FireRate(weapon, ref rate);
        return rate;
    }

    private static FieldInfo shotsTime;
    // Only operations whose rewritten read did not take: every other one leaves the postfix at
    // the lookup. The reflection below runs per frame only for these, only while firing.
    private sealed class CorrectedFire { internal object Weapon; internal int Rate; }
    private static readonly ConditionalWeakTable<object, CorrectedFire> correctedOperations = new ConditionalWeakTable<object, CorrectedFire>();

    // Shots of one trigger pull of a player weapon: a gap over 0.4 s starts a new burst.
    // After 12 shots the measured rate is logged, at most 8 times per game session.
    private sealed class Burst { internal float Start, Last; internal int Shots; internal int Rate; internal bool Logged; internal string Id; }
    private static readonly ConditionalWeakTable<object, Burst> bursts = new ConditionalWeakTable<object, Burst>();
    private static int burstLogs;

    private static void AutomaticShot(object __instance)
    {
        if (burstLogs >= 8 || !bursts.TryGetValue(__instance, out var burst)) return;
        var now = Time.realtimeSinceStartup;
        if (burst.Shots == 0 || now - burst.Last > 0.4f) { burst.Start = now; burst.Shots = 0; burst.Logged = false; }
        burst.Shots++;
        burst.Last = now;
        if (burst.Logged || burst.Shots < 12) return;
        burst.Logged = true;
        burstLogs++;
        var measured = (burst.Shots - 1) * 60f / Math.Max(0.001f, now - burst.Start);
        var interval = shotsTime.GetValue(__instance) is float value ? value * 1000 : 0;
        Plugin.LogInfo($"발사속도 실측: {burst.Id} {burst.Shots}발 {measured:0}rpm (목표 {burst.Rate}rpm, 지금 간격 {interval:0.0}ms, FPS {1f / Math.Max(0.0001f, Time.unscaledDeltaTime):0})");
    }

    private static void AutomaticUpdated(object __instance)
    {
        if (!Registry.HasTranscend || !correctedOperations.TryGetValue(__instance, out var fix)) return;
        try
        {
            var multiplier = GameBridge.Get(GameBridge.Get(fix.Weapon, "MalfState"), "OverheatFirerateMult") is float value && value > 0 ? value : 1f;
            var expected = 60f / (fix.Rate * multiplier);
            if (shotsTime.GetValue(__instance) is float current && Math.Abs(current - expected) > 0.0001f) shotsTime.SetValue(__instance, expected);
        }
        catch (Exception) { correctedOperations.Remove(__instance); }
    }
    // One line per weapon per game session, so the in-game log shows what the shot timer got.
    private static readonly HashSet<string> fireRateLogged = new HashSet<string>();

    // Runs when a weapon with full auto is drawn (not per shot). The interval must already be
    // 60 / (base + bonus) through the rewritten read; if it is not, it is set here and logged.
    private static void AutomaticCreated(object __instance)
    {
        try
        {
            var weapon = GameBridge.Get(__instance, "Weapon") ?? GameBridge.Get(GameBridge.Get(__instance, "Controller"), "Item");
            var id = CloneLinks.EffectId(weapon);
            // In-game 2026-10-24: no line at all after full auto in the hideout range. The first
            // build of the session and every enhanced weapon say what the hook saw.
            if (fireRateLogged.Count < 256 && (fireRateLogged.Add("first") || id != null && Registry.Find(id) != null && fireRateLogged.Add("seen " + id)))
                Plugin.LogInfo($"연사 준비 감지: 무기 {id ?? "?"} ({GameBridge.Get(weapon, "TemplateId")}) 기본 {GameBridge.Get(weapon, "FireRate")}rpm"
                    + $" · 간격 {shotsTime.GetValue(__instance)} · 강화 기록 {(id != null && Registry.Find(id) != null ? "있음" : "없음")}"
                    + $" · 이 총 3성 초월 {(id == null ? 0 : Registry.TranscendLevel(id, TranscendTrack.Third))} · 초월 기록 전체 {(Registry.HasTranscend ? "있음" : "없음")} · 월드 {CloneLinks.WorldName()}");
            if (id == null || !(GameBridge.Get(weapon, "FireRate") is int baseRate) || baseRate <= 0 || !(shotsTime.GetValue(__instance) is float current)) return;
            // The player's own weapons are timed too (unenhanced ones for comparison); bots never.
            if (Plugin.Detailed && GameBridge.Get(GameBridge.Get(__instance, "Player"), "IsAI") is false)
            {
                bursts.Remove(__instance);
                bursts.Add(__instance, new Burst { Rate = baseRate, Id = id });
            }
            var info = Weapon(weapon);
            if (info == null || info.Third <= 0)
            {
                if (Registry.TranscendLevel(id, TranscendTrack.Third) > 0 && fireRateLogged.Count < 256 && fireRateLogged.Add(id))
                    Plugin.LogInfo($"초월 발사속도 확인: {id} 기록은 있으나 효과 없음 (기록 {Registry.TranscendLevel(id, TranscendTrack.Third)}, 초월 전체 {Registry.HasTranscend})");
                return;
            }
            var bonus = FireRateBonus(weapon, baseRate);
            var expected = 60f / (baseRate + bonus);
            var corrected = bonus > 0 && Math.Abs(current - expected) > 0.0001f;
            if (bursts.TryGetValue(__instance, out var timed)) timed.Rate = baseRate + bonus;
            if (corrected)
            {
                shotsTime.SetValue(__instance, expected);
                correctedOperations.Remove(__instance);
                correctedOperations.Add(__instance, new CorrectedFire { Weapon = weapon, Rate = baseRate + bonus });
            }
            if (fireRateLogged.Count < 256 && fireRateLogged.Add(id))
                Plugin.LogInfo($"초월 발사속도 확인: {id} 기본 {baseRate}rpm + {bonus} (3성 초월 {info.Third}, 연사 {info.Automatic}) · 연사 간격 {current * 1000:0.0}ms"
                    + (corrected ? $" → {expected * 1000:0.0}ms로 보정(코드 치환이 안 걸림)" : " (치환 정상)"));
        }
        catch (Exception ex)
        {
            if (fireRateLogged.Add("!")) Plugin.LogError("Gear Enhance: 초월 발사속도 확인 실패(사격은 정상) / fire rate check failed: " + ex.GetBaseException().Message);
        }
    }

    private static void Overheat(object __instance, ref float __result)
    {
        if (!Registry.HasTranscend) return;
        var info = Weapon(__instance);
        if (info == null || info.Third <= 0 || info.Automatic) return;
        __result *= (float)(1 - TranscendEffects.Overheat(info.Third, Tuning) / 100);
    }

    // The total and its sources shrink together, so the drop between sources keeps its odds.
    private static void Malfunction(object __instance, ref float __result, ref double durabilityMalfChance,
        ref float magMalfChance, ref float ammoMalfChance, ref float overheatMalfChance)
    {
        if (!Registry.HasTranscend) return;
        var info = Weapon(GameBridge.Get(__instance, "Item"));
        if (info == null || info.Third <= 0 || info.Automatic) return;
        var keep = 1 - TranscendEffects.Malfunction(info.Third, Tuning) / 100;
        __result *= (float)keep;
        durabilityMalfChance *= keep;
        magMalfChance *= (float)keep;
        ammoMalfChance *= (float)keep;
        overheatMalfChance *= (float)keep;
    }

    // 3rd track of the armor an item belongs to: its own, or that of the armor it is
    // registered to and inserted in. Bounded like StatPatches.Amount; never recursive.
    internal static int ArmorLevel(object item)
    {
        string origin = null;
        var best = 0;
        for (var depth = 0; item != null && depth < 64; depth++)
        {
            var id = CloneLinks.EffectId(item);
            if (id == null) break;
            origin ??= id;
            if (depth == 0 || Registry.Registered(id, origin)) best = Math.Max(best, Registry.TranscendLevel(id, TranscendTrack.Third));
            var parent = GameBridge.SlotParent(item);
            if (ReferenceEquals(item, parent)) break;
            item = parent;
        }
        return best;
    }

    private static void ArmorClass(object __instance, ref int __result)
    {
        if (!Registry.HasTranscend) return;
        var level = ArmorLevel(GameBridge.Get(__instance, "Item"));
        if (level > 0) __result = TranscendEffects.ArmorClass(level, __result);
    }

    // Called from the rewritten game code (ArmorClassReads).
    internal static int ReadArmorClass(object component, int value)
    {
        ArmorClass(component, ref value);
        return value;
    }

    // Replaces the template blunt throughput read inside the armor damage code.
    internal static float Blunt(object component, float value)
    {
        if (value <= 0) return value;
        var item = GameBridge.Get(component, "Item");
        // 1.4.2: penalty-free armor's Mobility track (impact absorption).
        var relief = StatPatches.BluntRelief(item);
        if (relief > 0) value = EnhanceEffects.Reduce(value, relief);
        if (!Registry.HasTranscend) return value;
        var level = ArmorLevel(item);
        if (level <= 0) return value;
        return value * (float)(1 - TranscendEffects.Blunt(level, TemplateClass(GameBridge.Get(component, "Template")), Tuning) / 100);
    }

    // The template's own class (the rewritten ArmorClass reads already add the bonus). Game
    // templates implement IArmorComponentTemplate.ArmorClass explicitly over an `armorClass`
    // field, so a plain name lookup finds nothing: read it through the interface.
    private static PropertyInfo templateClass;
    internal static int TemplateClass(object template)
    {
        if (template == null) return 0;
        templateClass ??= GameBridge.RuntimeType("EFT.InventoryLogic.IArmorComponentTemplate")?.GetProperty("ArmorClass");
        if (templateClass != null && templateClass.DeclaringType.IsInstanceOfType(template) && templateClass.GetValue(template) is int value) return value;
        return GameBridge.Get(template, "ArmorClass") is int named ? named : GameBridge.Get(template, "armorClass") is int field ? field : 0;
    }

    private static bool Reads(CodeInstruction instruction, string name) =>
        instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field.Name == name
        || (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
            && instruction.operand is MethodInfo method && method.Name == "get_" + name;

    // `component.Template.BluntThroughput` → Blunt(component, that value). The component is
    // kept on the stack with Dup, like StatPatches.PenaltyDisplay.
    private static IEnumerable<CodeInstruction> BluntRead(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        var replaced = 0;
        for (var i = 0; i < list.Count; i++)
        {
            if (i + 1 < list.Count && Reads(list[i], "Template") && Reads(list[i + 1], "BluntThroughput"))
            {
                var duplicate = new CodeInstruction(OpCodes.Dup);
                duplicate.MoveLabelsFrom(list[i]);
                duplicate.MoveBlocksFrom(list[i]);
                yield return duplicate;
                yield return list[i];
                yield return list[++i];
                yield return new CodeInstruction(OpCodes.Call, Method(nameof(Blunt)));
                replaced++;
            }
            else yield return list[i];
        }
        if (replaced == 0) throw new MissingFieldException("ArmorComponent.Template.BluntThroughput");
    }

    // Trader prices rate durability against the template maximum with no limit. Items with
    // added max durability are priced as if it were not there (server does flea and Fence).
    internal static float PlainMax(object component, float max)
    {
        if (!Registry.HasGrants) return max;
        var grant = GrantOf(component);
        return grant > 0 && max > grant ? max - (float)grant : max;
    }

    internal static float PlainDurability(object component, float durability)
    {
        if (!Registry.HasGrants) return durability;
        var grant = GrantOf(component);
        if (grant <= 0) return durability;
        var max = GameBridge.Get(component, "MaxDurability") is float value ? value : 0;
        return max > grant ? durability * (max - (float)grant) / max : durability;
    }

    private static double GrantOf(object component)
    {
        var id = GameBridge.Get(GameBridge.Get(component, "Item"), "Id")?.ToString();
        return id == null ? 0 : Registry.Grant(id);
    }

    private static IEnumerable<CodeInstruction> PlainDurabilityReads(IEnumerable<CodeInstruction> instructions)
    {
        var replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field
                && field.DeclaringType?.FullName == "EFT.InventoryLogic.RepairableComponent"
                && (field.Name == "MaxDurability" || field.Name == "Durability"))
            {
                var duplicate = new CodeInstruction(OpCodes.Dup);
                duplicate.MoveLabelsFrom(instruction);
                duplicate.MoveBlocksFrom(instruction);
                yield return duplicate;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Call, Method(field.Name == "MaxDurability" ? nameof(PlainMax) : nameof(PlainDurability)));
                replaced++;
            }
            else yield return instruction;
        }
        if (replaced == 0) throw new MissingFieldException("RepairableComponent.MaxDurability");
    }

    // Inspection values follow the real ones: the weapon's own `Recoil back` and fire rate
    // attributes are wrapped once when it is created; the wrappers read the records when shown.
    // Math.Round is Mathf.RoundToInt's rounding (to even), without needing Unity in tests.
    private static object recoilBackId, fireRateId;
    private static bool displayReported;

    private static void WeaponCreated(object __instance)
    {
        try
        {
            if (!(GameBridge.Get(__instance, "Attributes") is IList attributes)) return;
            for (var i = 0; i < attributes.Count; i++)
            {
                var attribute = attributes[i];
                var id = GameBridge.Get(attribute, "Id");
                if (Equals(id, recoilBackId)) WrapRecoilBack(__instance, attribute);
                else if (Equals(id, fireRateId)) WrapFireRate(__instance, attribute);
            }
        }
        catch (Exception ex)
        {
            if (displayReported) return;
            displayReported = true;
            Plugin.LogError("Gear Enhance: 초월 검사 화면 수치 연결 실패(효과는 정상). Inspection value hook failed (effects still work): " + ex);
        }
    }

    internal static void WrapRecoilBack(object weapon, object attribute)
    {
        if (!(GameBridge.Get(attribute, "Base") is Func<float> value) || !(GameBridge.Get(attribute, "Delta") is Func<float> delta)
            || !(GameBridge.Get(attribute, "StringValue") is Func<string> text)) return;
        Func<float> scaled = () =>
        {
            var original = delta();
            var reduction = Horizontal(weapon);
            return reduction > 0 ? (float)((1 + original) * (1 - reduction / 100) - 1) : original;
        };
        GameBridge.Set(attribute, "Delta", scaled);
        GameBridge.Set(attribute, "StringValue", (Func<string>)(() =>
            Horizontal(weapon) > 0 ? ((int)Math.Round(value() * (1 + scaled()))).ToString() : text()));
    }

    internal static void WrapFireRate(object weapon, object attribute)
    {
        if (!(GameBridge.Get(attribute, "Base") is Func<float> value) || !(GameBridge.Get(attribute, "StringValue") is Func<string> text)) return;
        GameBridge.Set(attribute, "Base", (Func<float>)(() =>
        {
            var rate = value();
            return rate + FireRateBonus(weapon, rate);
        }));
        GameBridge.Set(attribute, "StringValue", (Func<string>)(() =>
        {
            var shown = text();
            var rate = value();
            var bonus = FireRateBonus(weapon, rate);
            if (bonus == 0 || string.IsNullOrEmpty(shown)) return shown;
            var digits = 0;
            while (digits < shown.Length && char.IsDigit(shown[digits])) digits++;
            return digits == 0 ? shown : ((int)Math.Round(rate) + bonus) + shown.Substring(digits);
        }));
    }
}
