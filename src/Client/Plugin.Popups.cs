using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

internal enum PopupKind { None, Charging, Result, RiskConfirm, MysteryConfirm, RerollConfirm }

// Mockup flex row: natural text widths, 12 px gaps, 16 px side padding; wrap on overflow.
internal static class PopupEffectLayout
{
    internal static (float Label, float Now, float Next) Widths(float width, float label, float now, float next)
    {
        var available = Math.Max(3, width - 34 - 36 - 20);
        var total = Math.Max(1, label) + Math.Max(1, now) + Math.Max(1, next);
        var scale = Math.Min(1, available / total);
        return (Math.Max(1, label) * scale, Math.Max(1, now) * scale, Math.Max(1, next) * scale);
    }
}

// 1.4.0 popups (design 6 and 7.1): charging gauge, results, the destroy-risk / ??? / reroll
// questions, and the mythic effects. Effects only offset and tint what is drawn (P2).
public sealed partial class Plugin
{
    private PopupKind popup;
    private double popupStarted = -100;
    private EnhanceQuote popupQuote;
    private EnhanceReceipt resultReceipt;
    private FeedbackCue resultCue;
    private Sprite resultSprite;
    private bool popupSkip, confirmInstant;
    private Look look;
    private GUIStyle effectLabelStyle, effectValueStyle, effectStyleSource;
    private readonly GUIContent effectLabelContent = new GUIContent(), effectNowContent = new GUIContent(), effectNextContent = new GUIContent();

    private sealed class Look
    {
        internal string Kicker = "", Title = "", Line = "", EffectLabel, EffectNow, EffectNext, Cost, Note;
        // A second effect row: a mythic weapon raises bullet damage and penetration separately.
        internal string EffectLabel2, EffectNow2, EffectNext2;
        internal Color Tint = Gold, NoteColor = Muted, EffectColor = Blue;
        internal bool Shatter, Glow, LateGlow, Mythic, MythicLite;
    }

    // Controls under a popup must not take its clicks (IMGUI hands events to the first drawn).
    private bool drawingPopup;
    private bool InputLocked => popup != PopupKind.None && !drawingPopup;

    private void OpenCharging(EnhanceQuote attempt)
    {
        popupQuote = attempt; popup = PopupKind.Charging; popupStarted = Clock;
    }

    private void CloseCharging()
    {
        if (popup == PopupKind.Charging) popup = PopupKind.None;
    }

    private void OpenConfirm(PopupKind kind, bool instant)
    {
        popup = kind; popupStarted = Clock; popupSkip = false; confirmInstant = instant;
        if (kind != PopupKind.RerollConfirm) popupQuote = quote;
        // ??? has its own question sound; its 1.22 s thump matches the glow below (design 7.1).
        if (kind == PopupKind.MysteryConfirm) PlayFeedback(FeedbackCue.MysteryConfirm);
        else PlayInteraction(UiInteraction.Open);
    }

    private void CancelConfirm()
    {
        var kind = popup;
        popup = PopupKind.None; popupQuote = null;
        // The reroll question replaced the server's pending attempt quote.
        if (kind == PopupKind.RerollConfirm) RefreshQuote();
    }

    private void ProceedConfirm()
    {
        var kind = popup;
        var subject = popupQuote;
        popup = PopupKind.None;
        if (kind == PopupKind.RerollConfirm) { RunManaged(subject, Loc.L("부위 다시 뽑기 완료", "Zone rerolled"), true); return; }
        if (subject == null || !ReferenceEquals(subject, quote)) { RefreshQuote(); return; }
        destructionConfirmed = true;
        if (kind == PopupKind.RiskConfirm && popupSkip) skipRiskConfirm = true;
        Execute(confirmInstant);
    }

    private void ShowResult(EnhanceReceipt receipt, EnhanceQuote attempt)
    {
        resultReceipt = receipt;
        // The last ticket is gone (or vanished with the success): the next quote must not ask for one.
        if (receipt.TicketUsed && receipt.TicketsLeft <= 0 || receipt.TicketsVoided > 0) useTicket = false;
        // A cosmetic failure must never turn a confirmed payment into an unknown result.
        try
        {
            look = Describe(receipt, attempt);
            resultCue = receipt.Success ? FeedbackCue.Success : receipt.FailureMode == FailureMode.Destroy ? FeedbackCue.Destroy : FeedbackCue.Failure;
            resultSprite = GameBridge.Get(itemIcon, "Sprite") as Sprite;
        }
        catch (Exception ex) { Logger.LogWarning("강화 결과 연출 생략: " + ex.GetBaseException().Message); look ??= new Look { Title = ResultText.Title(receipt, false) }; }
        popup = PopupKind.Result; popupStarted = Clock;
        var grand = receipt.Success && (receipt.IsEvolution || receipt.Transcend != TranscendTrack.None
            || receipt.Action == EnhanceAction.Mystery || receipt.Action == EnhanceAction.Mythic);
        if (receipt.Action == EnhanceAction.BuyTicket) return;
        var cue = GameFeedback.Result(receipt, grand);
        // Design 7.1: the popup's effects start when its result sound does.
        main.Enqueue(() =>
        {
            if (popup == PopupKind.Result && ReferenceEquals(resultReceipt, receipt)) popupStarted = Clock;
            PlayFeedback(cue);
        });
    }

