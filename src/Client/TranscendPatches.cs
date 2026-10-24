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
        Try("발사속도 / fire rate", () => harmony.Patch(AccessTools.PropertyGetter(weapon, "FireRate")
            ?? throw new MissingMemberException(weapon.FullName, "FireRate"), postfix: Hook(nameof(FireRate))));
        Try("과열 / overheat", () => harmony.Patch(GameBridge.RequiredMethod(weapon, "GetShotOverheat", 3), postfix: Hook(nameof(Overheat))));
        Try("고장 / malfunction", () => harmony.Patch(GameBridge.RequiredMethod(Required("EFT.Player+FirearmController"),
            "GetTotalMalfunctionChance", 7), postfix: Hook(nameof(Malfunction))));
        Try("방어등급 / armor class", () => harmony.Patch(AccessTools.PropertyGetter(armor, "ArmorClass")
            ?? throw new MissingMemberException(armor.FullName, "ArmorClass"), postfix: Hook(nameof(ArmorClass))));
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
        try { harmony.Patch(prefix, transpiler: Hook(nameof(BluntRead))); installed = true; return null; }
        catch (Exception ex) { return ex.GetBaseException().Message; }
    }

    private static Type Required(string name) => GameBridge.RuntimeType(name) ?? throw new TypeLoadException(name);
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
            var id = GameBridge.Get(weapon, "Id")?.ToString();
            info.Second = id == null ? 0 : Registry.TranscendLevel(id, TranscendTrack.Second);
            info.Third = id == null ? 0 : Registry.TranscendLevel(id, TranscendTrack.Third);
            info.Automatic = info.Third > 0 && Automatic(weapon);
            info.Version = version;
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
            var id = GameBridge.Get(item, "Id")?.ToString();
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

    // Replaces the template blunt throughput read inside the armor damage code.
    internal static float Blunt(object component, float value)
    {
        if (!Registry.HasTranscend || value <= 0) return value;
        var level = ArmorLevel(GameBridge.Get(component, "Item"));
        if (level <= 0) return value;
        return value * (float)(1 - TranscendEffects.Blunt(level, TemplateClass(GameBridge.Get(component, "Template")), Tuning) / 100);
    }

    // The template's own class (the patched ArmorClass getter already adds the bonus). Game
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
