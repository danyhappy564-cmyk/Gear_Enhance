using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GearEnhance;

// Visual gold tint and badge only. Never create or modify the native repair buff.
internal static class ItemMarkers
{
    private static readonly HashSet<object> views = new HashSet<object>();
    // Ids already shown once without a record. A new one (an insurance or flea return
    // taken from mail gets a new id) asks for one debounced state sync.
    private static readonly HashSet<string> unknown = new HashSet<string>();
    private static MethodInfo update, updateColor;
    private static readonly string[] LoadingStates = { "IsBeingLoadedAmmo", "IsBeingLoadedMagazine", "IsBeingUnloadedMagazine" };
    private static FieldInfo background;
    internal static void Install(Harmony harmony)
    {
        var grid = GameBridge.RuntimeType("EFT.UI.DragAndDrop.GridItemView") ?? throw new TypeLoadException("GridItemView");
        var basic = GameBridge.RuntimeType("EFT.UI.DragAndDrop.ItemView") ?? throw new TypeLoadException("ItemView");
        update = GameBridge.RequiredMethod(grid, "UpdateInfo", 0);
        updateColor = GameBridge.RequiredMethod(basic, "UpdateColor", 0);
        background = AccessTools.Field(basic, "BackgroundColor") ?? throw new MissingFieldException("ItemView.BackgroundColor");
        harmony.Patch(update, postfix: new HarmonyMethod(typeof(ItemMarkers), nameof(AfterUpdate)));
        harmony.Patch(GameBridge.RequiredMethod(grid, "GetItemName", 1),
            postfix: new HarmonyMethod(typeof(ItemMarkers), nameof(Name)));
        harmony.Patch(GameBridge.RequiredMethod(basic, "Kill", 0),
            prefix: new HarmonyMethod(typeof(ItemMarkers), nameof(Kill)));
    }
    // ItemView.Init calls UpdateInfo before SetActive(true). An exception escaping
    // these cosmetic postfixes leaves the item in the grid with no visible view.
    private static bool failed;
    private static void Report(Exception ex)
    {
        if (failed) return;
        failed = true;
        Plugin.LogError("강화 표식 오류(장비 표시는 유지): " + ex);
    }
    private static void AfterUpdate(object __instance)
    {
        try { Mark(__instance); }
        catch (Exception ex) { Report(ex); }
        try
        {
            // After a failure a pooled cell may still carry an old badge: keep hiding them.
            if (badgeFailed) InsuranceBadge.Hide(__instance);
            else Badge(__instance);
        }
        // A missing uGUI type would throw on every cell: report once and stop drawing badges.
        catch (Exception ex)
        {
            if (!badgeFailed) Plugin.LogError("강화 보험 배지 오류(배지만 숨김): " + ex);
            badgeFailed = true;
            try { InsuranceBadge.Hide(__instance); } catch (Exception) { }
        }
    }
    private static bool badgeFailed;
    private static void Mark(object __instance)
    {
        // Registered views are refreshed only after a state/receipt change, never in Update.
        if (views.Count < 2048) views.Add(__instance);
        var item = GameBridge.Get(__instance, "Item");
        var id = GameBridge.Get(item, "Id")?.ToString();
        if (id != null && StatPatches.Registry.TotalLevel(id) == 0)
        {
            if (unknown.Count >= 65536) unknown.Clear();
            if (unknown.Add(id)) Plugin.RequestSync();
        }
        var owner = id == null ? null : StatPatches.BadgeOwner(item, id);
        if (owner == null || StatPatches.Registry.TotalLevel(owner) == 0
            || GameBridge.Get(__instance, "Examined") is not true
            || GameBridge.Get(__instance, "_hasResizeConflict") is true
            || GameBridge.Get(__instance, "IsConflicting") is true) return;
        foreach (var state in LoadingStates)
            if (GameBridge.Get(GameBridge.Get(__instance, state), "Value") is true) return;
        var image = GameBridge.Get(__instance, "MainImage");
        if (GameBridge.Get(image, "color") is not Color color || color.g < 0.9f || color.b < 0.9f) return;
        // Same gold background used by GridItemView for native repair buffs.
        // One background color per cell: transcended gear shows cyan over its star color (approved mockup).
        background.SetValue(__instance, StatPatches.Registry.TranscendTotal(owner) > 0 ? new Color(64f / 255, 209f / 255, 217f / 255, 56f / 255)
            : StatPatches.Registry.StarLevel(owner) >= 3
            ? new Color(232f / 255, 70f / 255, 70f / 255, 65f / 255)
            : StatPatches.Registry.IsEvolved(owner) ? new Color(163f / 255, 103f / 255, 232f / 255, 65f / 255)
            : new Color(209f / 255, 153f / 255, 45f / 255, 32f / 255));
        updateColor.Invoke(__instance, null);
    }
    private static void Name(object __instance, ref string __result)
    {
        try
        {
            if (GameBridge.Get(__instance, "Examined") is not true) return;
            var item = GameBridge.Get(__instance, "Item");
            var id = GameBridge.Get(item, "Id")?.ToString();
            var owner = id == null ? null : StatPatches.BadgeOwner(item, id);
            var badge = owner == null ? "" : StatPatches.Registry.Badge(owner);
            if (badge.Length > 0) __result += " <color=" + StarEvolution.ColorHex(StatPatches.Registry.StarLevel(owner)) + ">" + badge + "</color>";
            var transcend = badge.Length == 0 ? 0 : StatPatches.Registry.TranscendTotal(owner);
            if (transcend > 0) __result += " <color=#40D1D9>" + Loc.L("초월 ", "Transcend ") + transcend + "</color>";
        }
        catch (Exception ex) { Report(ex); }
    }
    // Only the enhanced gear itself: plates share the armor's star badge, not its insurance.
    private static void Badge(object view)
    {
        var item = GameBridge.Get(view, "Item");
        var id = GameBridge.Get(item, "Id")?.ToString();
        if (id == null || StatPatches.Registry.TotalLevel(id) == 0 || GameBridge.Get(view, "Examined") is not true || GameBridge.IsArmorPart(item))
            InsuranceBadge.Hide(view);
        else InsuranceBadge.Show(view, StatPatches.Registry.Coverage(id));
    }
    private static void Kill(object __instance) => views.Remove(__instance);
    internal static void Refresh(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        foreach (var view in views.ToArray())
        {
            if (view is UnityEngine.Object instance && instance == null) { views.Remove(view); continue; }
            // Plates inside the enhanced armor show its badge, so they redraw with it.
            var item = GameBridge.Get(view, "Item");
            if (GameBridge.Get(item, "Id")?.ToString() == itemId || InsertedIn(item, itemId))
                update?.Invoke(view, null);
        }
    }
    // After a sync or a language change: redraw only views that carry a record.
    internal static void RefreshAll()
    {
        foreach (var view in views.ToArray())
        {
            if (view is UnityEngine.Object instance && instance == null) { views.Remove(view); continue; }
            var item = GameBridge.Get(view, "Item");
            var id = GameBridge.Get(item, "Id")?.ToString();
            if (id != null && StatPatches.Registry.TotalLevel(StatPatches.BadgeOwner(item, id)) > 0) update?.Invoke(view, null);
        }
    }
    private static bool InsertedIn(object item, string itemId)
    {
        var parent = GameBridge.SlotParent(item);
        for (var depth = 0; parent != null && !ReferenceEquals(parent, item) && depth < 64; depth++)
        {
            if (GameBridge.Get(parent, "Id")?.ToString() == itemId) return true;
            item = parent;
            parent = GameBridge.SlotParent(item);
        }
        return false;
    }
    internal static void Clear() { views.Clear(); unknown.Clear(); }
}
