using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

// 1.4.0 status panel (design 1): base stats, then the enhanced ones with the values this attempt
// would change in blue. Rows drop in one by one; numbers are re-read only when something changes.
public sealed partial class Plugin
{
    private string statKey;
    private List<StatRow> statRows = new List<StatRow>();

    private void DrawStatusPanel()
    {
        var rows = StatRows();
        var (offset, alpha) = UiMotion.Panel(Motion, Reduced);
        var x = StatusX + offset;
        var old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        var height = 56 + 14 + 24 + rows.Count * 31 + 18 + 24 + rows.Count * 31 + 16;
        Box(new Rect(x, 0, StatusWidth, height), Panel, Edge);
        Fill(new Rect(x + 1, 1, StatusWidth - 2, 55), HeaderFill);
        Fill(new Rect(x + 1, 56, StatusWidth - 2, 1), Line);
        Label(x + 16, 0, 160, 56, Loc.L("장비 상태", "Gear stats"), new GUIStyle(bodyBold) { fontSize = 15 }, LightGold);
        GUI.enabled = popup == PopupKind.None;
        IconButton(new Rect(x + StatusWidth - 48, 12, 36, 32), refreshIcon, 14, ReplayMotion);
        GUI.enabled = true;
        var y = 70f;
        Label(x + 16, y, 60, 20, Loc.L("원본", "Base"), bodyBold, Muted);
        Label(x + 70, y, StatusWidth - 86, 20, itemName ?? "", new GUIStyle(bodyBold) { fontSize = 15 }, Text);
        y += 24;
        for (var index = 0; index < rows.Count; index++, y += 31) StatLine(x, y, rows[index], index, false);
        y += 18;
        var mythic = MythicLevel;
        Label(x + 16, y, 60, 20, Loc.L("강화 후", "Enhanced"), bodyBold, Muted);
        Label(x + 80, y, StatusWidth - 96, 20, itemId == null ? "—" : mythic >= 0 ? Loc.L("✦ 신화", "✦ Mythic") : StarEvolution.Stars(StarCount),
            new GUIStyle(bodyBold) { fontSize = 15 }, mythic >= 0 ? Myth : StarTint(StarCount));
        y += 24;
        for (var index = 0; index < rows.Count; index++, y += 31) StatLine(x, y, rows[index], index, true);
        GUI.color = old;
    }

    private void StatLine(float x, float y, StatRow row, int index, bool enhanced)
    {
        var (drop, alpha) = UiMotion.Row(Motion, enhanced ? UiMotion.EnhancedRowDelay(index) : UiMotion.BaseRowDelay(index), Reduced);
        var old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        y += drop;
        Fill(new Rect(x + 16, y + 30, StatusWidth - 32, 1), Hex(0x22282e));
        Label(x + 16, y + 4, 120, 24, row.Label, new GUIStyle(sub) { fontSize = 13 });
        if (!enhanced || row.Next == null)
            Label(x + 120, y + 2, StatusWidth - 136, 26, enhanced ? row.Now : row.Base, new GUIStyle(number) { fontSize = 17 }, Text);
        else
        {
            // Changing rows: the current value dims, the new one glows once (mockup glowA: 12 px blue at 0.72 s).
            var glow = UiMotion.ValueGlow(Motion, Reduced);
            Label(x + 120, y + 2, StatusWidth - 222, 26, row.Now, new GUIStyle(number) { fontSize = 17 }, Muted);
            Label(x + StatusWidth - 100, y + 2, 14, 26, "→", tinyRight);
            var next = new Rect(x + StatusWidth - 84, y + 2, 68, 26);
            var style = new GUIStyle(number) { fontSize = 17 };
            if (glow > 0) GlowText(next, row.Next, style, new Color(95 / 255f, 168 / 255f, 1f, 0.95f * glow), 3);
            Label(next.x, next.y, next.width, next.height, row.Next, style, Blue);
        }
        GUI.color = old;
    }

    private List<StatRow> StatRows()
    {
        var key = $"{itemId}|{StatPatches.Registry.Version}|{preview?.QuoteId}|{tab}|{selectedPath}|{transcendTrack}|{Loc.English}|{boostDirtyAt >= 0}";
        if (key == statKey) return statRows;
        statKey = key;
        try
        {
            if (selectedItem == null || itemId == null) statRows = new List<StatRow>();
            else
            {
                var now = Levels(false);
                var next = NextLevels(now);
                statRows = weapon
                    ? StatSheet.Weapon(ReadWeapon(selectedItem), now, next, Tuned, MythicTuned)
                    : StatSheet.Armor(ReadArmor(selectedItem), now, next, Tuned, MythicTuned);
            }
        }
        catch (Exception ex)
        {
            statRows = new List<StatRow>();
            LogDetailed("장비 상태창 수치 읽기 실패: " + ex.GetBaseException().Message);
        }
        return statRows;
    }