    private void DismissResult()
    {
        if (busy && popup != PopupKind.Result) return;
        popup = PopupKind.None; resultReceipt = null;
        if (itemId == null) Close();
    }

    private Look Describe(EnhanceReceipt receipt, EnhanceQuote attempt)
    {
        var result = new Look();
        var gear = itemName ?? Loc.L("장비", "Gear");
        var spent = new List<string> { $"{receipt.Paid:N0} ₽" };
        foreach (var material in attempt?.Materials ?? Array.Empty<TranscendMaterial>())
            if (material != null) spent.Add(TranscendText.MaterialName(material.Template) + $" {material.Count}");
        foreach (var line in receipt.Boost ?? Array.Empty<BoostLine>())
            if (line != null) spent.Add(BoostName(line.Kind) + $" {line.Count}");
        if (receipt.Instant) spent.Add(Loc.L("즉시 강화", "Instant"));
        result.Cost = string.Join(" · ", spent);
        var destroyedLine = Loc.L($"{gear}와 부착물·탄창이 사라졌습니다", $"{gear}, its attachments and magazines are gone");
        switch (receipt.Action)
        {
            case EnhanceAction.Mystery:
                result.Kicker = "???";
                if (receipt.Success)
                {
                    result.Title = Loc.L("신화 달성", "Mythic reached"); result.Tint = Myth; result.Mythic = true;
                    var zones = new List<string>();
                    foreach (var zone in receipt.Zones ?? Array.Empty<string>()) zones.Add(ZoneName(zone));
                    result.Line = zones.Count == 0 ? Loc.L("신화 단계가 열렸습니다", "The Mythic stage is open")
                        : Loc.L("추가 보호 부위: ", "Extra zones: ") + string.Join(" · ", zones);
                    if (weapon)
                    {
                        var perLevel = Loc.L($"단계당 +{MythicTuned.WeaponPercentPerLevel:0.#}%", $"+{MythicTuned.WeaponPercentPerLevel:0.#}% per level");
                        (result.EffectLabel, result.EffectNow, result.EffectNext) = (Loc.L("탄 데미지", "Bullet damage"), "???", perLevel);
                        (result.EffectLabel2, result.EffectNow2, result.EffectNext2) = (Loc.L("관통력", "Penetration"), "???", perLevel);
                        result.Note = Loc.L($"신화 1단계부터 이 총에 든 탄의 불이익이 단계당 {MythicEffects.AmmoPenaltyCut(1):0.#}%씩 줄어듭니다",
                            $"From Mythic 1 the rounds in this gun lose {MythicEffects.AmmoPenaltyCut(1):0.#}% of their penalties per level"); result.NoteColor = Soft;
                    }
                    else (result.EffectLabel, result.EffectNow, result.EffectNext) = (Loc.L("보호 부위", "Protected zones"),
                        Loc.L("기존", "Before"), Loc.L($"+{zones.Count}곳", $"+{zones.Count}"));
                }
                else if (receipt.TicketUsed)
                {
                    result.Title = Loc.L("??? 실패 · 파괴 방지권 사용", "??? failed · ticket used"); result.Tint = Good;
                    result.Line = Loc.L($"{gear} ★★★ 그대로 남았습니다", $"{gear} stays ★★★");
                    result.Note = Loc.L("??? 파괴 방지권 1장을 썼습니다 · 장비당 1장이라 더 살 수 없습니다", "The ??? ticket was used · one per gear, no more to buy");
                    result.NoteColor = Good;
                }
                else
                {
                    result.Title = Loc.L("??? 실패 · 장비 파괴", "??? failed · destroyed"); result.Tint = Danger; result.Shatter = true;
                    result.Line = destroyedLine; result.Note = Loc.L($"{100 - (attempt?.SuccessPercent ?? 50):0.##}% 판정에서 부서졌습니다", "It broke on the roll");
                }
                break;
            case EnhanceAction.Mythic:
            {
                result.Kicker = Loc.L("신화", "Mythic");
                var from = attempt?.CurrentLevel ?? 0;
                if (receipt.Success)
                {
                    result.Title = Loc.L("신화 강화 성공", "Mythic level up"); result.Tint = Myth; result.MythicLite = true;
                    result.Line = Loc.L("신화  ", "Mythic  ") + $"{from} → {receipt.Level}";
                    (result.EffectLabel, result.EffectNow, result.EffectNext) = MythicEffect(from, receipt.Level);
                    // Damage and penetration rise by the same percent; both get their own row.
                    if (weapon)
                    {
                        (result.EffectLabel2, result.EffectNow2, result.EffectNext2) = (Loc.L("관통력", "Penetration"), result.EffectNow, result.EffectNext);
                        result.Note = AmmoPenaltyNote(receipt.Level); result.NoteColor = Soft;
                    }
                }
                else Fallback(result, receipt, attempt, Loc.L("신화", "Mythic"), from);
                break;
            }
            case EnhanceAction.Reroll:
                result.Kicker = Loc.L("신화 · 추가 보호 부위", "Mythic · extra zones");
                result.Title = Loc.L("부위 다시 뽑기 완료", "Zone rerolled"); result.Tint = Myth; result.MythicLite = true;
                result.Line = ZoneName(receipt.OldZone) + "  →  " + ZoneName(receipt.NewZone);
                (result.EffectLabel, result.EffectNow, result.EffectNext) = (Loc.L("추가 보호 부위", "Extra zone"), ZoneName(receipt.OldZone), ZoneName(receipt.NewZone));
                break;
            case EnhanceAction.Transcend:
            {
                result.Kicker = Loc.L("초월", "Transcend");
                var from = attempt?.CurrentLevel ?? 0;
                if (receipt.Success)
                {
                    result.Title = Loc.L("초월 성공", "Transcended"); result.Tint = Cyan; result.Glow = true;
                    result.Line = ResultText.Track(receipt.Transcend) + $"  {from} → {receipt.Level}";
                    var kind = TranscendEffects.Kind(weapon, receipt.Transcend, transcendAutomatic);
                    var now = TranscendEffects.Display(kind, from, Tuned);
                    var after = TranscendEffects.Display(kind, receipt.Level, Tuned);
                    (result.EffectLabel, result.EffectNow, result.EffectNext) = kind == TranscendKind.ArmorClass
                        ? (Loc.L("둔탁상 감소", "Blunt reduction"), Percent(now.Main), Percent(after.Main)) : (KindName(kind), Percent(now.Main), Percent(after.Main));
                }
                else Fallback(result, receipt, attempt, ResultText.Track(receipt.Transcend), from);
                break;
            }
            case EnhanceAction.Evolve:
            case EnhanceAction.EvolveThird:
            {
                var target = receipt.Action == EnhanceAction.EvolveThird ? 3 : 2;
                result.Kicker = Loc.L($"{StarEvolution.Stars(target)} 진화", $"{StarEvolution.Stars(target)} evolution");
                if (receipt.Success)
                {
                    result.Title = Loc.L($"{StarEvolution.Stars(target)} 진화 성공", $"{StarEvolution.Stars(target)} evolved"); result.Tint = StarTint(target);
                    result.Glow = result.LateGlow = true;
                    result.Line = Loc.L($"모든 항목 효과 ×{StarMultiplier(target):0.##}", $"Every effect ×{StarMultiplier(target):0.##}");
                    var path = weapon ? EnhancePath.Recoil : EnhancePath.ArmorDurability;
                    var level = StatPatches.Registry.Find(itemId ?? "", path)?.Level ?? maxLevel.Value;
                    (result.EffectLabel, result.EffectNow, result.EffectNext) = (EnhanceEffects.StatName(path),
                        EnhanceEffects.Format(path, EnhanceEffects.Amount(path, level, Tuning(), target - 1)), EnhanceEffects.Format(path, EnhanceEffects.Amount(path, level, Tuning(), target)));
                    var notes = new List<string>();
                    if (receipt.TicketUsed) notes.Add(Loc.L($"파괴 방지권 1장 사용 · 남은 {receipt.TicketsLeft}장", $"Used one ticket · {receipt.TicketsLeft} left"));
                    if (receipt.TicketsVoided > 0) notes.Add(Loc.L($"남은 {StarEvolution.Stars(target)}용 방지권 {receipt.TicketsVoided}장은 사라졌습니다", $"{receipt.TicketsVoided} unused {StarEvolution.Stars(target)} ticket(s) are gone"));
                    if (notes.Count > 0) result.Note = string.Join(" · ", notes);
                }
                else if (receipt.TicketUsed)
                {
                    result.Title = Loc.L("진화 실패 · 파괴 방지권 사용", "Evolution failed · ticket used"); result.Tint = Good;
                    result.Line = Loc.L($"{gear} 유지 · 단계 그대로", $"{gear} kept · levels unchanged");
                    result.Note = Loc.L($"파괴 방지권 1장을 썼습니다 · 남은 {receipt.TicketsLeft}장", $"One ticket used · {receipt.TicketsLeft} left"); result.NoteColor = Good;
                }
                else
                {
                    result.Title = Loc.L("진화 실패 · 장비 파괴", "Evolution failed · destroyed"); result.Tint = Danger; result.Shatter = true;
                    result.Line = destroyedLine; result.Note = Loc.L("진화 실패는 파괴 방지권으로만 막을 수 있습니다", "Only a destruction ticket protects an evolution");
                }
                break;
            }
            default:
            {
                result.Kicker = Loc.L("일반 강화", "Enhance") + (attempt?.HighRisk == true ? Loc.L(" · 고위험", " · high risk") : "");
                var path = receipt.Path;
                var from = attempt?.CurrentLevel ?? 0;
                if (receipt.Success)
                {
                    result.Title = attempt?.BindLegacy == true ? Loc.L("기존 단계 복원", "Old level restored") : Loc.L("강화 성공", "Enhanced");
                    result.Tint = Gold; result.Glow = true;
                    result.Line = EnhanceEffects.Name(path) + $"  +{from} → +{receipt.Level}";
                    if (attempt != null) (result.EffectLabel, result.EffectNow, result.EffectNext) = (EnhanceEffects.StatName(path),
                        EnhanceEffects.Format(path, attempt.CurrentEffect), EnhanceEffects.Format(path, attempt.NextEffect));
                }
                else if (receipt.DestructionPrevented || receipt.DowngradePrevented)
                {
                    result.Title = Loc.L("강화 실패 · 보호 발동", "Failed · guard triggered"); result.Tint = Good;
                    result.Line = EnhanceEffects.Name(path) + Loc.L($"  +{from} 유지", $"  +{from} kept");
                    result.Note = receipt.DestructionPrevented ? Loc.L("파괴 방지가 장비를 지켰습니다", "The destroy guard saved the gear")
                        : Loc.L("하락 방지가 단계를 지켰습니다", "The drop guard kept the level"); result.NoteColor = Good;
                }
                else if (receipt.FailureMode == FailureMode.Downgrade)
                {
                    result.Title = Loc.L("단계 하락", "Level dropped"); result.Tint = Caution;
                    result.Line = EnhanceEffects.Name(path) + $"  +{from} → +{receipt.Level}";
                    result.EffectColor = Loss;
                    if (attempt != null) (result.EffectLabel, result.EffectNow, result.EffectNext) = (EnhanceEffects.StatName(path),
                        EnhanceEffects.Format(path, attempt.CurrentEffect), EnhanceEffects.Format(path, EnhanceEffects.Amount(path, receipt.Level, Tuning(), StarCount)));
                }
                else if (receipt.FailureMode == FailureMode.Destroy)
                {
                    result.Title = Loc.L("장비 파괴", "Gear destroyed"); result.Tint = Danger; result.Shatter = true;
                    result.Line = destroyedLine; result.Note = Loc.L("강화 기록과 강화 보험도 함께 사라집니다", "Its records and enhancement insurance are gone too");
                }
                else
                {
                    result.Title = Loc.L("강화 실패", "Enhancement failed"); result.Tint = Hex(0xe07a6e);
                    result.Line = EnhanceEffects.Name(path) + Loc.L($"  +{from} 유지", $"  +{from} kept");
                    result.Note = ResultText.Detail(receipt);
                }
                break;
            }
        }
        return result;
    }

