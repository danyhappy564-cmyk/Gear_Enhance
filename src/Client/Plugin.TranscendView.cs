using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

// 1.3.0 transcendence window (approved mockup, 760×910): 2-star / 3-star tabs, effects,
// materials, chance with pity, cost, downgrade protection and the bitcoin boost.
public sealed partial class Plugin
{
    private bool transcendMode, transcendAutomatic;
    private TranscendTrack transcendTrack = TranscendTrack.Second;
    private GUI.WindowFunction drawTranscendWindow;
    private GUIStyle transcendLarge, transcendValue, transcendButton, noteDanger, tabHeading, tabStars, smallRight, richBodyRight;
    private Texture2D transcendGradient, transcendCard;
    private static readonly Color Cyan = new Color(0.25f, 0.82f, 0.85f);
    private static readonly Color PaleCyan = new Color(0.55f, 0.90f, 0.92f);
    private void EnsureTranscendSkin()
    {
        drawTranscendWindow = DrawTranscendWindow;
        transcendLarge = TextStyle(39, Cyan, FontStyle.Bold);
        transcendValue = TextStyle(26, Cyan, FontStyle.Bold, TextAnchor.MiddleCenter);
        transcendButton = new GUIStyle(headerButton);
        transcendButton.normal.textColor = transcendButton.hover.textColor = transcendButton.active.textColor = PaleCyan;
        noteDanger = TextStyle(13, Loss); noteDanger.wordWrap = true;
        tabHeading = new GUIStyle(richHeading) { alignment = TextAnchor.MiddleLeft };
        tabStars = new GUIStyle(richHeading) { alignment = TextAnchor.MiddleRight };
        smallRight = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
        richBodyRight = new GUIStyle(richBody) { alignment = TextAnchor.MiddleRight };
        transcendGradient = Gradient(new Color(0.50f, 0.89f, 0.91f), new Color(0.20f, 0.66f, 0.69f));
        transcendCard = Gradient(new Color(0.06f, 0.165f, 0.176f), new Color(0.043f, 0.078f, 0.086f));
    }

    private bool CanTranscend => itemId != null && !uncertain && StatPatches.Registry.StarLevel(itemId) >= TranscendRules.StarsNeeded(TranscendTrack.Second);

    private void OpenTranscend()
    {
        if (busy || resultVisible || !CanTranscend) return;
        var stars = StatPatches.Registry.StarLevel(itemId);
        transcendTrack = stars >= TranscendRules.StarsNeeded(TranscendTrack.Third)
            && StatPatches.Registry.TranscendLevel(itemId, TranscendTrack.Second) >= TranscendRules.MaxLevel ? TranscendTrack.Third : TranscendTrack.Second;
        try { transcendAutomatic = weapon && TranscendPatches.Automatic(selectedItem); }
        catch (Exception ex) { transcendAutomatic = false; Logger.LogWarning("초월 총기 발사 방식 확인 실패(견적이 오면 맞춰짐): " + ex.Message); }
        transcendMode = true; evolutionMode = false; insuranceVisible = false;
        protectDestruction = protectDowngrade = chanceBoost = false;
        quote = preview = null; generation++; ResetWindow(); RefreshQuote();
    }
    private void ReturnFromTranscend()
    {
        if (busy || resultVisible) return;
        transcendMode = false; chanceBoost = protectDowngrade = false; quote = preview = null; generation++;
        ResetWindow(); RefreshQuote();
    }
    private void SelectTrack(TranscendTrack track)
    {
        if (busy || resultVisible || uncertain || !transcendMode || transcendTrack == track || itemId == null
            || StatPatches.Registry.StarLevel(itemId) < TranscendRules.StarsNeeded(track)) return;
        transcendTrack = track; protectDowngrade = chanceBoost = false;
        quote = preview = null; generation++; RefreshQuote();
    }

