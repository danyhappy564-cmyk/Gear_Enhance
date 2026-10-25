using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

internal enum PopupKind { None, Charging, Result, RiskConfirm, MysteryConfirm, RerollConfirm }

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
                    result.Line = Loc.L($"신화  {from} → {receipt.Level}", $"Mythic  {from} → {receipt.Level}");
                    (result.EffectLabel, result.EffectNow, result.EffectNext) = MythicEffect(from, receipt.Level);
                    // Damage and penetration rise by the same percent; both get their own row.
                    if (weapon) (result.EffectLabel2, result.EffectNow2, result.EffectNext2) = (Loc.L("관통력", "Penetration"), result.EffectNow, result.EffectNext);
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
            // Popups slide 10 px into place over 0.3 s; they never start transparent (design 6).
            var y = overlay.center.y - height / 2 - (Reduced ? 0 : (float)(10 * (1 - UiMotion.Clamp(elapsed / UiMotion.PopTime))));
            var cardRect = new Rect(x, y, 620, height);
            Box(cardRect, HeaderFill, Edge);
            if (current.Mythic && elapsed > 0.75 && !Reduced)
            {
                var pulse = (float)(0.5 + 0.5 * Math.Sin((elapsed - 0.75) * Math.PI * 2 / 2.4));
                Outline(new Rect(x - 1, y - 1, 622, height + 2), Color.Lerp(Myth, MythGold, pulse), 2);
            }
            else Outline(new Rect(x - 1, y - 1, 622, height + 2), new Color(current.Tint.r, current.Tint.g, current.Tint.b, 0.35f));
            Fill(new Rect(x, y, 620, 5), current.Tint);
            var top = y + 27;
            Label(x, top, 620, 16, current.Kicker, popKicker); top += 28;
            if (current.Mythic || current.MythicLite)
            {
                var size = current.Mythic ? 46 : 30;
                var alpha = Reduced ? 1 : UiMotion.Fill(elapsed, 0.06, false);
                var old = GUI.color; GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
                Label(x, top, 620, size + 4, "✦", new GUIStyle(popTitle) { fontSize = size }, MythGold);
                GUI.color = old;
                top += size + 16;
            }
            var titleSize = current.Mythic ? 44 : current.MythicLite ? 36 : 34;
            Label(x + 20, top, 580, titleSize + 8, current.Title, new GUIStyle(popTitle) { fontSize = titleSize }, TitleColor(current, elapsed));
            top += titleSize + 20;
            Label(x + 32, top, 556, 24, current.Line, popLine); top += 36;
            if (current.Shatter) { DrawShatter(new Rect(x + 175, top, 270, 110), elapsed); top += 122; }
            if (current.EffectLabel != null)
            {
                DrawEffectRow(new Rect(x + 32, top, 556, 48), current.EffectLabel, current.EffectNow, current.EffectNext, current.EffectColor, elapsed);
                top += 60;
            }
            if (current.EffectLabel2 != null)
            {
                DrawEffectRow(new Rect(x + 32, top - 4, 556, 48), current.EffectLabel2, current.EffectNow2, current.EffectNext2, current.EffectColor, elapsed);
                top += 56;
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
        finally { drawingPopup = false; }
    }

    private void DrawEffectRow(Rect box, string label, string now, string next, Color tint, double elapsed)
    {
        Box(box, Deep, Line);
        Label(box.x + 16, box.y, 260, 48, label, new GUIStyle(body) { fontSize = 14 }, Soft);
        Label(box.xMax - 296, box.y, 140, 48, now, new GUIStyle(number) { fontSize = 22 });
        Label(box.xMax - 152, box.y, 20, 48, "→", smallCenter);
        var drop = Reduced ? 0 : 8 * (1 - UiMotion.Fill(elapsed, 0.35, false));
        Label(box.xMax - 132, box.y - drop, 116, 48, next, new GUIStyle(number) { fontSize = 22 }, tint);
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
                    : view.Action == EnhanceAction.Mythic ? Loc.L($"신화 {view.CurrentLevel} → {view.NextLevel}", $"Mythic {view.CurrentLevel} → {view.NextLevel}")
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
        if (current.EffectLabel != null) height += 60;
        if (current.EffectLabel2 != null) height += 56;
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
                    popupSkip = GUI.Toggle(new Rect(skip.x + 12, skip.y + 11, 18, 18), popupSkip, "");
                    Label(skip.x + 38, skip.y, skip.width - 44, skip.height, Loc.L("이 창을 닫을 때까지 다시 묻지 않기", "Don't ask again until this window closes"), body);
                    top += 50;
                }
                if (BigButton(new Rect(x + 32, top, 273, 46), Loc.L("취소", "Cancel"), ButtonKind.Dark, UiInteraction.Close) || Pressed(KeyCode.Escape)) main.Enqueue(CancelConfirm);
                if (BigButton(new Rect(x + 315, top, 273, 46), Loc.L("진행", "Proceed"), ButtonKind.Danger)) main.Enqueue(ProceedConfirm);
                break;
        }
    }

    private static bool Pressed(KeyCode key) => Event.current.type == EventType.KeyDown && Event.current.keyCode == key;

    private Color TitleColor(Look current, double elapsed)
    {
        if (Reduced) return current.Tint;
        if (current.Mythic || current.MythicLite)
        {
            // Rainbow sweep through pink, gold, white and blue (mockup shimmer, 2.4 s).
            var palette = new[] { Myth, MythGold, Color.white, Hex(0x9fd0ff), Myth };
            var t = (float)((elapsed / 2.4) % 1) * 4;
            var index = Math.Min(3, (int)t);
            return Color.Lerp(palette[index], palette[index + 1], t - index);
        }
        if (popup == PopupKind.MysteryConfirm) return Color.Lerp(current.Tint, Color.white, UiMotion.Glow(elapsed, 1.22, 0.35, false) * 0.6f);
        if (!current.Glow) return current.Tint;
        // Star evolutions peak with star2's loudest moment (1.6 s), the rest with success (0.19 s).
        return Color.Lerp(current.Tint, Color.white, UiMotion.Glow(elapsed, current.LateGlow ? 1.6 : 0.19, 0.5, false) * 0.55f);
    }

    private static readonly Vector2[] PieceMoves =
    {
        new Vector2(-70, -50), new Vector2(0, -70), new Vector2(75, -48), new Vector2(-95, 6), new Vector2(6, 26),
        new Vector2(98, -4), new Vector2(-72, 62), new Vector2(-4, 78), new Vector2(80, 58)
    };

    // Nine pieces of the gear picture fly apart from 0.06 s (in time with the destroy sound).
    private void DrawShatter(Rect area, double elapsed)
    {
        if (resultSprite == null || resultSprite.texture == null || Event.current.type != EventType.Repaint) return;
        // 0.95 s from 0.06 s, easing out; reduced motion shows the pieces where they land.
        var progress = Reduced ? 1f : (float)(1 - Math.Pow(1 - UiMotion.Clamp((elapsed - 0.06) / 0.95), 3));
        var source = resultSprite.rect;
        var fit = Math.Min(area.width / source.width, area.height / source.height);
        var w = source.width * fit / 3;
        var h = source.height * fit / 3;
        var previous = GUI.color;
        try
        {
            GUI.color = new Color(1, 0.62f, 0.52f, 1 - 0.65f * progress);
            for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
            {
                var move = PieceMoves[row * 3 + col] * progress;
                var rect = new Rect(area.x + (area.width - w * 3) / 2 + col * w + move.x, area.y + (area.height - h * 3) / 2 + row * h + move.y, w, h);
                GUI.DrawTextureWithTexCoords(rect, resultSprite.texture,
                    new Rect((source.x + col * source.width / 3) / resultSprite.texture.width,
                        (source.y + (2 - row) * source.height / 3) / resultSprite.texture.height,
                        source.width / 3 / resultSprite.texture.width, source.height / 3 / resultSprite.texture.height));
            }
        }
        finally { GUI.color = previous; }
    }

    // Mythic success (design 6 #14, 7.1): white flash, three rings and two bursts of sparks; the
    // level-up version has two rings and three small bursts. Rings are one prebuilt texture.
    private void DrawMythicEffect(Rect area, double elapsed, bool lite)
    {
        if (Event.current.type != EventType.Repaint) return;
        var centre = area.center;
        if (!lite) Fill(area, new Color(1, 1, 1, 0.9f * (1 - (float)UiMotion.Clamp(elapsed / 0.8))));
        var rings = lite ? new[] { (0.0, Myth), (0.09, MythGold) } : new[] { (0.0, Myth), (0.23, MythGold), (0.40, Color.white) };
        foreach (var (delay, color) in rings)
        {
            var (scale, alpha) = UiMotion.Ring(elapsed, delay, lite ? 1.1 : 1.5, lite ? 3f : 5.5f);
            if (alpha <= 0) continue;
            var size = 200 * scale;
            var old = GUI.color; GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.DrawTexture(new Rect(centre.x - size / 2, centre.y - size / 2, size, size), ringTexture);
            GUI.color = old;
        }
        var bursts = lite ? new[] { 0.02, 0.09, 0.16 } : new[] { 0.0, 1.25 };
        var count = lite ? 14 : 40;
        var colors = new[] { Myth, MythGold, Color.white };
        for (var spark = 0; spark < count; spark++)
        {
            var burst = bursts[spark % bursts.Length];
            var angle = (float)(spark * 137.5 + burst * 90) * Mathf.Deg2Rad;
            var (distance, alpha) = UiMotion.Spark(elapsed, burst, lite ? 0.9 : 1.2, lite ? 260 : 460);
            if (alpha <= 0) continue;
            var color = colors[spark % colors.Length];
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            for (var dot = 0; dot < 3; dot++)
            {
                var point = centre + direction * (distance - dot * 7);
                var dotSize = (lite ? 4 : 6) - dot;
                Fill(new Rect(point.x - dotSize / 2f, point.y - dotSize / 2f, dotSize, dotSize), new Color(color.r, color.g, color.b, alpha * (1 - dot * 0.3f)));
            }
        }
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