    // Failed transcendence or mythic attempt: kept, dropped, or saved by the guard; pity grows.
    private void Fallback(Look result, EnhanceReceipt receipt, EnhanceQuote attempt, string track, int from)
    {
        var pity = Loc.L($"다음 성공률 +{attempt?.PityStep ?? 0:0.##}%p (누적 +{receipt.Pity:0.##}%p)", $"Next chance +{attempt?.PityStep ?? 0:0.##}%p (total +{receipt.Pity:0.##}%p)");
        if (receipt.DowngradePrevented)
        {
            result.Title = Loc.L("실패 · 하락 방지", "Failed · drop guarded"); result.Tint = Good;
            result.Line = track + Loc.L($"  {from} 유지", $"  {from} kept");
        }
        else if (receipt.FailureMode == FailureMode.Downgrade)
        {
            result.Title = Loc.L("단계 하락", "Level dropped"); result.Tint = Caution;
            result.Line = track + $"  {from} → {receipt.Level}";
        }
        else
        {
            result.Title = receipt.Action == EnhanceAction.Mythic ? Loc.L("신화 강화 실패", "Mythic attempt failed") : Loc.L("초월 실패", "Transcendence failed");
            result.Tint = Hex(0x7fb7b2);
            result.Line = track + Loc.L($"  {from} 유지", $"  {from} kept");
        }
        result.Note = pity;
    }