    private int TranscendLevel(TranscendTrack track) => StatPatches.Registry.TranscendLevel(itemId ?? "", track);
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
        TranscendKind.HorizontalRecoil => Loc.L("수평 반동 감소 (연사 가능 총)", "Horizontal recoil (auto guns)"),
        TranscendKind.MaxDurability => Loc.L("최대 내구도 증가", "Max durability"),
        TranscendKind.FireRate => Loc.L("발사속도 증가 (연사·점사 총)", "Fire rate (auto/burst guns)"),
        TranscendKind.Reliability => Loc.L("고장·과열 감소 (반자동·볼트)", "Malfunction · overheat (semi/bolt)"),
        TranscendKind.ArmorClass => Loc.L("방어등급 증가 · 둔탁상 감소", "Armor class · blunt reduction"),
        _ => ""
    };
    private string KindNote(TranscendKind kind) => kind switch
    {
        TranscendKind.HorizontalRecoil => Loc.L("뒤로 차는 힘과 좌우로 튀는 각도를 같은 비율로 줄입니다 · 반동 제어 강화와 합쳐 최대 80%",
            "Cuts the backward kick and the left/right kick angle alike · at most 80% with the recoil track"),
        TranscendKind.MaxDurability => Loc.L("원래 최대 내구도 기준 · 등록된 방탄판에도 적용 · 상인 판매가에는 반영되지 않음",
            "Based on the original max durability · registered plates too · not counted in sale prices"),
        TranscendKind.FireRate => Loc.L("원래 발사속도의 % 와 단계별 rpm 상한 중 작은 값을 더합니다",
            "Adds the smaller of the percent of the base fire rate and the rpm limit"),
        TranscendKind.Reliability => Loc.L("반자동·볼트액션 총 · 고장 확률과 한 발당 쌓이는 과열을 줄입니다",
            "Semi-auto and bolt-action guns · fewer malfunctions and less overheat per shot"),
        TranscendKind.ArmorClass => Loc.L($"3단계 +1클 · 5단계 +2클(최대 6클) · 이미 6클인 부품은 대신 둔탁상 감소 +{Tuned.BluntPerMissingClass:0.#}%/클",
            $"+1 class at 3, +2 at 5 (max 6) · parts already at 6 get +{Tuned.BluntPerMissingClass:0.#}% blunt reduction per class instead"),
        _ => ""
    };
    private static TranscendTuning Tuned => StatPatches.Registry.TranscendTuning;
    private static string Percent(double value) => $"{value:0.#}%";

    private void DrawTranscendWindow(int id)
    {
        Surface(new Rect(0, 0, WindowWidth, ActiveWindowHeight), panelGradient, Cyan);
        if (Event.current.type == EventType.Repaint)
            GUI.DrawTexture(new Rect(1, 1, WindowWidth - 2, 53), headerGradient);
        Fill(new Rect(1, 54, WindowWidth - 2, 1), Line);
        Label(23, 9, 130, 36, Loc.L("초월 강화", "Transcend"), heading);
        Label(150, 16, 430, 24, Loc.L("★★ 이상 장비 전용 · 2성·3성 따로 0~5단계", "★★ gear and up · 2-star and 3-star tracks, levels 0-5"), small);
        GUI.enabled = !busy && !resultVisible;
        if (UiButton(new Rect(594, 13, 106, 29), Loc.L("돌아가기", "Back"), secondary, UiInteraction.Close)) main.Enqueue(ReturnFromTranscend);
        if (UiButton(new Rect(712, 10, 32, 32), "×", secondary, UiInteraction.Close)) main.Enqueue(Close);
        GUI.enabled = true;
        DrawPreview();
        DrawTrackTab(new Rect(24, 244, 350, 60), TranscendTrack.Second);
        DrawTrackTab(new Rect(386, 244, 350, 60), TranscendTrack.Third);
        DrawTranscendEffects();
        DrawMaterials();
        DrawTranscendCosts();
        DrawTranscendOptions();
        DrawTranscendActions();
        if (!resultVisible) GUI.DragWindow(new Rect(0, 0, 580, 54));
        if (resultVisible) DrawResultPanel();
    }

    // Right side of the shared preview (after the item name) in this window.
    private void DrawTranscendPreview()
    {
        var level = TranscendLevel(transcendTrack);
        var stars = StatPatches.Registry.StarLevel(itemId ?? "");
        Label(518, 112, 216, 25, TrackName(transcendTrack) + " · " + KindName(KindOf(transcendTrack)), small);
        Label(513, 145, 225, 64, itemId == null ? Loc.L("없음", "Gone") : level >= TranscendRules.MaxLevel
            ? Loc.L($"초월 {level} 최대", $"Level {level} max") : Loc.L($"초월 {level} → {level + 1}", $"Level {level} → {level + 1}"), transcendLarge);
        Label(517, 207, 217, 22, Paint(StarEvolution.Stars(stars), StarTint(stars)) + $" +{StatPatches.Registry.TotalLevel(itemId ?? "")}"
            + Loc.L($" · 초월 합계 {StatPatches.Registry.TranscendTotal(itemId ?? "")}", $" · transcend total {StatPatches.Registry.TranscendTotal(itemId ?? "")}"), richSmall);
        Fill(new Rect(517, 230, 217, 2), Cyan);
        Fill(new Rect(24, 231, 712, 1), Line);
    }

    private void DrawTrackTab(Rect rect, TranscendTrack track)
    {
        var stars = StatPatches.Registry.StarLevel(itemId ?? "");
        var open = stars >= TranscendRules.StarsNeeded(track);
        var selected = open && track == transcendTrack;
        GUI.enabled = open && !busy && !resultVisible && !uncertain && itemId != null;
        var hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
        Surface(rect, selected ? transcendCard : cardGradient, selected ? Cyan : hovered ? Muted : Line);
        if (selected) Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, 1), PaleCyan);
        if (UiButton(rect, GUIContent.none, card)) main.Enqueue(() => SelectTrack(track));
        GUI.enabled = true;
        var starText = StarEvolution.Stars(TranscendRules.StarsNeeded(track));
        var starColor = open ? StarTint(TranscendRules.StarsNeeded(track)) : new Color(0.43f, 0.29f, 0.29f);
        if (!open)
        {
            Label(rect.x + 16, rect.y + 8, 260, 24, Paint(TrackName(track) + Loc.L(" · 잠김", " · locked"), new Color(0.44f, 0.46f, 0.48f)), tabHeading);
            Label(rect.x + 16, rect.y + 33, 280, 20, Loc.L("3차 스타(★★★) 진화 후 열림", "Opens after the 3rd star (★★★)"), small);
        }
        else
        {
            var level = TranscendLevel(track);
            var levelText = level >= TranscendRules.MaxLevel ? Loc.L($"+{level} 최대", $"+{level} max") : $"+{level}";
            Label(rect.x + 16, rect.y + 8, 260, 24, TrackName(track) + " " + Paint(levelText, Cyan), tabHeading);
            Label(rect.x + 16, rect.y + 33, 280, 20, KindHint(KindOf(track)), small);
        }
        Label(rect.xMax - 70, rect.y + 18, 56, 24, Paint(starText, starColor), tabStars);
    }

    private void DrawTranscendEffects()
    {
        Box(new Rect(24, 316, 712, 140), Panel, Line);
        Fill(new Rect(25, 317, 710, 32), new Color(0.12f, 0.135f, 0.15f, 0.65f));
        Fill(new Rect(25, 349, 710, 1), Line);
        var level = TranscendLevel(transcendTrack);
        var maxed = level >= TranscendRules.MaxLevel;
        var next = maxed ? level : level + 1;
        Label(40, 318, 300, 30, Loc.L("초월 효과", "Transcendence effect"), body);
        Label(379, 318, 122, 30, Loc.L($"현재 {level}단계", $"Now {level}"), centeredSmall);
        Label(594, 318, 126, 30, maxed ? Loc.L("최대 단계", "Maximum") : Loc.L($"성공 시 {next}단계", $"Next {next}"), centeredSmall);
        var kind = KindOf(transcendTrack);
        var tuning = Tuned;
        var now = TranscendEffects.Display(kind, level, tuning);
        var after = TranscendEffects.Display(kind, next, tuning);
        switch (kind)
        {
            case TranscendKind.ArmorClass:
                EffectRow(354, Loc.L("방어등급", "Armor class"), $"+{now.Class}", $"+{after.Class}", after.Class > now.Class);
                EffectRow(394, Loc.L("둔탁상 감소", "Blunt reduction"), Percent(now.Main), Percent(after.Main), after.Main > now.Main);
                break;
            case TranscendKind.Reliability:
                EffectRow(354, Loc.L("고장 확률 감소", "Malfunction reduction"), Percent(now.Main), Percent(after.Main), after.Main > now.Main);
                EffectRow(394, Loc.L("과열 증가 감소", "Overheat buildup reduction"), Percent(now.Secondary), Percent(after.Secondary), after.Secondary > now.Secondary);
                break;
            case TranscendKind.FireRate:
                EffectRow(354, Loc.L("발사속도 증가", "Fire rate bonus"), Percent(now.Main), Percent(after.Main), after.Main > now.Main);
                EffectRow(394, Loc.L("증가 상한", "Bonus limit"), $"+{now.Secondary:0} rpm", $"+{after.Secondary:0} rpm", after.Secondary > now.Secondary);
                break;
            default:
                EffectRow(354, KindName(kind), Percent(now.Main), Percent(after.Main), after.Main > now.Main);
                var cap = TranscendEffects.Display(kind, TranscendRules.MaxLevel, tuning).Main;
                Label(48, 398, 320, 26, Loc.L($"최대 5단계 {Percent(cap)}", $"Level 5 maximum {Percent(cap)}"), small);
                var bar = new Rect(380, 405, 339, 8);
                Bar(bar, after.Main, cap, Cyan);
                if (cap > 0) Fill(new Rect(bar.x, bar.y, bar.width * (float)Math.Max(0, Math.Min(1, now.Main / cap)), bar.height), new Color(0.43f, 0.46f, 0.48f));
                break;
        }
        Label(48, 428, 672, 24, KindNote(kind), small);
    }
    private void EffectRow(float y, string name, string now, string after, bool gains)
    {
        Label(48, y, 320, 36, name, body);
        Label(379, y - 2, 122, 38, now, value);
        Label(532, y - 2, 40, 38, "→", value);
        Label(594, y - 2, 126, 38, after, gains ? transcendValue : value);
    }

    private void DrawMaterials()
    {
        Surface(new Rect(24, 466, 712, 98), cardGradient, Line);
        Label(40, 470, 300, 28, Loc.L("재료", "Materials"), body);
        Label(380, 470, 340, 28, Loc.L("성공·실패와 관계없이 소모 · 잠기지 않은 창고 물자", "Used up either way · unlocked stash items"), smallRight);
        Fill(new Rect(25, 500, 710, 1), Line);
        var materials = preview?.Materials;
        if (preview == null) { Label(48, 518, 672, 28, TranscendLevel(transcendTrack) >= TranscendRules.MaxLevel ? Loc.L("최대 단계 · 더 필요 없음", "Maximum level · none needed") : "—", small); return; }
        if (materials == null || materials.Length == 0) { Label(48, 518, 672, 28, Loc.L("재료 없음 (F12에서 재료 요구 꺼짐)", "No materials (turned off in F12)"), small); return; }
        for (var index = 0; index < Math.Min(2, materials.Length); index++)
        {
            var material = materials[index];
            if (material == null) continue;
            var y = 504 + index * 28;
            Label(48, y, 420, 28, TranscendText.MaterialName(material.Template), body);
            Label(480, y, 240, 28, Paint(Loc.L($"보유 {material.Owned:N0} / 필요 {material.Count:N0}", $"Have {material.Owned:N0} / need {material.Count:N0}"),
                material.Owned >= material.Count ? Good : Loss), richBodyRight);
        }
    }

    private void DrawTranscendCosts()
    {
        Surface(new Rect(24, 576, 347, 112), cardGradient, Line);
        Surface(new Rect(385, 576, 351, 112), cardGradient, Line);
        Label(45, 582, 310, 26, Loc.L("성공률", "Success chance") + (preview?.ChanceBoost == true ? Paint($" (+{preview.BoostPoints:0.##}%p)", Good) : ""), richBody);
        Label(44, 606, 120, 46, preview == null ? "—" : $"{preview.SuccessPercent:0.##}%", chanceValue);
        if (preview != null)
        {
            var basic = Math.Max(0, preview.BaseSuccessPercent - preview.PityPercent);
            Label(160, 612, 200, 20, Loc.L($"기본 {basic:0.##}%", $"Base {basic:0.##}%") + Paint(Loc.L($" · 천장 +{preview.PityPercent:0.##}%p", $" · pity +{preview.PityPercent:0.##}%p"), Cyan)
                + (preview.ChanceBoost ? Paint($" · BTC +{preview.BoostPoints:0.##}%p", Good) : ""), richSmall);
            Label(160, 632, 200, 20, Loc.L($"천장: 실패마다 +{preview.PityStep:0.##}%p · 최대 {preview.PityCap:0.##}%p", $"Pity: +{preview.PityStep:0.##}%p per failure · max {preview.PityCap:0.##}%p"), small);
            // One bar in three colors: base, pity, bitcoin boost.
            Bar(new Rect(48, 663, 296, 9), 0, 100, Gold);
            var x = 48f;
            foreach (var (amount, color) in new[] { (basic, Gold), (preview.PityPercent, Cyan), (preview.BoostPoints, Good) })
            {
                var width = (float)(296 * Math.Max(0, Math.Min(100, amount)) / 100);
                width = Math.Min(width, 344 - x);
                if (width <= 0) continue;
                Fill(new Rect(x, 663, width, 9), color);
                x += width;
            }
        }
        Label(405, 582, 310, 26, Loc.L("초월 비용", "Transcendence cost"), body);
        Label(406, 610, 310, 40, preview == null ? "—" : $"{preview.Cost:N0} ₽", costValue);
        Label(407, 652, 309, 25, preview == null ? "" : Loc.L("보유 ", "Have ") + $"{preview.AvailableRoubles:N0} ₽ · BTC {preview.AvailableBitcoins:N0}", small);
        Label(24, 692, 712, 22, Loc.L("가격 기준: ", "Price: ") + (preview == null ? Loc.L("견적 확인 중", "checking quote") : Loc.PriceSource(preview.PriceSource))
            + (preview?.PartsPrice > 0 ? Loc.L(" · 본체+방탄판", " · body + plates") : Loc.L(" · 장비 본체", " · gear body only"))
            + Loc.L($" · 최소 비용 {MinimumTranscendCost:N0} ₽", $" · minimum {MinimumTranscendCost:N0} ₽")
            + (preview?.InsertSurcharge > 0 ? Paint(Loc.L($" · 방탄판 차액 +{preview.InsertSurcharge:N0} ₽", $" · plate fee +{preview.InsertSurcharge:N0} ₽"), Caution) : ""), richSmall);
    }
    private int MinimumTranscendCost => transcendTrack == TranscendTrack.Third ? Tuned.ThirdMinimumCost : Tuned.SecondMinimumCost;

    private void DrawTranscendOptions()
    {
        Surface(new Rect(24, 718, 712, 70), cardGradient, Line);
        var level = TranscendLevel(transcendTrack);
        GUI.enabled = !busy && !resultVisible && !uncertain && quote?.DowngradeProtectionAvailable == true;
        var text = preview?.DowngradeProtectionAvailable == true
            ? Loc.L($" 단계 하락 방지 (+{preview.DowngradeProtectionFee:N0} ₽) · 실패해도 {level}단계 유지", $" Prevent downgrade (+{preview.DowngradeProtectionFee:N0} ₽) · keep level {level} on failure")
            : Loc.L(" 단계 하락 방지 (4·5단계 시도에서만 · 이번 시도는 하락 없음)", " Prevent downgrade (level 4 and 5 tries only · this try cannot drop)");
        var downgrade = UiToggle(new Rect(38, 724, 684, 26), protectDowngrade, text);
        if (downgrade != protectDowngrade) main.Enqueue(() => SetProtection(false, downgrade));
        GUI.enabled = true;
        DrawChanceBoost(38, 754, 684);
    }

    private string TranscendFailureNote(EnhanceQuote view, out bool danger)
    {
        danger = false;
        if (view == null) return Loc.L("견적을 확인하는 중입니다.", "Checking the quote.");
        var level = view.CurrentLevel;
        var pity = Loc.L($"천장 +{view.PityStep:0.##}%p", $"pity +{view.PityStep:0.##}%p");
        if (view.FailureMode != FailureMode.Downgrade)
            return Loc.L($"실패 시: 비용·재료만 소모 · {level}단계 유지 · 다음 시도 성공률 +{view.PityStep:0.##}%p (천장)",
                $"On failure: fee and materials only · level {level} kept · next chance +{view.PityStep:0.##}%p (pity)");
        if (view.Protection.Downgrade)
            return Loc.L($"하락 방지 적용 · 실패 시 비용·재료·보호비만 소모 · {level}단계 유지 · {pity}",
                $"Downgrade protected · failure costs fee, materials and protection · level {level} kept · {pity}");
        danger = true;
        var lost = view.TranscendKind == TranscendKind.ArmorClass && TranscendEffects.ClassBonus(level - 1) < TranscendEffects.ClassBonus(level)
            ? Loc.L($"(방어등급 +{TranscendEffects.ClassBonus(level)} 사라짐)", $"(armor class +{TranscendEffects.ClassBonus(level)} lost)") : "";
        return Loc.L($"실패 시: {level}단계 → {level - 1}단계로 하락{lost} · 비용·재료 소모 · {pity} · 장비 파괴 없음",
            $"On failure: level {level} → {level - 1}{(lost.Length > 0 ? " " + lost : "")} · fee and materials used · {pity} · never destroyed");
    }

    private void DrawTranscendActions()
    {
        var note = TranscendFailureNote(preview, out var danger);
        if (TranscendLevel(transcendTrack) >= TranscendRules.MaxLevel) { note = Loc.L("이 초월은 최대 단계입니다.", "This track is at the maximum level."); danger = false; }
        Box(new Rect(24, 796, 712, 40), danger ? new Color(0.14f, 0.035f, 0.035f) : new Color(0.10f, 0.085f, 0.055f), danger ? Danger : new Color(0.61f, 0.46f, 0.22f));
        Label(38, 800, 24, 32, "!", danger ? noteDanger : selectedValue);
        Label(75, 798, 645, 36, note, danger ? noteDanger : noteStyle);
        var missing = TranscendText.Missing(quote);
        if (attemptTimeline.Active)
        {
            var progress = attemptTimeline.Fraction(AttemptClock);
            Label(28, 836, 240, 18, progress < 1 ? Loc.L("초월 진행 중… ", "Transcending… ") + $"{progress * 100:0}%" : Loc.L("초월 결과 판정 중…", "Rolling result…"), statusStyle);
            Bar(new Rect(260, 842, 472, 6), progress, 1, Cyan);
        }
        else if (missing.Length > 0 && string.IsNullOrEmpty(message))
            Label(28, 836, 704, 18, Paint(Loc.L("재료가 부족합니다: ", "Missing materials: ") + missing, Loss), richStatus);
        else
        {
            var left = quote == null ? 0 : Math.Max(0, quote.ExpiresUnixSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Label(28, 836, 704, 18, string.IsNullOrEmpty(message) ? quote == null ? "" : Loc.L($"견적 유효 시간: {left}초", $"Quote valid for {left}s") : message, statusStyle);
        }
        GUI.enabled = !busy && !resultVisible;
        var back = new Rect(24, 856, 233, 46);
        Surface(back, cardGradient, Line);
        if (UiButton(back, Loc.L("일반 강화로 돌아가기", "Back to enhancing"), secondary, UiInteraction.Close)) main.Enqueue(ReturnFromTranscend);
        var expired = quote != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= quote.ExpiresUnixSeconds;
        var maxed = TranscendLevel(transcendTrack) >= TranscendRules.MaxLevel;
        GUI.enabled = !busy && !resultVisible && !uncertain && itemId != null && !maxed && (quote == null || expired || quote.CanPay);
        var action = new Rect(273, 856, 463, 46);
        Surface(action, transcendGradient, new Color(0.71f, 0.95f, 0.95f));
        var caption = busy ? attemptTimeline.Active ? Loc.L("초월 진행 중…", "Transcending…") : Loc.L("처리 중…", "Working…")
            : maxed ? TrackName(transcendTrack) + Loc.L(" 최대 단계", " maxed")
            : quote == null || expired ? Loc.L("초월 견적 다시 불러오기", "Reload quote")
            : missing.Length > 0 ? Loc.L("재료 부족", "Not enough materials")
            : !quote.CanPay ? Loc.L("루블 또는 BTC 부족", "Not enough roubles or BTC")
            : TrackName(transcendTrack) + Loc.L(" 시도  ", "  ") + $"{quote.Cost:N0} ₽";
        if (UiButton(action, caption, primary))
        {
            if (quote == null || expired) main.Enqueue(RefreshQuote); else main.Enqueue(Execute);
        }
        GUI.enabled = true;
    }
}
