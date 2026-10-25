using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace GearEnhance;

// 1.4.0 mythic effects in the game (design 5.2, 5.3, 12.3), each only for the player's own gear:
// - weapon: every shot it fires (BallisticsCalculator.CreateShot) carries more damage and
//   penetration; the ammo itself is untouched, so the same rounds fired elsewhere are normal.
// - armor: the drawn extra zones count as covered (ArmorComponent.ShotMatches) by its best
//   soft armor part, at that part's class and material.
// - armor: hits the player takes (bullets, fragments, explosions, melee) are reduced.
// All three methods are large or virtual, so the JIT does not inline them (kb 2.61).
internal static class MythicPatches
{
    private static EffectRegistry Registry => StatPatches.Registry;
    private static FieldInfo shotDamage, shotPenetration;
    private static PropertyInfo ownerRoot, worldInstance, plateMask;
    private static Func<object> readWorld;
    private static PropertyInfo readWorldProperty;
    private static Type armorType;
    private static MethodInfo getSlot;
    private static object[] wornSlots;
    private static object world, mainEquipment;
    private static string mainProfile;
    private static bool reported;
    // Worn armor slots whose mythic level reduces damage: the highest one counts, never a sum.
    private static readonly string[] ArmorSlots = { "ArmorVest", "TacticalVest", "Headwear", "FaceCover", "Eyewear" };

    internal static List<string> Install(Harmony harmony)
    {
        var failed = new List<string>();
        void Try(string name, Action install)
        {
            try { install(); }
            catch (Exception ex) { failed.Add(name + ": " + ex.GetBaseException().Message); }
        }
        var gameWorld = GameBridge.RuntimeType("EFT.GameWorld");
        var singleton = gameWorld == null ? null : GameBridge.RuntimeType("Comfort.Common.Singleton`1")?.MakeGenericType(gameWorld);
        worldInstance = singleton == null ? null : AccessTools.Property(singleton, "Instance");
        Try("신화 탄 불이익 감소 / mythic ammo penalties", () => AmmoPatches.Install(harmony));
        Try("신화 탄 데미지·관통 / mythic bullets", () =>
        {
            var shot = Required("EFT.Ballistics.Shot");
            shotDamage = AccessTools.Field(shot, "Damage") ?? throw new MissingFieldException("Shot.Damage");
            shotPenetration = AccessTools.Field(shot, "PenetrationPower") ?? throw new MissingFieldException("Shot.PenetrationPower");
            if (shotDamage.FieldType != typeof(float) || shotPenetration.FieldType != typeof(float)) throw new MissingFieldException("Shot.Damage float");
            harmony.Patch(GameBridge.RequiredMethod(Required("EFT.Ballistics.BallisticsCalculator"), "CreateShot", 8), postfix: Hook(nameof(Bullet)));
        });
        Try("신화 추가 보호 부위 / mythic zones", () =>
        {
            armorType = Required("EFT.InventoryLogic.ArmorComponent");
            plateMask = AccessTools.Property(armorType, "ArmorPlateCollidersMask") ?? throw new MissingMemberException("ArmorComponent.ArmorPlateCollidersMask");
            ownerRoot = Required("EFT.InventoryLogic.IItemOwner").GetProperty("RootItem") ?? throw new MissingMemberException("IItemOwner.RootItem");
            harmony.Patch(GameBridge.RequiredMethod(armorType, "ShotMatches", 2), postfix: Hook(nameof(Zone)));
        });
        Try("신화 받는 피해 감소 / mythic damage taken", () =>
        {
            var slots = Required("EFT.InventoryLogic.EquipmentSlot");
            wornSlots = Array.ConvertAll(ArmorSlots, name => Enum.Parse(slots, name));
            getSlot = GameBridge.RequiredMethod(Required("EFT.InventoryLogic.InventoryEquipment"), "GetSlot", 1);
            harmony.Patch(GameBridge.RequiredMethod(Required("EFT.HealthSystem.ActiveHealthController"), "ApplyDamage", 3), prefix: Hook(nameof(Taken)));
        });
        return failed;
    }

