using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GearEnhance;

// User 2026-10-26: the right panel's second tab, "탄 비교", for weapons. It finds the round loaded in
// the gun (the chamber first, then the magazine's top round) and shows it value by value: as the
// game has it, with this gun's mythic level now, and after the next level. The numbers come from the
// same rules as the effect itself (MythicEffects, AmmoPatches.NeutralInt/NeutralFloat).
public sealed partial class Plugin
{
    private bool ammoTab;
    private double ammoReadAt = -100;
    private string ammoKey, ammoName;
    private readonly List<(string Label, string Base, string Now, string Next, bool Changes)> ammoRows = new List<(string, string, string, string, bool)>();
    private const int AmmoRowHeight = 28;

    private float AmmoPanelHeight() => 56 + 14 + 24 + 22 + Math.Max(1, ammoRows.Count) * AmmoRowHeight + 16 + 44;

    // Two header tabs in place of the title (weapons only); armor keeps the one title.
    private void DrawStatusTabs(float x)
    {
        var width = (StatusWidth - 32 - 48 - 8) / 2;
        for (var index = 0; index < 2; index++)
        {
            var on = (index == 1) == ammoTab;
            var rect = new Rect(x + 16 + index * (width + 4), 14, width, 28);
            var hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
            Box(rect, on ? Hex(0x1d2a38) : hovered ? Hex(0x1b2025) : HeaderFill, on ? Blue : Hex(0x333a42));
            Label(rect.x, rect.y, rect.width, rect.height, index == 0 ? Loc.L("장비 상태", "Gear stats") : Loc.L("탄 비교", "Ammo"),
                new GUIStyle(bodyBoldCenter) { fontSize = 13 }, on ? LightGold : Muted);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && popup == PopupKind.None && on == false)
            {
                var ammo = index == 1;
                main.Enqueue(() => { ammoTab = ammo; ammoReadAt = -100; ReplayMotion(); });
            }
        }
    }

    private void DrawAmmoRows(float x)
    {
        ReadAmmo();
        var level = MythicLevel;
        var y = 70f;
        Label(x + 16, y, 60, 20, Loc.L("장전 탄", "Loaded"), bodyBold, Muted);
        Label(x + 76, y, StatusWidth - 92, 20, ammoName ?? Loc.L("없음", "None"), new GUIStyle(bodyBold) { fontSize = 14 }, Text);
        y += 24;
        var mid = StatusWidth - 16;
        Label(x + 16, y, 80, 20, "", tiny);
        Label(x + mid - 168, y, 52, 20, Loc.L("원래", "Base"), tinyRight);
        Label(x + mid - 110, y, 52, 20, level >= 0 ? Loc.F($"지금 {level}", $"Now {level}") : Loc.L("지금", "Now"), tinyRight, Myth);
        Label(x + mid - 52, y, 52, 20, level >= 0 && level < MythicRules.MaxLevel ? Loc.F($"다음 {level + 1}", $"Next {level + 1}") : "", tinyRight, Blue);
        y += 22;
        if (ammoRows.Count == 0)
        {
            Label(x + 16, y, StatusWidth - 32, AmmoRowHeight * 2, Loc.L("총에 장전된 탄이 없습니다.\n약실이나 탄창에 탄을 넣으면 비교해 보여 줍니다.",
                "No round is loaded.\nPut rounds in the chamber or magazine to compare."), new GUIStyle(wrap) { fontSize = 12 }, Muted);
            return;
        }
        var value = new GUIStyle(number) { fontSize = 14 };
        for (var index = 0; index < ammoRows.Count; index++, y += AmmoRowHeight)
        {
            var (drop, alpha) = UiMotion.Row(Motion, UiMotion.BaseRowDelay(index), Reduced);
            var old = GUI.color;
            GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
            var row = ammoRows[index];
            var top = y + drop;
            Fill(new Rect(x + 16, top + AmmoRowHeight - 1, StatusWidth - 32, 1), Hex(0x22282e));
            Label(x + 16, top + 4, 96, 20, row.Label, new GUIStyle(sub) { fontSize = 12 });
            Label(x + mid - 168, top + 2, 52, 22, row.Base, value, Text);
            Label(x + mid - 110, top + 2, 52, 22, row.Now, value, row.Changes ? Myth : Muted);
            Label(x + mid - 52, top + 2, 52, 22, row.Next, value, Blue);
            GUI.color = old;
        }
        y += 8;
        var note = level < 0 ? Loc.L("신화 무기만 탄이 바뀝니다(??? 성공 후).", "Only a mythic gun changes its rounds (after ???).")
            : Loc.L("이 총에 든 탄·이 총으로 쏘는 탄에만 적용 · 창고의 같은 탄은 그대로", "Only rounds in or fired from this gun · the same rounds elsewhere stay");
        Label(x + 16, y, StatusWidth - 32, 40, note, new GUIStyle(wrap) { fontSize = 11 }, Muted);
    }

    // Re-read at most twice a second, and only rebuild the rows when the round or the records change.
    private void ReadAmmo()
    {
        if (Clock - ammoReadAt < 0.5) return;
        ammoReadAt = Clock;
        object round = null;
        try { round = LoadedRound(selectedItem); }
        catch (Exception ex) { LogDetailed("장전 탄 읽기 실패: " + ex.GetBaseException().Message); }
        var level = MythicLevel;
        var key = (round == null ? "" : GameBridge.Get(round, "TemplateId")?.ToString()) + "|" + level + "|" + StatPatches.Registry.Version + "|" + Loc.Revision;
        if (key == ammoKey) return;
        ammoKey = key;
        ammoRows.Clear();
        ammoName = null;
        if (round == null) return;
        // The game's own numbers: the patched readers stand aside while Raw is set.
        StatPatches.Raw = true;
        try { BuildAmmoRows(round, level); }
        catch (Exception ex) { ammoRows.Clear(); LogDetailed("탄 비교 계산 실패: " + ex.GetBaseException().Message); }
        finally { StatPatches.Raw = false; }
    }

    private static object LoadedRound(object gun)
    {
        if (gun == null) return null;
        if (GameBridge.Get(gun, "Chambers") is Array chambers)
            foreach (var slot in chambers)
                if (GameBridge.Get(slot, "ContainedItem") is object chambered) return chambered;
        var magazine = AccessTools.Method(gun.GetType(), "GetCurrentMagazine")?.Invoke(gun, null);
        return GameBridge.Get(GameBridge.Get(magazine, "Cartridges"), "Last");
    }

    private void BuildAmmoRows(object round, int level)
    {
        ammoName = Localized(GameBridge.Get(round, "ShortName")?.ToString()) ?? "?";
        var tuning = MythicTuned;
        var next = level >= 0 && level < MythicRules.MaxLevel ? level + 1 : -1;
        // Damage and penetration: × (1 + 2 % a level).
        foreach (var (name, label) in new[] { ("Damage", Loc.L("탄 데미지", "Damage")), ("PenetrationPower", Loc.L("관통력", "Penetration")) })
        {
            var raw = Number(GameBridge.Get(round, name));
            string At(int at) => at < 0 ? "" : (raw * (1 + MythicEffects.WeaponPercent(at, tuning) / 100)).ToString("0.#");
            ammoRows.Add((label, raw.ToString("0.#"), level < 0 ? "—" : At(level), At(next), level > 0));
        }
        // Penalties, AmmoPatches kinds: 0 accuracy %, 1 recoil %, 2 burn ×, 3 heat ×, 4-6 chances (0..1).
        var penalties = new[]
        {
            ("ammoAccr", 0, Loc.L("명중률", "Accuracy")), ("ammoRec", 1, Loc.L("반동", "Recoil")),
            ("DurabilityBurnModificator", 2, Loc.L("내구도 소모", "Wear")), ("HeatFactor", 3, Loc.L("과열", "Heat")),
            ("MisfireChance", 4, Loc.L("불발 확률", "Misfire")), ("MalfMisfireChance", 5, Loc.L("고장(불발)", "Malf. misfire")),
            ("MalfFeedChance", 6, Loc.L("고장(급탄)", "Malf. feed"))
        };
        var any = false;
        foreach (var (name, kind, label) in penalties)
        {
            var raw = Number(GameBridge.Get(round, name));
            if (double.IsNaN(raw)) continue;
            // Rows with nothing to show (no bonus, no penalty) stay out.
            if (kind < 2 ? raw == 0 : kind < 4 ? Math.Abs(raw - 1) < 1e-4 : raw <= 0) continue;
            string Shown(double value) => kind < 2 ? Signed(value, "0") : kind < 4 ? Signed((value - 1) * 100, "0.#") : (value * 100).ToString("0.##") + "%";
            double At(int at) => at <= 0 ? raw : kind < 2
                ? AmmoPatches.NeutralInt((int)raw, kind, (float)(MythicEffects.AmmoPenaltyCut(at) / 100))
                : AmmoPatches.NeutralFloat((float)raw, kind, (float)(MythicEffects.AmmoPenaltyCut(at) / 100));
            var now = At(level);
            ammoRows.Add((label, Shown(raw), level < 0 ? "—" : Shown(now), next < 0 ? "" : Shown(At(next)), Math.Abs(now - raw) > 1e-6));
            any = true;
        }
        if (!any) ammoRows.Add((Loc.L("불이익", "Penalties"), Loc.L("없음", "none"), level < 0 ? "—" : Loc.L("없음", "none"), next < 0 ? "" : Loc.L("없음", "none"), false));
    }

    private static string Signed(double value, string format) => (value > 0 ? "+" : value < 0 ? "−" : "") + Math.Abs(value).ToString(format) + "%";
    private static double Number(object value) => value switch { int i => i, float f => f, double d => d, _ => double.NaN };

    private static string Localized(string key)
    {
        if (key == null) return null;
        var manager = GameBridge.RuntimeType("EFT.LocalizationManager");
        var localizer = manager?.GetMethod("LocalizedValue", new[] { typeof(string) });
        var instance = manager == null ? null : AccessTools.Property(manager, "Instance")?.GetValue(null);
        return instance != null && localizer != null ? localizer.Invoke(instance, new object[] { key })?.ToString() ?? key : key;
    }
}