    // The rounds' bad values a mythic gun takes away, named (user 2026-10-26: say what changed).
    private static string AmmoPenaltyNote(int level) => Loc.L(
        $"이 총에 든 탄의 불이익 {MythicEffects.AmmoPenaltyCut(level):0.#}% 감소 · 명중률 −, 반동 +, 내구도 소모 +, 과열 +, 고장 확률",
        $"Rounds in this gun lose {MythicEffects.AmmoPenaltyCut(level):0.#}% of their penalties · accuracy −, recoil +, wear +, heat +, malfunction chance");

    private (string, string, string) MythicEffect(int from, int to) => weapon
        ? (Loc.L("탄 데미지", "Bullet damage"), $"+{MythicEffects.WeaponPercent(from, MythicTuned):0.#}%", $"+{MythicEffects.WeaponPercent(to, MythicTuned):0.#}%")
        : (Loc.L("받는 피해 감소", "Damage taken reduction"), $"{MythicEffects.ArmorPercent(from, MythicTuned):0.#}%", $"{MythicEffects.ArmorPercent(to, MythicTuned):0.#}%");

    // ---- drawing ------------------------------------------------------------------------

    private void DrawPopup()
    {
        drawingPopup = true;
        try
        {
            var overlay = new Rect(1, HeaderHeight, FrameWidth - 2, WindowHeight - HeaderHeight - 1);
            Fill(overlay, new Color(0, 0, 0, 0.65f));
            var elapsed = Clock - popupStarted;
            var current = popup == PopupKind.Result ? look : ConfirmLook();
            if (current == null) return;
            if (popup == PopupKind.Result && (current.Mythic || current.MythicLite) && !Reduced) DrawMythicEffect(overlay, elapsed, current.MythicLite);
            var height = Measure(current);
            var x = (FrameWidth - 620) / 2 + (current.Shatter ? UiMotion.Shake(elapsed, Reduced) : 0);
            var y = overlay.center.y - height / 2;
            var cardRect = new Rect(x, y, 620, height);
            // Mockup .pop: popIn (scale .92 → 1). Mythic: mythIn (scale .45 / -5° → 1.07 → 1). The destroy
            // shake and the ??? hit replace popIn there (same transform, fill-mode both). Never transparent.
            float scale = 1, angle = 0;
            if (current.Mythic) (scale, angle) = UiMotion.MythIn(elapsed, Reduced);
            else if (popup == PopupKind.MysteryConfirm) scale = UiMotion.ConfirmHit(elapsed, Reduced);
            else if (!current.Shatter) scale = UiMotion.PopScale(elapsed, Reduced);
            var restore = Transform(cardRect.center, scale, angle);
            try { DrawPopupCard(current, cardRect, elapsed); }
            finally { GUI.matrix = restore; }
        }
        finally { drawingPopup = false; }
    }

