using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

// Body of the enhance, ★★, ★★★, transcendence and mythic tabs (design 1 items 3-8).
public sealed partial class Plugin
{
    private float midWidth = BodyWidth;

    private void DrawUpgradeTab()
    {
        var view = new Rect(BodyX, 180, BodyWidth, ButtonY - 10 - 180);
        var scrolls = midHeight > view.height + 1;
        midWidth = scrolls ? BodyWidth - 16 : BodyWidth;
        midScroll = GUI.BeginScrollView(view, midScroll, new Rect(0, 0, midWidth, Math.Max(midHeight, view.height)));
        var y = 0f;
        y = DrawCards(y);
        y = DrawEffects(y + 10);
        y = DrawChance(y + 10);
        y = DrawCost(y + 10);
        if (Evolving) y = DrawTickets(y + 10);
        y = DrawOptions(y + 10);
        y = DrawNote(y + 10);
        if (Event.current.type == EventType.Repaint) midHeight = y;
        GUI.EndScrollView();
        DrawUpgradeButtons();
    }

    // ---- cards -------------------------------------------------------------------------

    private float DrawCards(float y)
    {
        var id = itemId ?? "";
        var title = tab == UiTab.Star2 ? Loc.L("★★ 진화 조건", "★★ requirements") : tab == UiTab.Star3 ? Loc.L("★★★ 진화 조건", "★★★ requirements")
            : tab == UiTab.Transcend ? Loc.L("초월 트랙", "Transcendence tracks") : tab == UiTab.Mythic ? Loc.L("✦ 신화", "✦ Mythic") : Loc.L("강화 목록", "Upgrades");
        SectionTitle(y, title, tab == UiTab.Mythic ? Hex(0x5a2f52) : Hex(0x3a4148), tab == UiTab.Mythic ? Hex(0xff9be4) : Heading);
        y += 34;
        var cards = new List<(string Name, string Level, string Sub, bool On, bool Ok, Action Pick, Color Accent)>();
        if (tab == UiTab.Enhance)
            foreach (var path in weapon ? WeaponTracks : ArmorTracks)
            {
                var level = StatPatches.Registry.Find(id, path)?.Level ?? 0;
                var picked = path;
                // Mockup: the level alone, "· 최대" goes to the line under it.
                cards.Add((EnhanceEffects.Name(path), $"+{level}", TrackHint(path) + (level >= maxLevel.Value ? Loc.L(" · 최대", " · max") : ""),
                    path == selectedPath, false, () => SelectPath(picked), Gold));
            }
        else if (Evolving)
            foreach (var path in weapon ? WeaponTracks : ArmorTracks)
            {
                var level = StatPatches.Registry.Find(id, path)?.Level ?? 0;
                var met = level >= maxLevel.Value;
                cards.Add((EnhanceEffects.Name(path), $"+{level}", met ? Loc.L("조건 충족", "Met") : Loc.L($"+{maxLevel.Value} 필요", $"Needs +{maxLevel.Value}"),
                    false, met, null, Gold));
            }
        else if (tab == UiTab.Transcend)
            foreach (var track in new[] { TranscendTrack.Second, TranscendTrack.Third })
            {
                var open = StarCount >= TranscendRules.StarsNeeded(track);
                var level = StatPatches.Registry.TranscendLevel(id, track);
                var picked = track;
                cards.Add((TrackName(track), !open ? Loc.L("잠김", "Locked") : StageText.Of(level, TranscendRules.MaxLevel),
                    !open ? Loc.L("★★★ 진화 후 열림", "Opens after ★★★") : KindHint(KindOf(track)) + (level >= TranscendRules.MaxLevel ? Loc.L(" · 최대", " · max") : ""),
                    open && track == transcendTrack, false,
                    open ? () => SelectTrack(picked) : (Action)null, Cyan));
            }
        else
        {
            var level = Math.Max(0, MythicLevel);
            var tuning = MythicTuned;
            cards.Add((Loc.L("신화", "Mythic"), StageText.Of(level, MythicRules.MaxLevel), weapon
                ? Loc.L("탄 데미지 · 관통력", "Bullet damage · penetration") : Loc.L("피격 피해 감소", "Damage taken"), false, false, null, Myth));
            var zones = StatPatches.Registry.MythicZones(id);
            for (var slot = 0; slot < zones.Length && !weapon; slot++)
            {
                var picked = slot;
                cards.Add((Loc.L($"추가 부위 {slot + 1}", $"Extra zone {slot + 1}"), ZoneName(zones[slot]), Loc.L("눌러서 다시 뽑기", "Click to reroll"),
                    false, false, () => AskReroll(picked), Myth));
            }
        }
        var gap = 10f;
        var width = (midWidth - gap * (cards.Count - 1)) / Math.Max(1, cards.Count);
        for (var index = 0; index < cards.Count; index++)
        {
            var entry = cards[index];
            var rect = new Rect(index * (width + gap), y, width, 56);
            var mythCard = tab == UiTab.Mythic;
            GUI.enabled = entry.Pick != null && Steady && popup == PopupKind.None && !uncertain && itemId != null;
            var hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
            var border = entry.On ? entry.Accent : entry.Ok ? Hex(0x3d5a42) : mythCard ? (hovered ? Myth : Hex(0x8a3f79)) : hovered ? Hex(0x4a525b) : Line;
            Box(rect, entry.On ? Hex(0x211d14) : mythCard ? Hex(0x1a1019) : Section, border);
            if (UiButton(rect, GUIContent.none, card)) { var pick = entry.Pick; main.Enqueue(() => pick()); }
            GUI.enabled = true;
            Label(rect.x + 4, rect.y + 3, rect.width - 8, 18, entry.Name, cardName);
            Label(rect.x + 4, rect.y + 19, rect.width - 8, 22, entry.Level, cardLevel, entry.Ok ? Good : mythCard ? Myth : tab == UiTab.Transcend ? Cyan : Gold);
            Label(rect.x + 4, rect.y + 40, rect.width - 8, 14, entry.Sub, cardSub, mythCard && entry.Pick != null ? Hex(0xff9be4) : Muted);
        }
        return y + 56;
    }

