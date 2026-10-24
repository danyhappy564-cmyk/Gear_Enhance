using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GearEnhance;

// The hideout (3D) hands the PMC a copy of its equipment with new ids (in game 2026-10-24: the
// range K2 was 6adcb94a…, the profile's 6ad38711…), so no record matched and nothing applied.
// Outside raids an item under equipment other than the PMC's own maps to the item at the same
// slot path in the PMC's equipment when the templates match. Raid items keep their ids and are
// never mapped (bots' equipment must not borrow the player's records).
internal static class CloneLinks
{
    private sealed class Link { internal int Version = -1; internal string Id; }
    // Original(): the original's id, null for a settled "no copy", Pending when not settled yet
    // (no slot yet, no world player yet) so the next read tries again instead of caching it.
    private const string Pending = "\0";
    private static readonly ConditionalWeakTable<object, Link> links = new ConditionalWeakTable<object, Link>();
    private static readonly ConditionalWeakTable<object, Link>.CreateValueCallback newLink = _ => new Link();
    private static Type slotType, equipmentType, statusType;

    // Id the records are kept under: the item's own, or its original for a hideout copy.
    // Cached per item instance until the records change, so hot getters pay one lookup.
    internal static string EffectId(object item) => EffectId(item, out _);

    // `settled` is false while the answer may still change (callers with their own cache
    // must not keep it).
    internal static string EffectId(object item, out bool settled)
    {
        settled = true;
        var id = GameBridge.Get(item, "Id")?.ToString();
        if (id == null || StatPatches.Registry.Version == 0) return id;
        var link = links.GetValue(item, newLink);
        var version = StatPatches.Registry.Version;
        if (link.Version == version) return link.Id ?? id;
        string found;
        try { found = Original(item); }
        catch (Exception) { found = Pending; }
        if (ReferenceEquals(found, Pending)) { settled = false; return id; }
        link.Id = found;
        link.Version = version;
        return found ?? id;
    }

    private static string Original(object item)
    {
        statusType ??= GameBridge.RuntimeType("EFT.InGameStatus");
        if (GameBridge.Get(statusType, "InRaid") is not false) return null;
        slotType ??= GameBridge.RuntimeType("EFT.InventoryLogic.Slot");
        equipmentType ??= GameBridge.RuntimeType("EFT.InventoryLogic.InventoryEquipment");
        var own = GameBridge.Get(GameBridge.Get(GameBridge.Get(StatPatches.Session, "Profile"), "Inventory"), "Equipment");
        if (own == null || slotType == null || equipmentType == null) return Pending;
        // Slot names from the item up to its equipment root. Bounded: no cycles.
        var path = new List<string>();
        var current = item;
        for (var depth = 0; depth < 16; depth++)
        {
            var container = GameBridge.Get(GameBridge.Get(current, "CurrentAddress"), "Container");
            if (container == null || !slotType.IsInstanceOfType(container)) break;
            path.Add(GameBridge.Get(container, "ID") as string);
            current = GameBridge.Get(container, "ParentItem");
            if (current == null) return Pending;
        }
        // Loose items are settled as they are, except in the hideout scene, where a copy may
        // be read before it is slotted (a world player exists only there outside raids).
        if (path.Count == 0) return GameBridge.Get(World(), "MainPlayer") == null ? null : Pending;
        if (ReferenceEquals(current, own) || !equipmentType.IsInstanceOfType(current)) return null;
        // Only the equipment of the player the world is showing (the hideout PMC), never
        // another owner's that happens to share slots and templates.
        var player = GameBridge.Get(World(), "MainPlayer");
        var shown = GameBridge.Get(GameBridge.Get(GameBridge.Get(player, "Profile"), "Inventory"), "Equipment");
        if (shown == null) return Pending;
        if (!ReferenceEquals(current, shown)) return null;
        var target = own;
        for (var index = path.Count - 1; index >= 0 && target != null; index--)
            target = GameBridge.Get(Slot(target, path[index]), "ContainedItem");
        if (target == null || ReferenceEquals(target, item)
            || GameBridge.Get(target, "StringTemplateId") as string != GameBridge.Get(item, "StringTemplateId") as string) return null;
        return GameBridge.Get(target, "Id")?.ToString();
    }

    private static Type worldSingleton;
    private static object World()
    {
        if (worldSingleton == null)
        {
            var world = GameBridge.RuntimeType("EFT.GameWorld");
            worldSingleton = world == null ? null : GameBridge.RuntimeType("Comfort.Common.Singleton`1")?.MakeGenericType(world);
        }
        return worldSingleton == null ? null : GameBridge.Get(worldSingleton, "Instance");
    }

    private static object Slot(object compound, string name)
    {
        if (!(GameBridge.Get(compound, "AllSlots") is IEnumerable slots)) return null;
        foreach (var slot in slots)
            if (GameBridge.Get(slot, "ID") as string == name) return slot;
        return null;
    }
}