    private GearLevels Levels(bool _)
    {
        var id = itemId ?? "";
        return new GearLevels
        {
            Recoil = StatPatches.Registry.Amount(id, EnhancePath.Recoil), Ergonomics = StatPatches.Registry.Amount(id, EnhancePath.Ergonomics),
            Precision = StatPatches.Registry.Amount(id, EnhancePath.Precision), ArmorDurability = StatPatches.Registry.Amount(id, EnhancePath.ArmorDurability),
            Mobility = StatPatches.Registry.Amount(id, EnhancePath.Mobility),
            Second = StatPatches.Registry.TranscendLevel(id, TranscendTrack.Second), Third = StatPatches.Registry.TranscendLevel(id, TranscendTrack.Third),
            Mythic = StatPatches.Registry.MythicLevel(id), Zones = StatPatches.Registry.MythicZones(id).Length
        };
    }

    // What the gear has after a success of this tab's attempt, or null when nothing is offered.
    private GearLevels? NextLevels(GearLevels now)
    {
        var view = preview;
        var id = itemId ?? "";
        var next = now;
        switch (tab)
        {
            case UiTab.Enhance when view != null && view.Action == EnhanceAction.Upgrade && !view.BindLegacy:
                SetAmount(ref next, view.Path, view.NextEffect);
                return next;
            case UiTab.Star2 when view != null:
            case UiTab.Star3 when view != null:
                foreach (var path in weapon ? WeaponTracks : ArmorTracks)
                    SetAmount(ref next, path, EnhanceEffects.Amount(path, StatPatches.Registry.Find(id, path)?.Level ?? 0, Tuning(), view.NextStars));
                return next;
            case UiTab.Transcend when view != null:
                if (view.Transcend == TranscendTrack.Third) next.Third = view.NextLevel; else next.Second = view.NextLevel;
                return next;
            case UiTab.Mythic when view != null && view.Action == EnhanceAction.Mythic:
                next.Mythic = view.NextLevel;
                return next;
            case UiTab.Mythic when view != null && view.Action == EnhanceAction.Mystery:
                next.Mythic = 0;
                next.Zones = Math.Min(MythicTuned.ExtraZones, view.ZoneOptions?.Length ?? 0);
                return next;
        }
        return null;
    }

    private static void SetAmount(ref GearLevels levels, EnhancePath path, double amount)
    {
        switch (path)
        {
            case EnhancePath.Recoil: levels.Recoil = amount; break;
            case EnhancePath.Ergonomics: levels.Ergonomics = amount; break;
            case EnhancePath.Precision: levels.Precision = amount; break;
            case EnhancePath.ArmorDurability: levels.ArmorDurability = amount; break;
            case EnhancePath.Mobility: levels.Mobility = amount; break;
        }
    }

    // The game's own values with every Gear Enhance hook bypassed (StatPatches.Raw).
    private static WeaponStats ReadWeapon(object item)
    {
        StatPatches.Raw = true;
        try
        {
            var template = GameBridge.Get(item, "Template");
            var repairable = Component(item, "RepairableComponent");
            var center = GameBridge.RequiredMethod(item.GetType(), "GetTotalCenterOfImpact", 1).Invoke(item, new object[] { false });
            return new WeaponStats
            {
                RecoilUp = Float(GameBridge.Get(template, "RecoilForceUp")), RecoilBack = Float(GameBridge.Get(template, "RecoilForceBack")),
                RecoilDelta = Float(GameBridge.Get(item, "RecoilDelta")), Ergonomics = Float(GameBridge.Get(item, "ErgonomicsTotal")),
                CenterOfImpact = Float(center), FireRate = (int)Float(GameBridge.Get(item, "FireRate")), Automatic = TranscendPatches.Automatic(item),
                Durability = Float(GameBridge.Get(repairable, "Durability")), MaxDurability = Float(GameBridge.Get(repairable, "MaxDurability"))
            };
        }
        finally { StatPatches.Raw = false; }
    }

    private static ArmorStats ReadArmor(object item)
        => ArmorStatus.Read(item);

    private static object Component(object item, string typeName) => ArmorStatus.Component(item, typeName);

    private static float Float(object value) => value switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        null => float.NaN,
        _ => float.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : float.NaN
    };
}