    private void DrawPopupCard(Look current, Rect cardRect, double elapsed)
    {
        var x = cardRect.x; var y = cardRect.y; var height = cardRect.height;
        var pulse = current.Mythic || current.MythicLite ? UiMotion.MythPulse(elapsed, current.MythicLite, Reduced) : -1;
        if (pulse >= 0) MythicCardGlow(cardRect, pulse);
        Box(cardRect, HeaderFill, Edge);
        if (pulse < 0) Outline(new Rect(x - 1, y - 1, 622, height + 2), new Color(current.Tint.r, current.Tint.g, current.Tint.b, 0.35f));
        Fill(new Rect(x, y, 620, 5), current.Tint);
        var top = y + 27;
        Label(x, top, 620, 16, current.Kicker, popKicker); top += 28;
        if (current.Mythic || current.MythicLite)
        {
            var size = current.Mythic ? 46 : 30;
            DrawStar(new Rect(x, top, 620, size + 4), PopStyle(size), elapsed);
            top += size + 16;
        }
        var titleSize = current.Mythic ? 44 : current.MythicLite ? 36 : 34;
        var title = new Rect(x + 20, top, 580, titleSize + 8);
        if ((current.Mythic || current.MythicLite) && !Reduced) GradientLabel(title, current.Title, PopStyle(titleSize), elapsed);
        else
        {
            var glow = TitleGlow(current, elapsed);
            // titleGlow: 22 px of its own colour; the ??? hit: 28 px pink.
            if (glow > 0) GlowText(title, current.Title, PopStyle(titleSize), popup == PopupKind.MysteryConfirm ? Myth : current.Tint,
                (popup == PopupKind.MysteryConfirm ? 28 : 22) / 4f * glow);
            Label(title.x, title.y, title.width, title.height, current.Title, PopStyle(titleSize), current.Tint);
        }
        top += titleSize + 20;
        Label(x + 32, top, 556, 24, current.Line, popLine); top += 36;
        if (current.Shatter) { DrawShatter(new Rect(x + 175, top, 270, 110), elapsed); top += 122; }
        if (current.EffectLabel != null)
        {
            var rowHeight = EffectRowHeight(current.EffectLabel, current.EffectNow, current.EffectNext);
            DrawEffectRow(new Rect(x + 32, top, 556, rowHeight), current.EffectLabel, current.EffectNow, current.EffectNext, current.EffectColor, elapsed);
            top += rowHeight + 12;
        }
        if (current.EffectLabel2 != null)
        {
            var rowHeight = EffectRowHeight(current.EffectLabel2, current.EffectNow2, current.EffectNext2);
            DrawEffectRow(new Rect(x + 32, top - 4, 556, rowHeight), current.EffectLabel2, current.EffectNow2, current.EffectNext2, current.EffectColor, elapsed);
            top += rowHeight + 8;
        }
        if (popup == PopupKind.Charging)
        {
            var fraction = (float)attemptTimeline.Fraction(AttemptClock);
            Fill(new Rect(x + 32, top + 8, 556, 12), BarBack);
            Fill(new Rect(x + 32, top + 8, 556 * fraction, 12), current.Tint);
            top += 32;
        }
        if (current.Cost != null || current.Note != null)
        {
            Fill(new Rect(x + 32, top, 556, 1), Line); top += 12;
            if (current.Cost != null) { Label(x + 32, top, 556, 20, Loc.L("소모 ", "Spent ") + Paint(current.Cost, Text), richCenter); top += 24; }
            if (current.Note != null) { Label(x + 32, top, 556, 20, current.Note, smallCenter, current.NoteColor); top += 24; }
        }
        DrawPopupButtons(x, top + 4);
    }

    private readonly System.Collections.Generic.Dictionary<int, GUIStyle> popStyles = new System.Collections.Generic.Dictionary<int, GUIStyle>();
    private GUIStyle popStyleSource;
    // popTitle at another size, made once per size (the skin is rebuilt with new styles).
    private GUIStyle PopStyle(int size)
    {
        if (!ReferenceEquals(popStyleSource, popTitle)) { popStyles.Clear(); popStyleSource = popTitle; }
        if (!popStyles.TryGetValue(size, out var style)) popStyles[size] = style = new GUIStyle(popTitle) { fontSize = size };
        return style;
    }

    // Success titles: titleGlow (1.2 s from 0.05 s, or 1.45 s for star evolutions). ???: qHit (0.7 s from 1.22 s).
    private float TitleGlow(Look current, double elapsed) => Reduced ? 0
        : popup == PopupKind.MysteryConfirm ? UiMotion.Glow(elapsed, 1.22, 0.7, 0.25, false)
        : current.Glow ? UiMotion.Glow(elapsed, current.LateGlow ? 1.45 : 0.05, 1.2, 0.12, false) : 0;