    private static string TrackHint(EnhancePath path) => path == EnhancePath.Recoil ? Loc.L("수직·수평 반동", "Vertical & horizontal")
        : path == EnhancePath.Ergonomics ? Loc.L("인체공학", "Ergonomics") : path == EnhancePath.Precision ? Loc.L("탄 퍼짐", "Spread")
        : path == EnhancePath.ArmorDurability ? Loc.L("내구도 소모", "Durability loss") : Loc.L("장비 페널티", "Gear penalty");

    // ---- effects -----------------------------------------------------------------------

    private float DrawEffects(float y)
    {
        var rows = new List<(string Label, string Now, string Next, double NowBar, double NextBar, double Cap)>();
        var id = itemId ?? "";
        string chip;
        if (tab == UiTab.Enhance)
        {
            var current = preview?.CurrentEffect ?? StatPatches.Registry.Amount(id, selectedPath);
            var next = preview?.NextEffect ?? current;
            var cap = preview?.EffectCap ?? EnhanceEffects.Amount(selectedPath, maxLevel.Value, Tuning(), StarCount);
            var maxed = (StatPatches.Registry.Find(id, selectedPath)?.Level ?? 0) >= maxLevel.Value;
            rows.Add((EnhanceEffects.StatName(selectedPath), EnhanceEffects.Format(selectedPath, current),
                maxed ? Loc.L("최대", "Max") : EnhanceEffects.Format(selectedPath, next), current, maxed ? current : next, cap));
            // Mockup chip: the star multiplier the effects carry (×1, ×2, ×3).
            chip = $"×{StarMultiplier(StarCount):0.##}";
        }
        else if (Evolving)
        {
            var target = EvolutionStars;
            var from = Math.Min(StarCount, target - 1);
            foreach (var path in weapon ? WeaponTracks : ArmorTracks)
            {
                var level = StatPatches.Registry.Find(id, path)?.Level ?? 0;
                var now = EnhanceEffects.Amount(path, level, Tuning(), from);
                var next = EnhanceEffects.Amount(path, level, Tuning(), target);
                var cap = EnhanceEffects.Amount(path, maxLevel.Value, Tuning(), 3);
                rows.Add((EnhanceEffects.StatName(path), EnhanceEffects.Format(path, now), EnhanceEffects.Format(path, next), now, next, cap));
            }
            chip = $"×{StarMultiplier(target - 1):0.##} → ×{StarMultiplier(target):0.##}";
        }
        else if (tab == UiTab.Transcend)
        {
            var level = StatPatches.Registry.TranscendLevel(id, transcendTrack);
            var next = Math.Min(TranscendRules.MaxLevel, level + 1);
            var kind = KindOf(transcendTrack);
            var tuning = Tuned;
            var now = TranscendEffects.Display(kind, level, tuning);
            var after = TranscendEffects.Display(kind, next, tuning);
            var cap = TranscendEffects.Display(kind, TranscendRules.MaxLevel, tuning);
            switch (kind)
            {
                case TranscendKind.ArmorClass:
                    rows.Add((Loc.L("방어등급", "Armor class"), $"+{now.Class}", $"+{after.Class}", now.Class, after.Class, cap.Class));
                    rows.Add((Loc.L("둔탁상 감소", "Blunt reduction"), Percent(now.Main), Percent(after.Main), now.Main, after.Main, cap.Main));
                    break;
                case TranscendKind.Reliability:
                    rows.Add((Loc.L("고장 확률 감소", "Malfunction reduction"), Percent(now.Main), Percent(after.Main), now.Main, after.Main, cap.Main));
                    rows.Add((Loc.L("과열 증가 감소", "Overheat buildup reduction"), Percent(now.Secondary), Percent(after.Secondary), now.Secondary, after.Secondary, cap.Secondary));
                    break;
                case TranscendKind.FireRate:
                    rows.Add((Loc.L("발사속도 증가", "Fire rate bonus"), Percent(now.Main), Percent(after.Main), now.Main, after.Main, cap.Main));
                    rows.Add((Loc.L("증가 상한", "Bonus limit"), $"+{now.Secondary:0} rpm", $"+{after.Secondary:0} rpm", now.Secondary, after.Secondary, cap.Secondary));
                    break;
                default:
                    rows.Add((KindName(kind), Percent(now.Main), Percent(after.Main), now.Main, after.Main, cap.Main));
                    break;
            }
            chip = level >= TranscendRules.MaxLevel ? StageText.Of(level, TranscendRules.MaxLevel) : StageText.Of(level, TranscendRules.MaxLevel) + " → " + StageText.Of(next, TranscendRules.MaxLevel);
        }
        else
        {
            var level = Math.Max(0, MythicLevel);
            var next = Math.Min(MythicRules.MaxLevel, level + 1);
            var tuning = MythicTuned;
            // Mockup: damage and penetration on their own rows (both rise by the same percent).
            if (weapon)
                foreach (var name in new[] { Loc.L("탄 데미지", "Bullet damage"), Loc.L("관통력", "Penetration") })
                    rows.Add((name, $"+{MythicEffects.WeaponPercent(level, tuning):0.#}%",
                        level >= MythicRules.MaxLevel ? Loc.L("최대", "Max") : $"+{MythicEffects.WeaponPercent(next, tuning):0.#}%",
                        MythicEffects.WeaponPercent(level, tuning), MythicEffects.WeaponPercent(next, tuning), MythicEffects.WeaponPercent(MythicRules.MaxLevel, tuning)));
            // User 2026-10-26: what the mythic gun does to its rounds' bad values, level by level.
            if (weapon)
                rows.Add((Loc.L("탄 불이익 감소", "Ammo penalties cut"), $"{MythicEffects.AmmoPenaltyCut(level):0.#}%",
                    level >= MythicRules.MaxLevel ? Loc.L("최대", "Max") : $"{MythicEffects.AmmoPenaltyCut(next):0.#}%",
                    MythicEffects.AmmoPenaltyCut(level), MythicEffects.AmmoPenaltyCut(next), 100));
            else
                rows.Add((Loc.L("피격 피해 감소", "Damage taken reduction"), $"{MythicEffects.ArmorPercent(level, tuning):0.#}%",
                    level >= MythicRules.MaxLevel ? Loc.L("최대", "Max") : $"{MythicEffects.ArmorPercent(next, tuning):0.#}%", MythicEffects.ArmorPercent(level, tuning), MythicEffects.ArmorPercent(next, tuning),
                    MythicEffects.ArmorPercent(MythicRules.MaxLevel, tuning)));
            chip = level >= MythicRules.MaxLevel ? StageText.Of(level, MythicRules.MaxLevel) : StageText.Of(level, MythicRules.MaxLevel) + " → " + StageText.Of(next, MythicRules.MaxLevel);
        }
        var height = 34 + rows.Count * 36;
        var section = new Rect(0, y, midWidth, height);
        Box(section, Section, Line);
        Label(16, y + 8, 120, 18, Loc.L("강화 효과", "Effect"), bodyBold);
        var chipWidth = tiny.CalcSize(new GUIContent(chip)).x + 12;
        Outline(new Rect(90, y + 9, chipWidth, 16), Hex(0x3a4148));
        Label(90, y + 9, chipWidth, 16, chip, new GUIStyle(tiny) { alignment = TextAnchor.MiddleCenter });
        Label(midWidth - 216, y + 8, 200, 18, Loc.L("현재 → 성공 시", "Now → on success"), smallRight);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            // Mockup: the grey part from 0.18 s, the blue part from 0.56 s, 90 ms later per row.
            var fill = UiMotion.Fill(Motion, 0.18 + index * 0.09, Reduced);
            var fillNext = UiMotion.Fill(Motion, 0.56 + index * 0.09, Reduced);
            var top = y + 30 + index * 36;
            Label(16, top, 300, 22, row.Label, body);
            Label(midWidth - 230, top, 110, 22, row.Now, number);
            Label(midWidth - 116, top, 16, 22, "→", smallCenter);
            Label(midWidth - 100, top, 84, 22, row.Next, number, Blue);
            Bar(new Rect(16, top + 24, midWidth - 32, 8), row.NowBar, row.NextBar, row.Cap, BarNow, Blue, fill, "effect." + index, fillNext);
        }
        return y + height;
    }

    // ---- chance and materials ----------------------------------------------------------

    // Base, materials and pity as the window shows them: from the quote, or worked out here
    // while a changed material pick waits for its quote.
    private (double Base, double Pity, double Boost, double Total) ChanceParts()
    {
        if (preview == null) return (0, 0, 0, 0);
        var pity = preview.PityPercent;
        var basic = Math.Max(0, preview.BaseSuccessPercent - pity);
        double boost;
        if (BoostLocal && preview.BoostAvailable)
        {
            boost = 0;
            foreach (var kind in SuccessBoost.Kinds)
                if (boostOn[(int)kind]) boost += SuccessBoost.Points(kind, boostCount[(int)kind], BoostTuning());
        }
        else boost = preview.BoostPoints;
        var total = Math.Min(100, preview.BaseSuccessPercent + boost);
        return (basic, pity, total - preview.BaseSuccessPercent, total);
    }

    private float DrawChance(float y)
    {
        var height = 74 + (BoostRowsVisible ? 24 + 3 * 44 : 0);
        var section = new Rect(0, y, midWidth, height);
        Box(section, Section, Line);
        var parts = ChanceParts();
        // Mockup: "성공률" over a big Rajdhani number; "기본 55%  + 재료 7%" and the failure line beside it.
        var maxed = preview == null && TabMaxed;
        Label(14, y + 8, 120, 18, Loc.L("성공률", "Success chance"), bodyBold);
        Label(14, y + 26, 150, 40, preview != null ? $"{parts.Total:0.##}%" : maxed ? Loc.L("최대", "Max") : "—", numberBig);
        var x = 170f;
        var px = Pair(x, y + 28, Loc.L("기본 ", "Base "), preview == null ? "—" : $"{parts.Base:0.##}%", Text);
        if (parts.Pity > 0) px = Pair(px + 10, y + 28, Loc.L("+ 천장 ", "+ pity "), $"{parts.Pity:0.##}%", Cyan);
        Pair(px + 10, y + 28, Loc.L("+ 재료 ", "+ materials "), preview == null ? "—" : $"{parts.Boost:0.##}%", Blue);
        var fail = maxed ? Loc.L("최대 단계", "Max level") : FailureSummary(out _);
        var failColor = maxed ? Muted : FailureColor();
        Label(x, y + 28, midWidth - x - 14, 22, fail, smallRight, failColor);
        var bar = new Rect(x, y + 54, midWidth - x - 14, 8);
        Fill(bar, BarBack);
        var drawn = 0f;
        foreach (var (amount, color, delay, key) in new[] { (maxed ? 100 : parts.Base, BarBase, 0.24, "chance.base"), (parts.Pity, Cyan, 0.24, "chance.pity"), (parts.Boost, Blue, 0.62, "chance.boost") })
        {
            // Picks and quotes slide the bars to their new widths (mockup transition .35 s), never refill them.
            var width = bar.width * Tween(key, (float)Math.Max(0, Math.Min(1, amount / 100))) * UiMotion.Fill(Motion, delay, Reduced);
            width = Math.Min(width, bar.width - drawn);
            Fill(new Rect(bar.x + drawn, bar.y, width, bar.height), color);
            drawn += width;
        }
        if (!BoostRowsVisible) return y + height;
        var top = y + 74;
        var picked = 0;
        foreach (var on in boostOn) if (on) picked++;
        var tuning = BoostTuning();
        Label(14, top, 120, 20, Loc.L("성공률 올리기", "Boost chance"), bodyBold);
        Label(110, top, 200, 20, Loc.L($"재료 {tuning.MaxKinds}종까지", $"Up to {tuning.MaxKinds} kinds"), small);
        // Why the rows cannot be picked, in place of the count (a maxed track has no quote, 100% needs nothing).
        var why = preview == null ? maxed ? Loc.L("최대 단계라 재료가 필요 없습니다", "Maxed: no materials needed") : ""
            : preview.BoostAvailable ? "" : preview.BaseSuccessPercent >= 100 ? Loc.L("성공률 100%라 재료가 필요 없습니다", "Already 100%: no materials needed")
            : Loc.L("이 시도에는 재료를 쓸 수 없습니다", "No materials for this attempt");
        if (why.Length > 0) Label(midWidth - 314, top, 300, 20, why, smallRight, Muted);
        else Label(midWidth - 214, top, 200, 20, Loc.L("선택 ", "Picked ") + $"{picked} / {tuning.MaxKinds}", numberSmallRight, picked >= tuning.MaxKinds ? Blue : Muted);
        var usable = preview?.BoostAvailable == true && Steady && popup == PopupKind.None && !uncertain;
        for (var index = 0; index < SuccessBoost.Kinds.Length; index++)
            DrawMaterialRow(new Rect(12, top + 24 + index * 44, midWidth - 24, 38), SuccessBoost.Kinds[index], usable, picked >= tuning.MaxKinds, tuning);
        return y + height;
    }

    // ??? has its own tab; star evolution, enhancement, transcendence and mythic take materials.
    private bool BoostRowsVisible => tab != UiTab.Insurance && tab != UiTab.Help;

    private void DrawMaterialRow(Rect rect, BoostMaterial kind, bool usable, bool full, BoostTuning tuning)
    {
        var index = (int)kind;
        var on = boostOn[index];
        var locked = !on && full;
        var count = boostCount[index];
        var owned = preview?.BoostOwned != null && preview.BoostOwned.Length > index ? preview.BoostOwned[index] : 0;
        Box(rect, on ? Hex(0x121a24) : Inner, on ? Hex(0x3b5f8a) : Line);
        // Only a row that cannot be picked fades (mockup .mrow.locked); a quote reload never dims it.
        var faded = locked || preview?.BoostAvailable != true;
        var old = GUI.color;
        if (faded) GUI.color = new Color(old.r, old.g, old.b, old.a * 0.55f);
        var name = new Rect(rect.x + 12, rect.y, 196, rect.height);
        GUI.enabled = usable && !locked;
        var toggled = UiToggle(new Rect(name.x, rect.y + 10, 18, 18), on, "", name);
        if (toggled != on) main.Enqueue(() => ToggleBoost(kind));
        Label(name.x + 26, rect.y + 2, 170, 20, BoostName(kind), materialName, Text);
        Label(name.x + 26, rect.y + 21, 170, 15, Loc.L($"개당 +{SuccessBoost.PointsEach(kind, tuning):0.##}% · {SuccessBoost.RoublesEach(kind, tuning):N0} ₽",
            $"each +{SuccessBoost.PointsEach(kind, tuning):0.##}% · {SuccessBoost.RoublesEach(kind, tuning):N0} ₽"), tiny);
        // Stepper: one box with − count + (mockup .step).
        var step = new Rect(name.xMax + 10, rect.y + 2, 116, rect.height - 4);
        Box(step, Hex(0x0d1013), Line);
        var limit = BoostLimit(kind);
        GUI.enabled = usable && on && count > 1;
        StepButton(new Rect(step.x + 1, step.y + 1, 36, step.height - 2), "−", () => SetBoostCount(kind, count - 1));
        Label(step.x + 36, step.y, 44, step.height, on ? count.ToString() : "—", stepCount, on ? Text : Muted);
        GUI.enabled = usable && on && count < limit;
        StepButton(new Rect(step.xMax - 37, step.y + 1, 36, step.height - 2), "+", () => SetBoostCount(kind, count + 1));
        MiniButton(new Rect(step.xMax + 10, rect.y + 3, 52, rect.height - 6), Loc.L("최대", "Max"), () => SetBoostCount(kind, limit));
        GUI.enabled = true;
        var points = on ? SuccessBoost.Points(kind, count, tuning) : 0;
        var bonusX = step.xMax + 72;
        var bonusText = $"+{points:0.##}%";
        Label(bonusX, rect.y, 70, rect.height, bonusText, bonusNumber, on ? Blue : Hex(0x6f777f));
        var bonusWidth = bonusNumber.CalcSize(new GUIContent(bonusText)).x;
        Label(bonusX + bonusWidth + 4, rect.y + 6, 60, 13, Loc.L("최대", "max"), tiny);
        Label(bonusX + bonusWidth + 4, rect.y + 19, 60, 13, $"{SuccessBoost.Cap(kind, tuning):0.##}%", tiny);
        Label(rect.xMax - 12 - 64 - 10 - 104, rect.y, 104, rect.height, on ? $"{(long)count * SuccessBoost.RoublesEach(kind, tuning):N0} ₽" : "—", materialCost, on ? Text : Muted);
        Label(rect.xMax - 12 - 64, rect.y, 64, rect.height, Loc.L($"보유 {owned:N0}", $"own {owned:N0}"), tinyRight, on && owned < count ? Loss : Muted);
        GUI.color = old;
    }

    // − / + inside the stepper: no frame of their own, a hover tint only.
    private void StepButton(Rect rect, string text, Action action)
    {
        if (GUI.enabled && rect.Contains(Event.current.mousePosition)) Fill(rect, Hex(0x1f252b));
        if (UiButton(rect, "", card)) main.Enqueue(action);
        Label(rect.x, rect.y, rect.width, rect.height, text, stepSign, GUI.enabled ? Text : Hex(0x4b5259));
    }

    private static string BoostName(BoostMaterial kind) => kind == BoostMaterial.Dogtag ? Loc.L("독택", "Dogtag")
        : kind == BoostMaterial.GraphicsCard ? Loc.L("그래픽 카드", "Graphics card") : Loc.L("비트코인", "Bitcoin");

    private Color FailureColor() { FailureSummary(out var color); return color; }

    // The selected track (or stage) is at its maximum: there is nothing to quote.
    private bool TabMaxed
    {
        get
        {
            var id = itemId ?? "";
            return tab switch
            {
                UiTab.Enhance => (StatPatches.Registry.Find(id, selectedPath)?.Level ?? 0) >= maxLevel.Value,
                UiTab.Transcend => StatPatches.Registry.TranscendLevel(id, transcendTrack) >= TranscendRules.MaxLevel,
                UiTab.Mythic => MythicLevel >= MythicRules.MaxLevel,
                _ => false
            };
        }
    }

    // "label value" with the value in the number font (mockup: <span>기본 <span class=num>55%</span></span>).
    private float Pair(float x, float y, string label, string value, Color valueColor)
    {
        var labelWidth = sub.CalcSize(new GUIContent(label)).x;
        Label(x, y, labelWidth + 2, 22, label, sub);
        var valueWidth = numberInline.CalcSize(new GUIContent(value)).x;
        Label(x + labelWidth, y - 1, valueWidth + 2, 22, value, numberInline, valueColor);
        return x + labelWidth + valueWidth;
    }

    private string FailureSummary(out Color color)
    {
        color = Muted;
        var view = preview;
        if (view == null) return "";
        if (view.SuccessPercent >= 100) return Loc.L("항상 성공", "Always succeeds");
        if (view.IsEvolution)
        {
            if (view.UseTicket) { color = Good; return Loc.L("실패해도 방지권으로 장비 유지", "Fail: ticket keeps the gear"); }
            color = Loss; return Loc.L("실패 시 장비 파괴", "Fail: gear destroyed");
        }
        if (view.HighRisk)
        {
            var destroy = view.Protection.Destruction ? 0 : view.DestructionPercent;
            var drop = view.Protection.Downgrade ? 0 : 100 - view.DestructionPercent;
            if (destroy == 0 && drop == 0) { color = Good; return Loc.L("실패해도 보호", "Fail: guarded"); }
            color = destroy > 0 ? Loss : Caution;
            return Loc.L($"실패 시 파괴 {destroy:0.##}% · 하락 {drop:0.##}%", $"Fail: destroy {destroy:0.##}% · drop {drop:0.##}%");
        }
        if (view.FailureMode == FailureMode.Downgrade)
        {
            if (view.Protection.Downgrade) { color = Good; return Loc.L("실패해도 하락 방지", "Fail: drop guarded"); }
            color = Caution; return Loc.L("실패 시 1단계 하락", "Fail: drops one level");
        }
        if (view.PityStep > 0) return Loc.L($"실패 시 천장 +{view.PityStep:0.##}%p", $"Fail: pity +{view.PityStep:0.##}%p");
        return view.FailureMode == FailureMode.KeepItem ? Loc.L("실패 시 비용만 소모", "Fail: cost only") : FailureText(view.FailureMode);
    }

    // ---- cost --------------------------------------------------------------------------

    private float DrawCost(float y)
    {
        // Mockup cost section: "강화비 ₽ · 보호비 ₽ · 재료 루블 ₽ … 합계" over the required materials and the purse.
        var section = new Rect(0, y, midWidth, 66);
        Box(section, Section, Line);
        var view = preview;
        string Money(long value) => view == null ? "—" : $"{value:N0} ₽";
        var line = Loc.L("강화비 ", "Cost ") + Paint(Money(view?.BaseCost ?? 0), Text);
        var fee = view == null ? 0 : view.ProtectionCost + view.InsertSurcharge;
        if (fee > 0)
        {
            var label = view.InsertSurcharge > 0 && view.ProtectionCost > 0 ? Loc.L("보호비·방탄판 차액 ", "Guards & plate diff. ")
                : view.InsertSurcharge > 0 ? Loc.L("방탄판 차액 ", "Plate difference ") : Loc.L("보호비 ", "Guards ");
            line += "      " + Paint(label + Money(fee), view.InsertSurcharge > 0 ? Caution : Muted);
        }
        var boost = BoostRoubles(view);
        line += "      " + Loc.L("재료 루블 ", "Materials ") + Paint(Money(boost), Text);
        Label(16, y + 10, midWidth - 290, 26, line, costLine);
        var total = EstimatedCost(view);
        var totalText = view == null ? "—" : $"{total:N0} ₽";
        var totalWidth = costTotal.CalcSize(new GUIContent(totalText)).x;
        Label(midWidth - 16 - totalWidth - 64, y + 10, 56, 26, Loc.L("합계", "Total"), smallRight);
        Label(midWidth - 200, y + 4, 184, 34, totalText, costTotal);
        // Second line: the attempt's required materials (LEDX, parts…), then the purse.
        var items = new List<string>();
        foreach (var material in view?.Materials ?? Array.Empty<TranscendMaterial>())
            if (material != null)
                items.Add(Paint(TranscendText.MaterialName(material.Template) + $" {material.Owned:N0} / {material.Count}", material.Owned >= material.Count ? Good : Loss));
        Label(16, y + 38, midWidth - 260, 20, string.Join("      ", items), costLine);
        var short_ = view != null && view.AvailableRoubles < total;
        Label(midWidth - 260, y + 38, 244, 20, Loc.L("보유 ", "Owned ") + Paint(view == null ? "—" : $"{view.AvailableRoubles:N0} ₽", short_ ? Loss : Muted), costLineRight);
        return y + 66;
    }

    // Material roubles and the total: from the quote, or worked out here while a pick waits for its quote.
    private long BoostRoubles(EnhanceQuote view)
    {
        if (view == null) return 0;
        if (!BoostLocal) return view.BoostCost;
        long boost = 0;
        foreach (var kind in SuccessBoost.Kinds)
            if (boostOn[(int)kind]) boost += (long)boostCount[(int)kind] * SuccessBoost.RoublesEach(kind, BoostTuning());
        return boost;
    }
    private long EstimatedCost(EnhanceQuote view) => view == null ? 0 : BoostLocal ? view.Cost - view.BoostCost + BoostRoubles(view) : view.Cost;

    // ---- tickets, options and note ------------------------------------------------------

    private float DrawTickets(float y)
    {
        var view = preview;
        var target = TicketTarget;
        var mystery = target == MythicRules.MysteryTicket;
        var chip = new Rect(0, y, midWidth * 0.42f, 44);
        Box(chip, Inner, Hex(0x3d5a42));
        Label(chip.x + 10, chip.y + 4, chip.width - 20, 18, mystery ? Loc.L("??? 파괴 방지권 · 장비당 1장", "??? destruction ticket · one per gear")
            : Loc.L($"{StarEvolution.Stars(target)} 진화 파괴 방지권", $"{StarEvolution.Stars(target)} destruction ticket"), bodyBold, Text);
        var state = view == null || view.TicketStars != target ? "—"
            : Loc.L($"보유 {view.TicketsHeld}장 · 구매 {view.TicketsBought} / {view.TicketMax}", $"Held {view.TicketsHeld} · bought {view.TicketsBought} / {view.TicketMax}")
              + (view.TicketsBought >= view.TicketMax ? Loc.L(" (더 못 삼)", " (no more)") : "")
              + (target == 3 ? Loc.L(" · ★★용은 못 씀", " · ★★ tickets don't count") : "");
        Label(chip.x + 10, chip.y + 23, chip.width - 20, 16, state, tiny);
        var use = new Rect(chip.xMax + 8, y, midWidth * 0.30f, 44);
        var canUse = view != null && view.TicketStars == target && view.TicketsHeld > 0 && Steady && popup == PopupKind.None && !uncertain;
        Box(use, canUse ? Section : Inner, Line);
        GUI.enabled = canUse || useTicket;
        var toggled = UiToggle(new Rect(use.x + 10, use.y + 13, 18, 18), useTicket, "");
        if (toggled != useTicket) main.Enqueue(() => SetTicket(toggled));
        GUI.enabled = true;
        Label(use.x + 34, use.y, use.width - 40, 44, mystery ? Loc.L("방지권 사용\n(실패해도 ★★★ 유지)", "Use the ticket\n(★★★ kept on failure)")
            : Loc.L("방지권 사용\n(실패해도 장비 유지)", "Use a ticket\n(gear kept on failure)"), tiny, canUse ? Text : Muted);
        var buy = new Rect(use.xMax + 8, y, midWidth - use.xMax - 8, 44);
        GUI.enabled = view != null && view.TicketStars == target && view.TicketsBought < view.TicketMax && view.AvailableRoubles >= view.TicketPrice
            && Steady && popup == PopupKind.None && !uncertain;
        MiniButton(buy, view == null ? "—" : view.TicketsBought >= view.TicketMax ? Loc.L("더 살 수 없음", "Sold out")
            : Loc.L($"1장 구매 · {view.TicketPrice:N0} ₽", $"Buy one · {view.TicketPrice:N0} ₽"), BuyTicket);
        GUI.enabled = true;
        return y + 44;
    }

    private float DrawOptions(float y)
    {
        var view = preview;
        var usable = Steady && popup == PopupKind.None && !uncertain;
        if (tab == UiTab.Enhance)
        {
            var plates = !weapon;
            var width = plates ? (midWidth - 24) / 4 : (midWidth - 8) / 2;
            OptionToggle(new Rect(0, y, width, 44), protectDestruction, usable && view?.DestructionProtectionAvailable == true,
                Loc.L("파괴 방지", "Destroy guard"), view?.DestructionProtectionAvailable == true ? $"+{view.DestructionProtectionFee:N0}" : Loc.L("+6부터", "From +6"),
                on => SetProtection(true, on));
            OptionToggle(new Rect(width + 8, y, width, 44), protectDowngrade, usable && view?.DowngradeProtectionAvailable == true,
                Loc.L("하락 방지", "Drop guard"), view?.DowngradeProtectionAvailable == true ? $"+{view.DowngradeProtectionFee:N0}" : Loc.L("+6부터", "From +6"),
                on => SetProtection(false, on));
            if (plates) DrawPlates(new Rect(2 * (width + 8), y, midWidth - 2 * (width + 8), 44), view, usable);
            return y + 44;
        }
        if (tab == UiTab.Transcend || tab == UiTab.Mythic)
        {
            OptionToggle(new Rect(0, y, midWidth, 44), protectDowngrade, usable && view?.DowngradeProtectionAvailable == true,
                Loc.L("하락 방지", "Drop guard") + (view?.DowngradeProtectionAvailable == true ? "" : Loc.L(" · →4·→5 시도에서만", " · →4 and →5 only")),
                view?.DowngradeProtectionAvailable == true ? $"+{view.DowngradeProtectionFee:N0}" : "—", on => SetProtection(false, on));
            return y + 44;
        }
        return y - 10;
    }

    private void OptionToggle(Rect rect, bool on, bool enabled, string label, string price, Action<bool> change)
    {
        Box(rect, enabled ? Section : Inner, Line);
        GUI.enabled = enabled;
        var toggled = UiToggle(new Rect(rect.x + 12, rect.y + 13, 18, 18), on, "", rect);
        if (toggled != on) main.Enqueue(() => change(toggled));
        GUI.enabled = true;
        Label(rect.x + 36, rect.y, rect.width - 140, rect.height, label, optionLabel, enabled ? Text : Hex(0x6f777f));
        Label(rect.xMax - 120, rect.y, 108, rect.height, price, numberSmallRight, enabled ? Hex(0xc9b07a) : Hex(0x6f777f));
    }

    // Armor plates (1.3.0 slot rule) beside the guards: state and the paid registration.
    private void DrawPlates(Rect rect, EnhanceQuote view, bool usable)
    {
        var chip = new Rect(rect.x, rect.y, rect.width * 0.55f, rect.height);
        var owes = view != null && view.UnregisteredParts > 0;
        Box(chip, owes ? Hex(0x1f1a12) : Inner, owes ? Hex(0x8a6a3b) : Line);
        Label(chip.x + 10, chip.y + 4, chip.width - 16, 18, Loc.L($"방탄판 {view?.InsertedParts ?? 0}장", $"{view?.InsertedParts ?? 0} plates"), bodyBold, Text);
        Label(chip.x + 10, chip.y + 23, chip.width - 16, 16, view == null ? "—" : !owes ? Loc.L("모두 적용됨", "All applied")
            : view.InsertSurcharge > 0 ? Loc.L($"차액 {view.InsertSurcharge:N0} ₽", $"Owes {view.InsertSurcharge:N0} ₽") : Loc.L($"미적용 {view.UnregisteredParts}장", $"{view.UnregisteredParts} not applied"),
            tiny, owes ? Caution : Muted);
        GUI.enabled = usable && owes;
        MiniButton(new Rect(chip.xMax + 8, rect.y, rect.width - chip.width - 8, rect.height),
            owes && view.InsertSurcharge > 0 ? Loc.L("차액 내고 적용", "Pay and apply") : Loc.L("적용", "Apply"), () => RegisterPartsFrom(view));
        GUI.enabled = true;
    }

    private float DrawNote(float y)
    {
        var view = preview;
        var text = message;
        if (string.IsNullOrEmpty(text) && view != null)
        {
            var missing = TranscendText.Missing(view);
            text = missing.Length > 0 ? Paint(Loc.L("재료가 부족합니다: ", "Missing materials: ") + missing, Loss)
                : view.BindLegacy ? Loc.L("기존 단계 유지 · 첫 효과 선택은 무료 · 100% 적용", "Keeps old level · first effect choice is free · 100%")
                : "";
        }
        if (string.IsNullOrEmpty(text)) return y - 10;
        var height = Math.Max(20, wrap.CalcHeight(new GUIContent(text), midWidth));
        Label(2, y, midWidth - 4, height, text ?? "", new GUIStyle(wrap) { fontSize = 12 });
        return y + height;
    }

    // ---- bottom buttons ----------------------------------------------------------------

    private void DrawUpgradeButtons()
    {
        var view = quote;
        var expired = view != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= view.ExpiresUnixSeconds;
        var ready = view != null && !expired && boostDirtyAt < 0 && view.CanPay && !busy && popup == PopupKind.None && !uncertain && itemId != null;
        // A pick waiting for its quote keeps the buttons lit (text and the worked-out total) until the
        // quote is back; a click meanwhile does nothing.
        var pending = BoostLocal && preview != null && !attemptTimeline.Active && popup == PopupKind.None && !uncertain && itemId != null;
        var shown = pending ? preview : view;
        var instant = !Evolving;
        var close = new Rect(BodyX, ButtonY, instant ? 175 : 233, ButtonHeight);
        GUI.enabled = Steady && popup == PopupKind.None && !attemptTimeline.Active;
        if (BigButton(close, Loc.L("닫기", "Close"), ButtonKind.Dark, UiInteraction.Close)) main.Enqueue(Close);
        var actionX = close.xMax + 10;
        if (instant)
        {
            var fee = shown?.InstantAvailable == true ? settingsInstantFee : 0;
            var fast = new Rect(actionX, ButtonY, 175, ButtonHeight);
            GUI.enabled = (ready || pending) && shown.InstantAvailable && shown.AvailableRoubles >= (pending ? EstimatedCost(shown) : shown.Cost) + fee;
            if (BigButton(fast, Loc.L("즉시 강화", "Instant") + "\n" + Paint($"+{settingsInstantFee:N0} ₽", Hex(0x9fc4ee)), ButtonKind.Fast) && !pending)
                main.Enqueue(() => Execute(true));
            actionX = fast.xMax + 10;
        }
        var action = new Rect(actionX, ButtonY, BodyX + BodyWidth - actionX, ButtonHeight);
        var word = Evolving ? Loc.L("진화 시도", "Evolve") : tab == UiTab.Transcend ? Loc.L("초월 시도", "Transcend")
            : tab == UiTab.Mythic ? Loc.L("신화 강화", "Mythic attempt") : Loc.L("강화 시도", "Enhance");
        var reason = view == null ? NoQuoteReason() : null;
        var caption = pending ? $"{word} · {EstimatedCost(preview):N0} ₽"
            : busy ? attemptTimeline.Active ? Loc.L("진행 중…", "In progress…") : Loc.L("처리 중…", "Working…")
            : view == null ? !string.IsNullOrEmpty(reason) ? ShortReason() : Loc.L("견적 확인 중", "Checking quote")
            : expired ? Loc.L("견적 다시 불러오기", "Reload quote")
            : TranscendText.Missing(view).Length > 0 ? Loc.L("재료 부족", "Not enough materials")
            : !view.CanPay ? Loc.L("루블·재료 부족", "Not enough roubles or materials")
            : view.BindLegacy ? Loc.L("기존 단계에 효과 적용 · 무료", "Apply effect to old level · free")
            : $"{word} · {view.Cost:N0} ₽";
        GUI.enabled = pending ? preview.CanPay : itemId != null && !busy && popup == PopupKind.None && !uncertain && (ready || view != null && expired);
        if (BigButton(action, caption, ButtonKind.Gold) && !pending)
        {
            if (view == null || expired) main.Enqueue(RefreshQuote);
            else main.Enqueue(() => Execute(false));
        }
        GUI.enabled = true;
    }

    private int settingsInstantFee => instantFee?.Value ?? 50000;

    private string ShortReason() => tab == UiTab.Enhance ? Loc.L("이 항목은 최대 단계", "Track maxed")
        : Evolving ? StarCount >= EvolutionStars ? StarEvolution.Stars(EvolutionStars) + Loc.L(" 완료", " done") : Loc.L("조건 미달", "Requirements not met")
        : tab == UiTab.Transcend ? Loc.L("이 초월은 최대 단계", "Track maxed")
        : tab == UiTab.Mythic ? Loc.L("최대 신화 단계", "Mythic maxed") : "—";

    // ---- transcendence names (1.3.0) -----------------------------------------------------

    private TranscendKind KindOf(TranscendTrack track) => TranscendEffects.Kind(weapon, track, transcendAutomatic);
    private static string TrackName(TranscendTrack track) => track == TranscendTrack.Third ? Loc.L("3성 초월", "3-star transcendence") : Loc.L("2성 초월", "2-star transcendence");
    private static string KindName(TranscendKind kind) => kind switch
    {
        TranscendKind.HorizontalRecoil => Loc.L("수평 반동 감소", "Horizontal recoil reduction"),
        TranscendKind.MaxDurability => Loc.L("최대 내구도 증가", "Max durability bonus"),
        TranscendKind.FireRate => Loc.L("발사속도 증가", "Fire rate bonus"),
        TranscendKind.Reliability => Loc.L("고장 확률 감소", "Malfunction chance reduction"),
        TranscendKind.ArmorClass => Loc.L("방어등급 증가 · 둔탁상 감소", "Armor class · blunt reduction"),
        _ => "—"
    };
    private static string KindHint(TranscendKind kind) => kind switch
    {
        TranscendKind.HorizontalRecoil => Loc.L("수평 반동 감소", "Horizontal recoil"),
        TranscendKind.MaxDurability => Loc.L("최대 내구도 증가", "Max durability"),
        TranscendKind.FireRate => Loc.L("발사속도 증가", "Fire rate"),
        TranscendKind.Reliability => Loc.L("고장·과열 감소", "Malfunction · overheat"),
        TranscendKind.ArmorClass => Loc.L("방어등급 · 둔탁상", "Class · blunt"),
        _ => ""
    };
    private static TranscendTuning Tuned => StatPatches.Registry.TranscendTuning;
    private static MythicTuning MythicTuned => StatPatches.Registry.MythicTuning;
    private static string Percent(double value) => $"{value:0.#}%";

    internal static string ZoneName(string zone) => MythicText.ZoneName(zone);
}