// Reflection-only reader, also exercised without loading Unity or the game in client-tests.
internal static class ArmorStatus
{
    internal static ArmorStats Read(object item)
    {
        var wasRaw = StatPatches.Raw;
        StatPatches.Raw = true;
        try
        {
            var result = new ArmorStats();
            var penalty = GameBridge.Get(Component(item, "EquipmentPenaltyComponent"), "Template");
            result.Speed = Float(GameBridge.Get(penalty, "SpeedPenaltyPercent"));
            result.Turn = Float(GameBridge.Get(penalty, "MousePenalty"));
            result.Ergonomics = Float(GameBridge.Get(penalty, "WeaponErgonomicPenalty"));
            var zones = new HashSet<string>();
            var seen = new HashSet<object>();
            var plate = 0;
            // Slots are immediate children; AllSlots already flattens descendants. Never read grids.
            var queue = new Queue<(object Item, int Depth, bool Locked)>();
            queue.Enqueue((item, 0, true));
            for (var visited = 0; queue.Count > 0 && visited < 64; visited++)
            {
                var (current, depth, locked) = queue.Dequeue();
                if (current == null || !seen.Add(current)) continue;
                var component = Component(current, "ArmorComponent");
                if (component != null)
                {
                    var hasZones = false;
                    if (GameBridge.Get(component, "ArmorColliders") is IEnumerable colliders)
                        foreach (var collider in colliders)
                            if (collider != null) { zones.Add("soft:" + collider); hasZones = true; }
                    var hasPlates = false;
                    var getPlates = component.GetType().GetMethod("GetArmorPlateColliders", Type.EmptyTypes);
                    if (getPlates?.Invoke(component, null) is IEnumerable collidersPlate)
                        foreach (var collider in collidersPlate)
                            if (collider != null) { zones.Add("plate:" + collider); hasPlates = true; }
                    result.Zones = hasZones || hasPlates ? zones.Count : result.Zones;
                    var armorClass = Float(GameBridge.Get(component, "ArmorClass"));
                    if (Finite(armorClass) && armorClass > 0 && armorClass < int.MaxValue)
                    {
                        if (depth > 0 && !locked && hasPlates) plate = Math.Max(plate, (int)armorClass);
                        else result.ArmorClass = Math.Max(result.ArmorClass, (int)armorClass);
                        var repairable = GameBridge.Get(component, "Repairable") ?? Component(current, "RepairableComponent");
                        var templateMax = Float(GameBridge.Get(repairable, "TemplateDurability"));
                        var durability = Float(GameBridge.Get(repairable, "Durability"));
                        var max = Float(GameBridge.Get(repairable, "MaxDurability"));
                        if (Finite(templateMax) && templateMax >= 0)
                            result.TemplateMax = (Finite(result.TemplateMax) ? result.TemplateMax : 0) + templateMax;
                        if (Finite(durability) && durability >= 0)
                        {
                            result.Parts ??= new List<ArmorCondition>();
                            result.Parts.Add(new ArmorCondition { Durability = durability, Max = Finite(max) ? max : durability,
                                TemplateMax = templateMax, Grant = StatPatches.Registry.Grant(GameBridge.Get(current, "Id")?.ToString() ?? "") });
                        }
                    }
                    // The carrier template replaces its own armor penalty; children contribute once.
                    if (depth > 0 || penalty == null)
                    {
                        result.Speed = Add(result.Speed, Float(GameBridge.Get(component, "SpeedPenalty")));
                        result.Turn = Add(result.Turn, Float(GameBridge.Get(component, "MousePenalty")));
                        result.Ergonomics = Add(result.Ergonomics, Float(GameBridge.Get(component, "WeaponErgonomicPenalty")));
                    }
                }
                if (depth >= 3 || !(GameBridge.Get(current, "Slots") is IEnumerable slots)) continue;
                foreach (var slot in slots)
                {
                    var contained = GameBridge.Get(slot, "ContainedItem");
                    if (contained != null) queue.Enqueue((contained, depth + 1, GameBridge.Get(slot, "Locked") is true));
                }
            }
            result.PlateClass = plate > result.ArmorClass ? plate : 0;
            return result;
        }
        finally { StatPatches.Raw = wasRaw; }
    }

    internal static object Component(object item, string typeName)
    {
        if (!(GameBridge.Get(item, "Components") is IEnumerable components)) return null;
        foreach (var component in components)
            for (var type = component?.GetType(); type != null; type = type.BaseType)
                if (type.Name == typeName) return component;
        return null;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static float Add(float total, float value) => Finite(value) ? (Finite(total) ? total : 0) + value : total;

    private static float Float(object value) => value switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        null => float.NaN,
        _ => float.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : float.NaN
    };
}