    private void DrawEffectRow(Rect box, string label, string now, string next, Color tint, double elapsed)
    {
        var widths = EffectWidths(label, now, next);
        Box(box, Deep, Line);
        var valueX = box.xMax - 17 - widths.Next - 12 - 20 - 12 - widths.Now;
        Label(box.x + 17, box.y + 13, widths.Label, box.height - 26, label, effectLabelStyle, Soft);
        Label(valueX, box.y + 13, widths.Now, box.height - 26, now, effectValueStyle);
        Label(valueX + widths.Now + 12, box.y + 13, 20, box.height - 26, "→", smallCenter);
        var drop = UiMotion.EffectDrop(elapsed, Reduced);
        Label(box.xMax - 17 - widths.Next, box.y + 13 - drop, widths.Next, box.height - 26, next, effectValueStyle, tint);
    }

    private (float Label, float Now, float Next) EffectWidths(string label, string now, string next)
    {
        if (!ReferenceEquals(effectStyleSource, body))
        {
            effectStyleSource = body;
            effectLabelStyle = new GUIStyle(body) { fontSize = 14, wordWrap = true };
            effectValueStyle = new GUIStyle(number) { fontSize = 22, wordWrap = true };
        }
        effectLabelContent.text = label;
        effectNowContent.text = now;
        effectNextContent.text = next;
        return PopupEffectLayout.Widths(556, effectLabelStyle.CalcSize(effectLabelContent).x,
            effectValueStyle.CalcSize(effectNowContent).x, effectValueStyle.CalcSize(effectNextContent).x);
    }

    private float EffectRowHeight(string label, string now, string next)
    {
        var widths = EffectWidths(label, now, next);
        return Math.Max(48, 26 + Math.Max(effectLabelStyle.CalcHeight(effectLabelContent, widths.Label),
            Math.Max(effectValueStyle.CalcHeight(effectNowContent, widths.Now), effectValueStyle.CalcHeight(effectNextContent, widths.Next))));
    }

    private Look ConfirmLook()
    {
        var subject = popupQuote;
        switch (popup)
        {
            case PopupKind.Charging:
            {
                var view = subject;
                if (view == null) return null;
                var fraction = attemptTimeline.Fraction(AttemptClock);
                var what = view.Action == EnhanceAction.Transcend ? ResultText.Track(view.Transcend) + $" {view.CurrentLevel} → {view.NextLevel}"
                    : view.Action == EnhanceAction.Mythic ? Loc.L("신화 ", "Mythic ") + $"{view.CurrentLevel} → {view.NextLevel}"
                    : view.Action == EnhanceAction.Mystery ? "???"
                    : view.IsEvolution ? $"{StarEvolution.Stars(view.CurrentStars)} → {StarEvolution.Stars(view.NextStars)}"
                    : EnhanceEffects.Name(view.Path) + $" +{view.CurrentLevel} → +{view.NextLevel}";
                return new Look
                {
                    Kicker = ActionName(view), Title = fraction < 1 ? Loc.L("진행 중…", "In progress…") : Loc.L("판정 중…", "Rolling…"),
                    Line = (itemName ?? "") + " · " + what + $" · {fraction * 100:0}%", Tint = ActionTint(view)
                };
            }
            case PopupKind.RiskConfirm:
            {
                if (subject == null) return null;
                var destroy = subject.IsEvolution ? 100 - subject.SuccessPercent : (100 - subject.SuccessPercent) * subject.DestructionPercent / 100;
                return new Look
                {
                    Kicker = ActionName(subject), Title = Loc.L("장비가 파괴될 수 있습니다", "The gear can be destroyed"), Tint = Danger,
                    Line = Loc.L($"이번 시도 파괴 확률 {destroy:0.##}% · 본체와 부착물 {subject.AttachedItems}개(탄약 포함)", $"Destruction chance this try {destroy:0.##}% · gear and {subject.AttachedItems} attachment(s) incl. ammo"),
                    Cost = $"{subject.Cost:N0} ₽" + (confirmInstant ? Loc.L(" · 즉시 강화", " · instant") : "")
                };
            }
            case PopupKind.MysteryConfirm:
                if (subject == null) return null;
                return new Look
                {
                    Kicker = "???", Title = "???", Tint = Myth, Glow = true,
                    Line = subject.UseTicket
                        ? Loc.L($"{subject.SuccessPercent:0.##}% 확률로 성공 · 실패해도 파괴 방지권으로 ★★★ 장비가 남습니다.", $"{subject.SuccessPercent:0.##}% to succeed · on a failure the ticket keeps the ★★★ gear.")
                        : weapon ? Loc.L($"{subject.SuccessPercent:0.##}/{100 - subject.SuccessPercent:0.##} 확률로 무기가 성공하거나 부서집니다.", $"At {subject.SuccessPercent:0.##}/{100 - subject.SuccessPercent:0.##} the weapon succeeds or breaks.")
                        : Loc.L($"{subject.SuccessPercent:0.##}/{100 - subject.SuccessPercent:0.##} 확률로 방어구가 성공하거나 부서집니다.", $"At {subject.SuccessPercent:0.##}/{100 - subject.SuccessPercent:0.##} the armor succeeds or breaks."),
                    Cost = $"{subject.Cost:N0} ₽" + (subject.Materials?.Length > 0 ? $" · LEDX {subject.Materials[0].Count}" : "")
                        + (subject.UseTicket ? Loc.L(" · ??? 방지권 1장", " · 1 ??? ticket") : ""),
                    Note = Loc.L("진행하시겠습니까?", "Proceed?"), NoteColor = Text
                };
            case PopupKind.RerollConfirm:
                if (subject == null) return null;
                var zones = StatPatches.Registry.MythicZones(itemId ?? "");
                return new Look
                {
                    Kicker = Loc.L("신화 · 추가 보호 부위", "Mythic · extra zones"), Title = Loc.L("부위 다시 뽑기", "Reroll a zone"), Tint = Myth,
                    Line = (subject.Slot < zones.Length ? ZoneName(zones[subject.Slot]) : "?") + Loc.L($" → 남은 {subject.ZoneOptions.Length}곳 중 무작위", $" → one of {subject.ZoneOptions.Length} at random"),
                    Cost = $"{subject.Cost:N0} ₽" + (subject.Materials?.Length > 0 ? " · " + TranscendText.MaterialName(subject.Materials[0].Template) + $" {subject.Materials[0].Count}" : ""),
                    Note = Loc.L("되돌릴 수 없습니다. 다음 다시 뽑기는 ×1.5", "Cannot be undone. The next reroll costs ×1.5"), NoteColor = Caution
                };
        }
        return null;
    }