    private static Type Required(string name) => GameBridge.RuntimeType(name) ?? throw new TypeLoadException(name);
    private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(MythicPatches), name);

    private static void Report(Exception ex)
    {
        if (reported) return;
        reported = true;
        Plugin.LogError("신화 효과 오류(이번 판은 효과 없이 계속) / mythic effect error: " + ex.GetBaseException().Message);
    }

    // The raid's main player, cached per GameWorld (the hideout and each raid make a new one).
    internal static bool Main(out string profile, out object equipment)
    {
        // Ammo readers share this check on hot paths; bind the world getter once instead
        // of reflection Invoke on every read. Tests can still supply an absent world.
        if (!ReferenceEquals(readWorldProperty, worldInstance))
        {
            readWorld = worldInstance == null ? null : (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), worldInstance.GetGetMethod(true));
            readWorldProperty = worldInstance;
        }
        var current = readWorld?.Invoke();
        if (!ReferenceEquals(current, world) || mainProfile == null)
        {
            world = current;
            var player = GameBridge.Get(current, "MainPlayer");
            mainProfile = GameBridge.Get(player, "ProfileId")?.ToString();
            mainEquipment = GameBridge.Get(GameBridge.Get(player, "Inventory"), "Equipment");
        }
        profile = mainProfile; equipment = mainEquipment;
        return profile != null;
    }

    private static void Bullet(object __result, string player, object weapon)
    {
        try
        {
            if (!Registry.HasMythic || __result == null || weapon == null || player == null) return;
            var level = Registry.MythicLevel(CloneLinks.EffectId(weapon) ?? "");
            if (level <= 0 || !Main(out var profile, out _) || player != profile) return;
            var factor = (float)(1 + MythicEffects.WeaponPercent(level, Registry.MythicTuning) / 100);
            shotDamage.SetValue(__result, (float)shotDamage.GetValue(__result) * factor);
            shotPenetration.SetValue(__result, (float)shotPenetration.GetValue(__result) * factor);
        }
        catch (Exception ex) { Report(ex); }
    }

    private static void Zone(object __instance, object[] __args, ref bool __result)
    {
        try
        {
            if (__result || !Registry.HasMythic || __args == null || __args.Length < 2) return;
            // A plate collider check belongs to the plates; extra zones are body colliders.
            if (Convert.ToInt64(__args[1]) != 0) return;
            // Only on the player's own body (checked first: most hits are on bots); a bot wearing
            // looted mythic armor gets nothing.
            if (!Main(out _, out var equipment) || equipment == null) return;
            var item = GameBridge.Get(__instance, "Item");
            var owner = GameBridge.Get(item, "Owner");
            if (owner == null || !ReferenceEquals(ownerRoot.GetValue(owner), equipment)) return;
            var holder = Holder(item);
            if (holder == null) return;
            var zone = __args[0]?.ToString();
            if (zone == null || Array.IndexOf(Registry.MythicZones(holder), zone) < 0) return;
            __result = true;
        }
        catch (Exception ex) { Report(ex); }
    }

    // Id of the mythic armor whose extra zones this component carries, or null. The carrier is
    // the armor itself or one of the parts in its slots: the highest class non-plate armor.
    private static readonly Dictionary<object, (double At, object Carrier)> carriers = new Dictionary<object, (double, object)>();
    private static string Holder(object item)
    {
        var id = CloneLinks.EffectId(item);
        var root = item;
        if (id == null || Registry.MythicLevel(id) < 0)
        {
            root = GameBridge.SlotParent(item);
            id = root == null ? null : CloneLinks.EffectId(root);
            if (id == null || Registry.MythicLevel(id) < 0) return null;
        }
        if (Registry.MythicZones(id).Length == 0) return null;
        var now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        // Parts can be swapped in the stash; a short cache keeps one hit's checks cheap.
        if (!carriers.TryGetValue(root, out var cached) || now - cached.At > 2)
        {
            if (carriers.Count > 64) carriers.Clear();
            carriers[root] = cached = (now, Carrier(root));
        }
        return ReferenceEquals(cached.Carrier, item) ? id : null;
    }

    private static object Carrier(object root)
    {
        object best = null;
        var bestClass = 0;
        void Consider(object candidate)
        {
            var component = Armor(candidate);
            if (component == null || Convert.ToInt64(plateMask.GetValue(component)) != 0) return;
            var armorClass = Convert.ToInt32(GameBridge.Get(component, "ArmorClass") ?? 0);
            if (armorClass > bestClass) { best = candidate; bestClass = armorClass; }
        }
        Consider(root);
        if (GameBridge.Get(root, "AllSlots") is IEnumerable slots)
            foreach (var slot in slots)
            {
                var part = GameBridge.Get(slot, "ContainedItem");
                if (part != null) Consider(part);
            }
        return best;
    }

    private static object Armor(object item)
    {
        if (!(GameBridge.Get(item, "Components") is IEnumerable components)) return null;
        foreach (var component in components) if (armorType.IsInstanceOfType(component)) return component;
        return null;
    }

    private static void Taken(object __instance, object[] __args, ref float damage)
    {
        try
        {
            if (!Registry.HasMythic || damage <= 0 || __args == null || __args.Length < 3) return;
            var type = GameBridge.Get(__args[2], "DamageType");
            if (type == null || (Convert.ToInt32(type) & MythicRules.HitDamageTypes) == 0) return;
            var player = GameBridge.Get(__instance, "Player");
            if (GameBridge.Get(player, "IsYourPlayer") is not true) return;
            var equipment = GameBridge.Get(GameBridge.Get(player, "Inventory"), "Equipment");
            if (equipment == null) return;
            var level = -1;
            foreach (var slot in wornSlots)
            {
                var worn = GameBridge.Get(getSlot.Invoke(equipment, new[] { slot }), "ContainedItem");
                if (worn != null) level = Math.Max(level, Registry.MythicLevel(CloneLinks.EffectId(worn) ?? ""));
            }
            if (level <= 0) return;
            // ApplyDamage(EBodyPart bodyPart, …): head hits get half the reduction.
            var head = __args[0]?.ToString() == "Head";
            damage *= (float)Math.Max(0, 1 - MythicEffects.ArmorPercent(level, Registry.MythicTuning, head) / 100);
        }
        catch (Exception ex) { Report(ex); }
    }
}
