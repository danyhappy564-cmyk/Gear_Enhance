using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace GearEnhance;

// Small Ammo getters are inlined by Mono (kb 2.61). Like TranscendPatches, rewrite
// their callers, keeping the instance on the stack. Never write a shared template.
internal static class AmmoPatches
{
    private static EffectRegistry Registry => StatPatches.Registry;
    private static readonly string[] Names = { "ammoAccr", "ammoRec", "DurabilityBurnModificator", "HeatFactor",
        "MisfireChance", "MalfMisfireChance", "MalfFeedChance", "AmmoFactor" };
    private static Type weaponType, slotType, stackType;
    private static Func<object, object> address, container, slotParent, stackParent, owner, ownerRoot, template;
    private static Func<object, object> controllerWeapon, controllerPlayer, playerProfile;
    private static Func<object, object> chambers, launcherChambers, slotItem, magazine, cartridges, lastRound;
    private static readonly Dictionary<MethodInfo, int> getters = new Dictionary<MethodInfo, int>();
    private static readonly Dictionary<FieldInfo, int> fields = new Dictionary<FieldInfo, int>();
    private static readonly Dictionary<MethodBase, (int Getter, int Field)> sites = new Dictionary<MethodBase, (int, int)>();
    private static MethodInfo templateFactor;
    private static bool enabled, reported, hasAny;
    private static int snapshotVersion = -1;
    // The registry deliberately exposes single-id reads only. Inspect its snapshot only
    // when Version changes; no registry edit or scan is needed on a shot/frame.
    private static readonly FieldInfo mythic = typeof(EffectRegistry).GetField("mythic", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void Install(Harmony harmony)
    {
        enabled = false;
        try
        {
            var ammo = Required("EFT.InventoryLogic.Ammo");
            var ammoTemplate = Required("EFT.InventoryLogic.AmmoTemplate");
            weaponType = Required("EFT.InventoryLogic.Weapon");
            slotType = Required("EFT.InventoryLogic.Slot"); stackType = Required("EFT.InventoryLogic.StackSlot");
            var item = Required("EFT.InventoryLogic.Item");
            address = Reader(item, "CurrentAddress"); container = Reader(Required("EFT.InventoryLogic.ItemAddress"), "Container");
            slotParent = Reader(slotType, "ParentItem"); stackParent = Reader(stackType, "ParentItem");
            owner = Reader(item, "Owner"); ownerRoot = Reader(Required("EFT.InventoryLogic.IItemOwner"), "RootItem");
            template = Reader(item, "Template");
            chambers = Reader(weaponType, "Chambers"); launcherChambers = Reader(Required("EFT.InventoryLogic.Launcher"), "Chambers");
            slotItem = Reader(slotType, "ContainedItem"); magazine = Reader(weaponType, "GetCurrentMagazine");
            cartridges = Reader(Required("EFT.InventoryLogic.Magazine"), "Cartridges"); lastRound = Reader(stackType, "Last");
            var controller = Required("EFT.Player+FirearmController");
            controllerWeapon = Reader(controller, "Item"); controllerPlayer = Reader(controller, "_player");
            playerProfile = Reader(Required("EFT.Player"), "ProfileId");
            if (mythic == null) throw new MissingFieldException("EffectRegistry.mythic");
            getters.Clear(); fields.Clear();
            for (var kind = 0; kind < Names.Length; kind++)
            {
                var getter = AccessTools.PropertyGetter(ammo, Names[kind]) ?? throw new MissingMemberException(ammo.FullName, Names[kind]);
                var expected = kind < 2 ? typeof(int) : typeof(float);
                if (getter.ReturnType != expected) throw new InvalidOperationException(getter.Name + " return type");
                getters.Add(getter, kind);
                if (kind == 7) continue;
                var field = AccessTools.Field(ammoTemplate, Names[kind]) ?? throw new MissingFieldException(ammoTemplate.FullName, Names[kind]);
                if (field.FieldType != expected) throw new InvalidOperationException(field.Name + " field type");
                fields.Add(field, kind);
            }
            templateFactor = AccessTools.PropertyGetter(ammoTemplate, "AmmoFactor") ?? throw new MissingMemberException("AmmoTemplate.AmmoFactor");
            // One pass over the game's method bodies for every getter call and template field read
            // (one scan per getter would walk all of Assembly-CSharp nine more times at startup).
            var callTokens = new HashSet<int>(getters.Keys.Select(g => g.MetadataToken)) { templateFactor.MetadataToken };
            var fieldTokens = new HashSet<int>(fields.Keys.Select(f => f.MetadataToken));
            var callers = new HashSet<MethodBase>();
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var type in ammo.Assembly.GetTypes())
            {
                if (type.IsGenericTypeDefinition) continue;
                foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.Module != ammo.Module) continue;
                    byte[] il;
                    try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                    catch (Exception) { continue; }
                    if (il != null && Uses(il, callTokens, fieldTokens)) callers.Add(method);
                }
            }
            foreach (var name in new[] { "Shot", "LauncherShot", "GetTotalMalfunctionChance", "GetSpecificMalfunctionVariants", "GetTotalCenterOfImpact" })
                if (!callers.Any(m => m.Name == name)) throw new MissingMethodException("ammo read caller", name);
            // A fired round may already have been removed from its chamber. The controller
            // supplies its actual gun for this call, including an attached launcher.
            foreach (var name in new[] { "Shot", "LauncherShot", "GetTotalMalfunctionChance", "GetSpecificMalfunctionVariants" })
            {
                var shot = GameBridge.RequiredMethod(controller, name, name == "Shot" ? 4 : name == "LauncherShot" ? 2 : 7);
                harmony.Patch(shot, prefix: Hook(name == "Shot" ? nameof(BeginWeaponShot)
                    : name == "GetTotalMalfunctionChance" ? nameof(BeginMalfunction) : nameof(BeginShot)), finalizer: Hook(nameof(EndShot)));
            }
            // A gun lives across shots. Rounds can be split during firing, so their cache
            // is lazy: the verified shot/malfunction scopes bypass it entirely.
            var weaponCtor = weaponType.GetConstructors(all).Single(c => c.GetParameters().Length == 2 && c.GetParameters()[0].ParameterType == typeof(string));
            harmony.Patch(weaponCtor, postfix: Hook(nameof(WeaponCreated)));
            sites.Clear();
            foreach (var caller in callers)
                harmony.Patch(caller, transpiler: Hook(nameof(Reads)));
            enabled = true;
            var changed = sites.Where(s => s.Value.Getter + s.Value.Field > 0).Select(s => s.Key).OrderBy(m => m.MetadataToken).ToArray();
            Plugin.LogInfo($"신화 탄 불이익 감소 연결 / mythic ammo penalties: getter {sites.Values.Sum(s => s.Getter)}, field {sites.Values.Sum(s => s.Field)}, callers {changed.Length}: "
                + string.Join(", ", changed.Select(m => m.DeclaringType?.FullName + "." + m.Name)));
        }
        catch { enabled = false; throw; }
    }

    private static Type Required(string name) => GameBridge.RuntimeType(name) ?? throw new TypeLoadException(name);
    private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(AmmoPatches), name);
    private static MethodInfo Method(string name) => typeof(AmmoPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
    // call / callvirt of a getter token, or ldfld of a template field token (byte scan, like CallsToken).
    private static bool Uses(byte[] il, HashSet<int> calls, HashSet<int> reads)
    {
        for (var i = 0; i + 4 < il.Length; i++)
        {
            var op = il[i];
            if (op != 0x28 && op != 0x6F && op != 0x7B) continue;
            var token = BitConverter.ToInt32(il, i + 1);
            if (op == 0x7B ? reads.Contains(token) : calls.Contains(token)) return true;
        }
        return false;
    }

    // Compile reference getters once. Hot ownership/address checks use delegates, without
    // reflection Invoke, argument arrays, boxed numbers, or a new parent search.
    private static Func<object, object> Reader(Type type, string name)
    {
        MemberInfo member = null;
        for (var current = type; current != null && member == null; current = current.BaseType)
            member = (MemberInfo)current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                ?? (MemberInfo)current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                ?? current.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
        if (member == null) throw new MissingMemberException(type.FullName, name);
        var result = member is PropertyInfo property ? property.PropertyType : member is MethodInfo method ? method.ReturnType : ((FieldInfo)member).FieldType;
        if (result.IsValueType) throw new InvalidOperationException(type.Name + "." + name + " must be a reference");
        var read = new DynamicMethod("AmmoRead_" + name, typeof(object), new[] { typeof(object) }, typeof(AmmoPatches), true);
        var il = read.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, member.DeclaringType);
        if (member is PropertyInfo p) il.Emit(OpCodes.Callvirt, p.GetGetMethod(true));
        else if (member is MethodInfo m) il.Emit(OpCodes.Callvirt, m);
        else il.Emit(OpCodes.Ldfld, (FieldInfo)member);
        il.Emit(OpCodes.Ret);
        return (Func<object, object>)read.CreateDelegate(typeof(Func<object, object>));
    }

    private static bool Active()
    {
        if (!enabled || StatPatches.Raw || !Registry.HasMythic) return false;
        if (snapshotVersion != Registry.Version)
        {
            var version = Registry.Version;
            var records = (Dictionary<string, (int Level, string[] Zones)>)mythic.GetValue(Registry);
            hasAny = false;
            foreach (var entry in records)
            {
                if (entry.Value.Level < 1) continue;
                var track = Registry.Find(entry.Key);
                if (track == null || track.Path != EnhancePath.Recoil && track.Path != EnhancePath.Ergonomics && track.Path != EnhancePath.Precision) continue;
                hasAny = true; break;
            }
            snapshotVersion = version;
        }
        return hasAny;
    }

    private sealed class WeaponInfo { internal int Version = -1, Level; }
    private static readonly ConditionalWeakTable<object, WeaponInfo> weapons = new ConditionalWeakTable<object, WeaponInfo>();
    private static readonly ConditionalWeakTable<object, WeaponInfo>.CreateValueCallback newWeapon = _ => new WeaponInfo();
    private sealed class AmmoInfo
    {
        internal int Version = -1, Count;
        internal object Weapon;
        internal readonly object[] Items = new object[8], Addresses = new object[8];
    }
    private static readonly ConditionalWeakTable<object, AmmoInfo> rounds = new ConditionalWeakTable<object, AmmoInfo>();
    private static readonly ConditionalWeakTable<object, AmmoInfo>.CreateValueCallback newAmmo = _ => new AmmoInfo();
    private static void WeaponCreated(object __instance) => weapons.GetValue(__instance, newWeapon);

    private static object Weapon(object ammo)
    {
        var info = rounds.GetValue(ammo, newAmmo);
        var valid = info.Version == Registry.Version;
        for (var i = 0; valid && i < info.Count; i++) valid = ReferenceEquals(address(info.Items[i]), info.Addresses[i]);
        if (valid) return info.Weapon;
        Array.Clear(info.Items, 0, info.Count); Array.Clear(info.Addresses, 0, info.Count);
        info.Count = 0; info.Weapon = null;
        var current = ammo;
        for (var depth = 0; current != null && depth < 8; depth++)
        {
            if (weaponType.IsInstanceOfType(current)) { info.Weapon = current; break; }
            var at = address(current);
            info.Items[info.Count] = current; info.Addresses[info.Count++] = at;
            if (at == null) break;
            var holder = container(at);
            current = slotType.IsInstanceOfType(holder) ? slotParent(holder)
                : stackType.IsInstanceOfType(holder) ? stackParent(holder) : null;
        }
        info.Version = Registry.Version;
        return info.Weapon;
    }

    // The player's own gun's mythic level, 0 for anything else (a bot's looted gun, stash ammo).
    private static int Stage(object weapon)
    {
        if (weapon == null || !MythicPatches.Main(out var profile, out var equipment) || equipment == null || profile != Registry.ProfileId) return 0;
        var heldBy = owner(weapon);
        if (heldBy == null || !ReferenceEquals(ownerRoot(heldBy), equipment)) return 0;
        var info = weapons.GetValue(weapon, newWeapon);
        if (info.Version != Registry.Version)
        {
            var id = CloneLinks.EffectId(weapon, out var settled);
            info.Level = id == null ? -1 : Registry.MythicLevel(id);
            if (settled) info.Version = Registry.Version;
        }
        return Math.Max(0, info.Level);
    }

    [ThreadStatic] private static object shotAmmo, shotWeapon, shotTemplate;
    private struct ShotState { internal object Ammo, Weapon, Template; internal bool Entered; }
    private static void BeginShot(object __instance, object ammo, out ShotState __state) => BeginScope(__instance, ammo, null, out __state);
    private static void BeginWeaponShot(object __instance, object weapon, object ammo, out ShotState __state) => BeginScope(__instance, ammo, weapon, out __state);
    private static void BeginScope(object controller, object ammo, object firingWeapon, out ShotState state)
    {
        state = new ShotState { Ammo = shotAmmo, Weapon = shotWeapon, Template = shotTemplate, Entered = true };
        shotAmmo = shotWeapon = shotTemplate = null;
        try
        {
            if (!Active() || ammo == null) return;
            var player = controllerPlayer(controller);
            if (player == null || !MythicPatches.Main(out var profile, out _) || (string)playerProfile(player) != profile) return;
            var weapon = firingWeapon ?? controllerWeapon(controller);
            if (Stage(weapon) <= 0) return;
            shotAmmo = ammo; shotWeapon = weapon; shotTemplate = template(ammo);
        }
        catch (Exception ex) { Fail(ex); }
    }
    private static Exception EndShot(Exception __exception, ShotState __state)
    {
        if (__state.Entered) { shotAmmo = __state.Ammo; shotWeapon = __state.Weapon; shotTemplate = __state.Template; }
        return __exception;
    }
    private static void BeginMalfunction(object __instance, object ammoToFire, out ShotState __state) => BeginShot(__instance, ammoToFire, out __state);

    private static void Fail(Exception ex)
    {
        enabled = false;
        if (reported) return;
        reported = true;
        // The logger lives in the plugin (BepInEx); without it (test host) the effect still stops quietly.
        try { Report("신화 탄 불이익 감소 중단(나머지 기능은 정상) / mythic ammo penalties disabled: " + ex.GetBaseException().Message); }
        catch (Exception) { }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Report(string text) => Plugin.LogError(text);
    // Share of each bad value taken away: 20 % a mythic level, all of it at V (user 2026-10-26).
    private static float Share(object ammo) => !Active() || ammo == null ? 0
        : (float)(MythicEffects.AmmoPenaltyCut(Stage(ReferenceEquals(ammo, shotAmmo) ? shotWeapon : Weapon(ammo))) / 100);
    internal static int NeutralInt(int value, int kind, float share) => kind == 0 && value < 0 || kind == 1 && value > 0
        ? (int)Math.Round(value * (1 - share), MidpointRounding.AwayFromZero) : value;
    // Multipliers (burn, heat, AmmoFactor) are bad above 1; chances are bad above 0.
    internal static float NeutralFloat(float value, int kind, float share) => kind == 2 || kind == 3 || kind == 7
        ? value > 1 ? 1 + (value - 1) * (1 - share) : value : value > 0 ? value * (1 - share) : value;
    private static int ReadInt(object ammo, int value, int kind)
    {
        try { var share = Share(ammo); return share > 0 ? NeutralInt(value, kind, share) : value; }
        catch (Exception ex) { Fail(ex); return value; }
    }
    private static float ReadFloat(object ammo, float value, int kind)
    {
        try { var share = Share(ammo); return share > 0 ? NeutralFloat(value, kind, share) : value; }
        catch (Exception ex) { Fail(ex); return value; }
    }
    private static float InstanceFactor(float value, object ammo) => ReadFloat(ammo, value, 7);
    private static int InstanceInt(int value, object ammo, int kind) => ReadInt(ammo, value, kind);
    private static float InstanceFloat(float value, object ammo, int kind) => ReadFloat(ammo, value, kind);
    private static float WeaponFactor(float value, object weapon)
    {
        try
        {
            if (!Active() || !HasLoadedAmmo(weapon)) return value;
            var share = (float)(MythicEffects.AmmoPenaltyCut(Stage(weaponType.IsInstanceOfType(weapon) ? weapon : Weapon(weapon))) / 100);
            return share > 0 ? NeutralFloat(value, 7, share) : value;
        }
        catch (Exception ex) { Fail(ex); return value; }
    }
    private static bool HasLoadedAmmo(object weapon)
    {
        if (weapon == null) return false;
        var isWeapon = weaponType.IsInstanceOfType(weapon);
        var slots = (object[])(isWeapon ? chambers(weapon) : launcherChambers(weapon));
        if (slots != null)
            for (var i = 0; i < slots.Length; i++) if (slots[i] != null && slotItem(slots[i]) != null) return true;
        var mag = isWeapon ? magazine(weapon) : null;
        var stack = mag == null ? null : cartridges(mag);
        return stack != null && lastRound(stack) != null;
    }
    // Template-only readers have no owner. They change only inside a verified shot and
    // only for that round's exact template; stash/inspection/debug readers stay original.
    private static int TemplateInt(object source, int value, int kind) => ReferenceEquals(source, shotTemplate) ? ReadInt(shotAmmo, value, kind) : value;
    private static float TemplateFloat(object source, float value, int kind) => ReferenceEquals(source, shotTemplate) ? ReadFloat(shotAmmo, value, kind) : value;

    private static IEnumerable<CodeInstruction> Reads(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var getterSites = 0; var fieldSites = 0;
        foreach (var instruction in instructions)
        {
            var isCall = instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt;
            if (isCall && instruction.operand is MethodInfo getter && getters.TryGetValue(getter, out var kind))
            {
                getterSites++;
                var duplicate = new CodeInstruction(OpCodes.Dup);
                duplicate.MoveLabelsFrom(instruction); duplicate.MoveBlocksFrom(instruction);
                yield return duplicate; yield return instruction;
                yield return new CodeInstruction(OpCodes.Ldc_I4, kind);
                yield return new CodeInstruction(OpCodes.Call, Method(kind < 2 ? nameof(ReadInt) : nameof(ReadFloat)));
            }
            else if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && fields.TryGetValue(field, out kind))
            {
                fieldSites++;
                if (!__originalMethod.IsStatic && __originalMethod.DeclaringType?.FullName == "EFT.InventoryLogic.Ammo")
                {
                    yield return instruction; yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldc_I4, kind);
                    yield return new CodeInstruction(OpCodes.Call, Method(kind < 2 ? nameof(InstanceInt) : nameof(InstanceFloat)));
                }
                else
                {
                    var duplicate = new CodeInstruction(OpCodes.Dup);
                    duplicate.MoveLabelsFrom(instruction); duplicate.MoveBlocksFrom(instruction);
                    yield return duplicate; yield return instruction;
                    yield return new CodeInstruction(OpCodes.Ldc_I4, kind);
                    yield return new CodeInstruction(OpCodes.Call, Method(kind < 2 ? nameof(TemplateInt) : nameof(TemplateFloat)));
                }
            }
            else if (isCall && instruction.operand is MethodInfo method && method == templateFactor && !__originalMethod.IsStatic
                && (__originalMethod.DeclaringType?.FullName == "EFT.InventoryLogic.Ammo"
                    || __originalMethod.DeclaringType?.FullName == "EFT.InventoryLogic.Weapon"
                    || __originalMethod.DeclaringType?.FullName == "EFT.InventoryLogic.Launcher"))
            {
                getterSites++;
                yield return instruction; yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, Method(__originalMethod.DeclaringType.FullName == "EFT.InventoryLogic.Ammo"
                    ? nameof(InstanceFactor) : nameof(WeaponFactor)));
            }
            else yield return instruction;
        }
        sites[__originalMethod] = (getterSites, fieldSites);
    }
}