    private float Measure(Look current)
    {
        var height = 27f + 28;
        if (current.Mythic || current.MythicLite) height += (current.Mythic ? 46 : 30) + 16;
        height += (current.Mythic ? 44 : current.MythicLite ? 36 : 34) + 20 + 36;
        if (current.Shatter) height += 122;
        if (current.EffectLabel != null) height += EffectRowHeight(current.EffectLabel, current.EffectNow, current.EffectNext) + 12;
        if (current.EffectLabel2 != null) height += EffectRowHeight(current.EffectLabel2, current.EffectNow2, current.EffectNext2) + 8;
        if (popup == PopupKind.Charging) height += 32;
        if (current.Cost != null || current.Note != null) height += 12 + (current.Cost != null ? 24 : 0) + (current.Note != null ? 24 : 0);
        height += popup == PopupKind.Charging ? 10 : popup == PopupKind.RiskConfirm ? 4 + 46 + 10 + 40 + 26 : 4 + 46 + 26;
        return height;
    }

    private void DrawPopupButtons(float x, float top)
    {
        switch (popup)
        {
            case PopupKind.Result:
                GUI.enabled = true;
                if (BigButton(new Rect(x + 145, top, 330, 46), Loc.L("확인", "OK"), ButtonKind.Gold) || Pressed(KeyCode.Return)) main.Enqueue(DismissResult);
                break;
            case PopupKind.RiskConfirm:
            case PopupKind.MysteryConfirm:
            case PopupKind.RerollConfirm:
                if (popup == PopupKind.RiskConfirm)
                {
                    var skip = new Rect(x + 32, top, 556, 40);
                    Box(skip, Section, Line);
                    popupSkip = UiToggle(new Rect(skip.x + 12, skip.y + 11, 18, 18), popupSkip, "", skip);
                    Label(skip.x + 38, skip.y, skip.width - 44, skip.height, Loc.L("이 창을 닫을 때까지 다시 묻지 않기", "Don't ask again until this window closes"), body);
                    top += 50;
                }
                if (BigButton(new Rect(x + 32, top, 273, 46), Loc.L("취소", "Cancel"), ButtonKind.Dark, UiInteraction.Close) || Pressed(KeyCode.Escape)) main.Enqueue(CancelConfirm);
                if (BigButton(new Rect(x + 315, top, 273, 46), Loc.L("진행", "Proceed"), ButtonKind.Danger)) main.Enqueue(ProceedConfirm);
                break;
        }
    }

    private static bool Pressed(KeyCode key) => Event.current.type == EventType.KeyDown && Event.current.keyCode == key;

    private static readonly Vector2[] PieceMoves =
    {
        new Vector2(-70, -50), new Vector2(0, -70), new Vector2(75, -48), new Vector2(-95, 6), new Vector2(6, 26),
        new Vector2(98, -4), new Vector2(-72, 62), new Vector2(-4, 78), new Vector2(80, 58)
    };
    private static readonly float[] PieceTurns = { -25, 12, 28, -18, 8, 20, 30, -14, -26 };

    // Nine pieces of the gear picture fly apart and turn from 0.06 s (in time with the destroy sound).
    private void DrawShatter(Rect area, double elapsed)
    {
        if (resultSprite == null || resultSprite.texture == null || Event.current.type != EventType.Repaint) return;
        // 0.95 s from 0.06 s (mockup s0-s8); reduced motion shows the pieces where they land.
        var progress = UiMotion.Shatter(elapsed, Reduced);
        var source = resultSprite.rect;
        var fit = Math.Min(area.width / source.width, area.height / source.height);
        var w = source.width * fit / 3;
        var h = source.height * fit / 3;
        var previous = GUI.color;
        var matrix = GUI.matrix;
        try
        {
            GUI.color = new Color(1, 0.62f, 0.52f, 1 - 0.65f * progress);
            for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
            {
                var move = PieceMoves[row * 3 + col] * progress;
                var rect = new Rect(area.x + (area.width - w * 3) / 2 + col * w + move.x, area.y + (area.height - h * 3) / 2 + row * h + move.y, w, h);
                Transform(rect.center, 1, PieceTurns[row * 3 + col] * progress);
                GUI.DrawTextureWithTexCoords(rect, resultSprite.texture,
                    new Rect((source.x + col * source.width / 3) / resultSprite.texture.width,
                        (source.y + (2 - row) * source.height / 3) / resultSprite.texture.height,
                        source.width / 3 / resultSprite.texture.width, source.height / 3 / resultSprite.texture.height));
                GUI.matrix = matrix;
            }
        }
        finally { GUI.color = previous; GUI.matrix = matrix; }
    }

