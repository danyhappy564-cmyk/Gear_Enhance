using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GearEnhance;

// The hideout (3D) hands the PMC a copy of its equipment with new ids (in game 2026-10-24: the
// range K2 was 6adcb94a…, the profile's 6ad38711…), so no record matched and nothing applied.
// In the hideout world only, an item under equipment other than the PMC's own maps to the item
// at the same slot path in the PMC's equipment when the templates match. Raid items keep their
// ids and are never mapped (bots' equipment must not borrow the player's records).
internal static class CloneLinks
{
    private sealed class Link { internal int Version = -1; internal string Id; }
    // Original(): the original's id, null for a settled "no copy", Pending when not settled yet
    // (no slot yet, no world player yet) so the next read tries again instead of caching it.
    private const string Pending = "\0";
    private static readonly ConditionalWeakTable<object, Link> links = new ConditionalWeakTable<object, Link>();
    private static readonly ConditionalWeakTable<object, Link>.CreateValueCallback newLink = _ => new Link();
    private static Type slotType;

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

    // In game 2026-10-24 the first version (InRaid false + world MainPlayer's equipment) still
    // left the range K2 unlinked. The hideout scene is now told by its own world type, which no
    // raid uses, and the first answer per reason is logged once.
    private static string Original(object item)
    {
        var world = World();
        if (world == null) return null;
        if (!InHideout(world)) return null;
        slotType ??= GameBridge.RuntimeType("EFT.InventoryLogic.Slot");
        var own = GameBridge.Get(GameBridge.Get(GameBridge.Get(StatPatches.Session, "Profile"), "Inventory"), "Equipment");
        if (own == null || slotType == null) return Why("PMC 장비 없음", Pending);
        // Slot names from the item up to its equipment root. Bounded: no cycles.
        var path = new List<string>();
        var current = item;
        for (var depth = 0; depth < 16; depth++)
        {
            var container = GameBridge.Get(GameBridge.Get(current, "CurrentAddress"), "Container");
            if (container == null || !slotType.IsInstanceOfType(container)) break;
            path.Add(GameBridge.Get(container, "ID") as string);
            current = GameBridge.Get(container, "ParentItem");
            if (current == null) return Why("슬롯 부모 없음", Pending);
        }
        // A copy read before it is slotted is tried again; loose items stay as they are.
        if (path.Count == 0) return Pending;
        if (ReferenceEquals(current, own)) return Why("원본 장비(연결 불필요)", null);
        // The hideout world holds only the PMC, so any other equipment root is its copy.
        if (!(GameBridge.Get(current, "AllSlots") is IEnumerable)) return Why("장비 루트 아님: " + current.GetType().Name, null);
        var target = own;
        for (var index = path.Count - 1; index >= 0 && target != null; index--)
            target = GameBridge.Get(Slot(target, path[index]), "ContainedItem");
        if (target == null) return Why("원본 같은 칸 비었음: " + string.Join("/", path), null);
        if (ReferenceEquals(target, item)) return null;
        if (GameBridge.Get(target, "StringTemplateId") as string != GameBridge.Get(item, "StringTemplateId") as string)
            return Why("원본 같은 칸 템플릿 다름: " + string.Join("/", path), null);
        var id = GameBridge.Get(target, "Id")?.ToString();
        return Why("연결 성공 " + string.Join("/", path) + " → " + id, id);
    }

    internal static string WorldName() => World()?.GetType().Name ?? "없음";

    private static bool InHideout(object world)
    {
        for (var type = world.GetType(); type != null; type = type.BaseType)
            if (type.Name == "HideoutGameWorld") return true;
        return false;
    }

    private static readonly HashSet<string> reasons = new HashSet<string>();
    private static string Why(string reason, string result)
    {
        if (reasons.Count < 64 && reasons.Add(reason)) Plugin.LogInfo("은신처 장비 연결: " + reason);
        return result;
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
