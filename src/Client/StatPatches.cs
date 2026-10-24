using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using HarmonyLib;

namespace GearEnhance;

internal static class StatPatches
{
    internal static readonly EffectRegistry Registry = new EffectRegistry();

    internal static void Install(Harmony harmony)
    {
        var weapon = RequiredType("EFT.InventoryLogic.Weapon");
        var armor = RequiredType("EFT.InventoryLogic.ArmorComponent");
        PatchGetter(harmony, weapon, "RecoilDelta", nameof(Recoil));
        PatchGetter(harmony, weapon, "ErgonomicsTotal", nameof(Ergonomics));
        PatchGetter(harmony, weapon, "TotalShotgunDispersion", nameof(Precision));
        harmony.Patch(GameBridge.RequiredMethod(weapon, "GetTotalCenterOfImpact", 1),
            postfix: Hook(nameof(Precision)));
        foreach (var property in new[] { "SpeedPenalty", "MousePenalty", "WeaponErgonomicPenalty" })
            PatchGetter(harmony, armor, property, nameof(Mobility));
        var damage = GameBridge.RequiredMethod(armor, "ApplyDurabilityDamage", 2);
        if (damage.GetParameters()[0].ParameterType != typeof(float))
            throw new MissingMethodException("ArmorComponent.ApplyDurabilityDamage(float, ...)");
        harmony.Patch(damage, prefix: Hook(nameof(Durability)));
        var profiles = GameBridge.RequiredMethod(RequiredType("EFT.EftClientBackendSession"), "GetProfiles", 0);
        if (profiles.ReturnType != typeof(Task)) throw new MissingMethodException("GetProfiles must return Task");
        harmony.Patch(profiles, prefix: Hook(nameof(ProfilesPrefix)), postfix: Hook(nameof(ProfilesPostfix)));

        // Native inspection cards include the carrier's own template penalties.
        // Preserve the owner on the stack when reading its template so only that
        // item's contribution is scaled, alongside patched plate getters.
        var penalty = RequiredType("EFT.InventoryLogic.EquipmentPenaltyComponent");
        foreach (var type in new[] { penalty }.Concat(penalty.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
        foreach (var method in AccessTools.GetDeclaredMethods(type).Where(m => m.GetMethodBody() != null))
            harmony.Patch(method, transpiler: Hook(nameof(PenaltyDisplay)));
    }

    private static Type RequiredType(string name) => GameBridge.RuntimeType(name) ?? throw new TypeLoadException(name);
    private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(StatPatches), name);
    private static void PatchGetter(Harmony harmony, Type type, string name, string hook) =>
        harmony.Patch(AccessTools.PropertyGetter(type, name) ?? throw new MissingMemberException(type.FullName, name),
            postfix: Hook(hook));

    // Armor parts inside an enhanced armor's slot (plates, soft inserts, visors) get
    // its effect while inserted. A plate's own enhancement counts if it is higher.
    internal static double Amount(object item, EnhancePath path, bool inherit = false)
    {
        // The root of EFT's inventory can point to itself. All parent walks have
        // a hard bound; never recurse through the inventory graph.
        var best = 0.0;
        for (var depth = 0; item != null && depth < (inherit ? 64 : 1); depth++)
        {
            var id = GameBridge.Get(item, "Id")?.ToString();
            if (id == null) break;
            if (Registry.Find(id, path) != null) best = Math.Max(best, Registry.Amount(id, path));
            if (!inherit) break;
            var parent = GameBridge.SlotParent(item);
            if (ReferenceEquals(item, parent)) break;
            item = parent;
        }
        return best;
    }

    // Id whose badge an armor part shows: its own, or the armor it is inserted into
    // when that one is enhanced further. Loose items in a pocket never inherit.
    internal static string BadgeOwner(object item, string id)
    {
        var owner = id;
        if (!GameBridge.Eligible(item) || GameBridge.IsWeapon(item)) return owner;
        var parent = GameBridge.SlotParent(item);
        for (var depth = 0; parent != null && !ReferenceEquals(parent, item) && depth < 64; depth++)
        {
            var parentId = GameBridge.Get(parent, "Id")?.ToString();
            if (parentId == null) break;
            if (!GameBridge.IsWeapon(parent) && ArmorLevel(parentId) > ArmorLevel(owner)) owner = parentId;
            item = parent;
            parent = GameBridge.SlotParent(item);
        }
        return owner;
    }
    private static int ArmorLevel(string id) => (Registry.Find(id, EnhancePath.ArmorDurability)?.Level ?? 0)
        + (Registry.Find(id, EnhancePath.Mobility)?.Level ?? 0);

    private static void Recoil(object __instance, ref float __result)
    {
        var amount = Amount(__instance, EnhancePath.Recoil);
        if (amount > 0) __result = EnhanceEffects.RecoilDelta(__result, amount);
    }
    private static void Ergonomics(object __instance, ref float __result)
    {
        var amount = Amount(__instance, EnhancePath.Ergonomics);
        if (amount > 0) __result = EnhanceEffects.Ergonomics(__result, amount);
    }
    private static void Precision(object __instance, ref float __result)
    {
        var amount = Amount(__instance, EnhancePath.Precision);
        if (amount > 0) __result = EnhanceEffects.Reduce(__result, amount);
    }
    private static void Mobility(object __instance, ref float __result)
    {
        var amount = Amount(GameBridge.Get(__instance, "Item"), EnhancePath.Mobility, true);
        if (amount > 0 && __result < 0) __result = EnhanceEffects.Reduce(__result, amount);
    }
    private static void Durability(object __instance, ref float __0)
    {
        var amount = Amount(GameBridge.Get(__instance, "Item"), EnhancePath.ArmorDurability, true);
        if (amount > 0 && __0 > 0) __0 = EnhanceEffects.Reduce(__0, amount);
    }
    // Last loaded backend session, for the background sync of mail-returned gear.
    internal static object Session;
    private static void ProfilesPrefix() { Registry.Clear(); ItemMarkers.Clear(); }
    private static void ProfilesPostfix(object __instance, ref Task __result)
    {
        Session = __instance;
        __result = AfterProfiles(__result, __instance);
    }
    private static async Task AfterProfiles(Task original, object session)
    {
        await original;
        if (Plugin.Instance != null) await Plugin.Instance.SyncEffects(session);
    }

    private static float DisplayPenalty(object component, float value)
    {
        var amount = Amount(GameBridge.Get(component, "Item"), EnhancePath.Mobility, true);
        return amount > 0 && value < 0 ? EnhanceEffects.Reduce(value, amount) : value;
    }
    private static IEnumerable<CodeInstruction> PenaltyDisplay(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var field = list[i];
            if (i + 1 < list.Count && field.opcode == OpCodes.Ldfld && field.operand is FieldInfo info
                && info.Name == "Template" && info.DeclaringType.FullName == "EFT.InventoryLogic.EquipmentPenaltyComponent"
                && list[i + 1].operand is MethodInfo getter &&
                (getter.Name == "get_SpeedPenaltyPercent" || getter.Name == "get_MousePenalty" || getter.Name == "get_WeaponErgonomicPenalty"))
            {
                var duplicate = new CodeInstruction(OpCodes.Dup);
                duplicate.MoveLabelsFrom(field);
                duplicate.MoveBlocksFrom(field);
                yield return duplicate;
                yield return field;
                yield return list[++i];
                yield return new CodeInstruction(OpCodes.Call, typeof(StatPatches).GetMethod(nameof(DisplayPenalty), BindingFlags.Static | BindingFlags.NonPublic));
            }
            else yield return field;
        }
    }
}