    // Mythic success (design 6 #14, 7.1; mockup .fx): rotating rays, three glowing rings, two waves of
    // 20 spark bars and a white flash over them; the level-up / reroll version has two rings and 14 sparks.
    private static readonly (double Delay, int Color)[] Rings = { (0, 0), (0.23, 1), (0.40, 2) }, LiteRings = { (0, 0), (0.09, 1) };
    private void DrawMythicEffect(Rect area, double elapsed, bool lite)
    {
        if (Event.current.type != EventType.Repaint) return;
        EnsureFx();
        // Mockup .fx is overflow:hidden: nothing reaches the header or the status panel. Drawing is in the
        // clip's coordinates; Transform takes window coordinates (pivot).
        var pivot = area.center;
        var centre = new Vector2(area.width / 2, area.height / 2);
        var old = GUI.color;
        var matrix = GUI.matrix;
        GUI.BeginClip(area);
        try
        {
            if (!lite)
            {
                var (alpha, turn) = UiMotion.Rays(elapsed);
                Transform(pivot, 1, turn);
                GUI.color = new Color(1, 1, 1, alpha);
                GUI.DrawTexture(new Rect(centre.x - 750, centre.y - 750, 1500, 1500), raysTexture);
                GUI.matrix = matrix;
            }
            foreach (var (delay, tint) in lite ? LiteRings : Rings)
            {
                var (scale, alpha) = UiMotion.Ring(elapsed, delay, lite ? 1.1 : 1.5, lite ? 3f : 5.5f, lite ? 0.9f : 1);
                if (alpha <= 0) continue;
                var color = tint == 0 ? Myth : tint == 1 ? MythGold : Color.white;
                var size = RingSpan * scale;
                GUI.color = new Color(color.r, color.g, color.b, alpha);
                GUI.DrawTexture(new Rect(centre.x - size / 2, centre.y - size / 2, size, size), fxRingTexture);
            }
            // fxspark: bar `length` × `thick` with a 12 px glow, pushed out from the centre along its angle.
            var count = lite ? 14 : 40;
            var length = lite ? 20f : 30f;
            var thick = lite ? 4f : 6f;
            for (var spark = 0; spark < count; spark++)
            {
                double angle, delay; Color color;
                if (lite) { angle = spark * (360.0 / 14); delay = 0.02 + spark % 3 * 0.07; color = SparkColors[spark % 4]; }
                else
                {
                    var wave = spark / 20; var index = spark % 20;
                    angle = index * 18 + wave * 9; delay = wave * 1.25 + index % 5 * 0.04; color = SparkColors[(index + wave) % 4];
                }
                var (distance, stretch, alpha) = UiMotion.Spark(elapsed, delay, lite ? 0.9 : 1.2, lite ? 16 : 20, lite ? 260 : 460, lite ? 0.15 : 0.12);
                if (alpha <= 0) continue;
                var w = (length + 24) * stretch;
                var h = thick + 24;
                var middle = centre.x + distance + length / 2;
                Transform(pivot, 1, (float)angle);
                GUI.color = new Color(color.r, color.g, color.b, alpha);
                GUI.DrawTexture(new Rect(middle - w / 2, centre.y - h / 2, w, h), sparkTexture);
                GUI.matrix = matrix;
            }
            if (!lite)
            {
                var flash = UiMotion.Flash(elapsed);
                if (flash > 0) { GUI.color = new Color(1, 1, 1, flash); GUI.DrawTexture(new Rect(0, 0, area.width, area.height), Texture2D.whiteTexture); }
            }
        }
        finally { GUI.color = old; GUI.matrix = matrix; GUI.EndClip(); }
    }

    private static string ActionName(EnhanceQuote view) => view.Action switch
    {
        EnhanceAction.Evolve => Loc.L("★★ 진화", "★★ evolution"),
        EnhanceAction.EvolveThird => Loc.L("★★★ 진화", "★★★ evolution"),
        EnhanceAction.Transcend => Loc.L("초월", "Transcend"),
        EnhanceAction.Mystery => "???",
        EnhanceAction.Mythic => Loc.L("신화", "Mythic"),
        _ => Loc.L("일반 강화", "Enhance") + (view.HighRisk ? Loc.L(" · 고위험", " · high risk") : "")
    };

    private static Color ActionTint(EnhanceQuote view) => view.Action switch
    {
        EnhanceAction.Evolve => Purple,
        EnhanceAction.EvolveThird => StarTint(3),
        EnhanceAction.Transcend => Cyan,
        EnhanceAction.Mystery => Myth,
        EnhanceAction.Mythic => Myth,
        _ => Gold
    };
}
