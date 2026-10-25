using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

internal enum UiTab { Enhance, Star2, Star3, Transcend, Mythic, Insurance, Help }

// 1.4.0 tab state and the quote → attempt → result flow of every tab.
public sealed partial class Plugin
{
    private UiTab tab;
    private bool transcendAutomatic, useTicket, skipRiskConfirm;
    private TranscendTrack transcendTrack = TranscendTrack.Second;
    // Chance materials (design 2): which kinds are picked and how many of each.
    private readonly bool[] boostOn = new bool[3];
    private readonly int[] boostCount = { 20, 1, 1 };
    private double boostDirtyAt = -1;
    // A material change requotes quietly: no loading line, the picked numbers stay and the controls
    // stay lit (user 2026-10-26: the old numbers and "견적을 불러오는 중" flashed between picks).
    private bool boostQuiet;
    private bool BoostLocal => boostDirtyAt >= 0 || boostQuiet;
    private bool Steady => !busy || boostQuiet;
    private double motionStart = -100;
    private int helpOpen;
    private Vector2 midScroll;
    private float midHeight = 1;

    private int MythicLevel => StatPatches.Registry.MythicLevel(itemId ?? "");
    private int StarCount => StatPatches.Registry.StarLevel(itemId ?? "");
    private bool Reduced => reducedMotion?.Value == true;
    private double Motion => Clock - motionStart;
    private void ReplayMotion() { motionStart = Clock; tweens.Clear(); }

    private struct TabEntry
    {
        public UiTab Tab; public string Mark, Label, Sub; public Color Color, Ring; public bool Open;
    }

    private IEnumerable<TabEntry> TabEntries()
    {
        var id = itemId ?? "";
        var stars = StarCount;
        var mythic = MythicLevel;
        var tier = StatPatches.Registry.Coverage(id);
        yield return new TabEntry { Tab = UiTab.Enhance, Mark = "+", Color = Gold, Open = true, Label = Loc.L("일반 강화", "Enhance"), Sub = Loc.L("항목별 +10", "Each to +10") };
        yield return new TabEntry { Tab = UiTab.Star2, Mark = "★★", Color = Purple, Open = true, Label = Loc.L("★★성", "★★ Star"),
            Sub = stars >= 2 ? Loc.L("진화 완료", "Evolved") : Loc.L("진화 · 효과 ×2", "Evolve · ×2") };
        yield return new TabEntry { Tab = UiTab.Star3, Mark = "★★★", Color = StarTint(3), Open = true, Label = Loc.L("★★★성", "★★★ Star"),
            Sub = stars >= 3 ? Loc.L("진화 완료", "Evolved") : Loc.L("진화 · 효과 ×3", "Evolve · ×3") };
        yield return new TabEntry { Tab = UiTab.Transcend, Mark = "◆", Color = Cyan, Open = stars >= 2, Label = Loc.L("초월", "Transcend"),
            Sub = stars < 2 ? Loc.L("★★ 이후 열림", "Opens after ★★")
                : Loc.L($"2성 {StatPatches.Registry.TranscendLevel(id, TranscendTrack.Second)}/5 · 3성 {StatPatches.Registry.TranscendLevel(id, TranscendTrack.Third)}/5",
                    $"2★ {StatPatches.Registry.TranscendLevel(id, TranscendTrack.Second)}/5 · 3★ {StatPatches.Registry.TranscendLevel(id, TranscendTrack.Third)}/5") };
        yield return new TabEntry { Tab = UiTab.Mythic, Mark = mythic >= 0 ? "✦" : "◈", Color = Myth, Open = stars >= 3,
            Label = mythic >= 0 ? Loc.L("신화", "Mythic") : "???",
            Sub = stars < 3 ? Loc.L("★★★ 이후 열림", "Opens after ★★★") : mythic < 0 ? "50 / 50"
                : weapon ? Loc.L($"데미지·관통 {mythic}/5", $"Dmg · pen {mythic}/5")
                : Loc.L($"추가 부위 {StatPatches.Registry.MythicZones(id).Length}곳 · {mythic}/5", $"+{StatPatches.Registry.MythicZones(id).Length} zones · {mythic}/5") };
        yield return new TabEntry { Tab = UiTab.Insurance, Mark = "P", Color = tier == InsuranceTier.None ? Hex(0xffc46b) : TierColor(tier), Open = true,
            Label = Loc.L("보험", "Insurance"), Ring = tier == InsuranceTier.None ? Color.clear : TierColor(tier),
            Sub = tier == InsuranceTier.None ? Loc.L("미가입", "None") : Loc.TierName(tier) + $" {StatPatches.Registry.CoveragePercent(id):0.##}%" };
        yield return new TabEntry { Tab = UiTab.Help, Mark = "?", Color = Muted, Open = true, Label = Loc.L("도움말", "Help"), Sub = Loc.L("규칙 요약", "Rules") };
    }

    private void SelectTab(UiTab next)
    {
        if (busy || popup != PopupKind.None || uncertain || itemId == null) return;
        // The current tab again only replays the panel and bars (mockup: every tab click replays them).
        if (tab == next) { ReplayMotion(); return; }
        tab = next;
        protectDestruction = protectDowngrade = useTicket = false;
        destructionConfirmed = false;
        if (next == UiTab.Transcend)
        {
            var stars = StarCount;
            transcendTrack = stars >= 3 && StatPatches.Registry.TranscendLevel(itemId, TranscendTrack.Second) >= TranscendRules.MaxLevel
                ? TranscendTrack.Third : TranscendTrack.Second;
            try { transcendAutomatic = weapon && TranscendPatches.Automatic(selectedItem); }
            catch (Exception ex) { transcendAutomatic = false; Logger.LogWarning("초월 총기 발사 방식 확인 실패(견적이 오면 맞춰짐): " + ex.Message); }
        }
        midScroll = Vector2.zero;
        quote = preview = null; generation++; ReplayMotion();
        RefreshTab();
    }

    private void RefreshTab()
    {
        if (tab == UiTab.Insurance) LoadInsuranceQuote(null);
        else if (tab == UiTab.Help) { quote = preview = null; message = ""; }
        else RefreshQuote();
    }

    private EnhanceAction TabAction => tab switch
    {
        UiTab.Star2 => EnhanceAction.Evolve,
        UiTab.Star3 => EnhanceAction.EvolveThird,
        UiTab.Transcend => EnhanceAction.Transcend,
        UiTab.Mythic => MythicLevel < 0 ? EnhanceAction.Mystery : EnhanceAction.Mythic,
        UiTab.Insurance => EnhanceAction.Insure,
        _ => EnhanceAction.Upgrade
    };
    private bool Evolving => tab == UiTab.Star2 || tab == UiTab.Star3;
    private int EvolutionStars => tab == UiTab.Star3 ? 3 : 2;
    // The ticket line of this tab: ★★ / ★★★ evolution, or ??? (MythicRules.MysteryTicket).
    private bool MysteryTab => tab == UiTab.Mythic && MythicLevel < 0;
    private int TicketTarget => MysteryTab ? MythicRules.MysteryTicket : EvolutionStars;

    // Why this tab has nothing to quote now (shown instead of a quote), or null.
    private string NoQuoteReason()
    {
        var id = itemId ?? "";
        var stars = StarCount;
        switch (tab)
        {
            case UiTab.Enhance:
                return (StatPatches.Registry.Find(id, selectedPath)?.Level ?? 0) >= maxLevel.Value
                    ? Loc.L("이 항목은 최대 단계입니다. 다른 항목을 선택해 주세요.", "This track is at the maximum level. Choose another track.") : null;
            case UiTab.Star2:
            case UiTab.Star3:
                if (stars >= EvolutionStars) return StarEvolution.Stars(EvolutionStars) + Loc.L(" 진화를 마쳤습니다.", " evolution complete.");
                if (stars < EvolutionStars - 1) return Loc.L("★★ 진화를 먼저 마쳐 주세요.", "Finish the ★★ evolution first.");
                return StatPatches.Registry.IsComplete(id) ? null
                    : Loc.L($"모든 항목을 +{maxLevel.Value}까지 강화하면 도전할 수 있습니다.", $"Max every track to +{maxLevel.Value} to try.");
            case UiTab.Transcend:
                if (stars < TranscendRules.StarsNeeded(transcendTrack)) return Loc.L("이 초월은 아직 잠겨 있습니다.", "This transcendence is still locked.");
                return StatPatches.Registry.TranscendLevel(id, transcendTrack) >= TranscendRules.MaxLevel
                    ? Loc.L("이 초월은 최대 단계입니다. 다른 트랙을 골라 주세요.", "This track is at the maximum level. Choose the other one.") : null;
            case UiTab.Mythic:
                if (stars < 3) return Loc.L("★★★ 진화 후 열립니다.", "Opens after ★★★.");
                return MythicLevel >= MythicRules.MaxLevel ? Loc.L("최대 신화 단계입니다.", "Maximum mythic level.") : null;
            default: return "";
        }
    }

    private object QuoteBody(EnhanceAction action, bool instant = false, int ticketStars = 0, int slot = 0) => new
    {
        ProtocolVersion = EnhanceProtocol.Version, ItemId = itemId, Path = selectedPath, Settings = Settings(),
        Protection = new EnhanceProtection { Destruction = protectDestruction, Downgrade = protectDowngrade },
        Boost = action == EnhanceAction.Mystery ? Array.Empty<BoostChoice>() : BoostRequest(), Action = action,
        Track = action == EnhanceAction.Transcend ? transcendTrack : TranscendTrack.None, Tier = insuranceTier,
        Instant = instant, UseTicket = useTicket && (action == EnhanceAction.Evolve || action == EnhanceAction.EvolveThird || action == EnhanceAction.Mystery),
        TicketStars = ticketStars, Slot = slot
    };

    private BoostChoice[] BoostRequest()
    {
        var chosen = new List<BoostChoice>();
        foreach (var kind in SuccessBoost.Kinds)
            if (boostOn[(int)kind] && boostCount[(int)kind] > 0) chosen.Add(new BoostChoice { Kind = kind, Count = boostCount[(int)kind] });
        return chosen.ToArray();
    }

    private void RefreshQuote() => LoadQuote(null);
    private async void LoadQuote(string resultMessage, bool quiet = false)
    {
        if (busy || uncertain || itemId == null) return;
        if (tab == UiTab.Insurance) { LoadInsuranceQuote(resultMessage); return; }
        busy = true; quote = null; boostQuiet = quiet;
        if (!quiet)
        {
            boostDirtyAt = -1;
            message = resultMessage ?? Loc.L("견적을 불러오는 중입니다…", "Loading quote…");
        }
        var current = generation;
        try
        {
            await GameBridge.Flush(session);
            if (firstQuote)
            {
                if (!weapon) await ApplyInserts(session);
                if (!await SyncEffects(session))
                    throw Loc.Invalid("강화 기록을 동기화하지 못했습니다. 서버 모드 버전과 연결을 확인하고 다시 불러와 주세요.",
                        "Could not sync enhancement records. Check the server mod version and connection, then reload.");
                selectedPath = StatPatches.Registry.Find(itemId)?.Path ?? selectedPath;
                firstQuote = false;
            }
            var why = NoQuoteReason();
            if (why != null)
            {
                preview = null;
                message = resultMessage ?? why;
                return;
            }
            var response = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>("/rf/gear-enhance/quote", QuoteBody(TabAction)));
            // Another pick during a quiet requote: TickBoost asks again once this one ends. Any other
            // change (F12 settings) keeps its notice.
            if (current != generation) { if (!quiet || boostDirtyAt < 0) message = SettingsChanged; return; }
            quote = preview = response; weapon = response.IsWeapon;
            if (response.Action == EnhanceAction.Transcend) transcendAutomatic = response.Automatic;
            message = resultMessage ?? ""; destructionConfirmed = false;
            protectDestruction = response.Protection.Destruction; protectDowngrade = response.Protection.Downgrade;
            useTicket = response.UseTicket;
        }
        catch (Exception ex) { message = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; boostQuiet = false; }
    }

    private void SelectPath(EnhancePath path)
    {
        if (busy || popup != PopupKind.None || tab != UiTab.Enhance || selectedPath == path) return;
        selectedPath = path; protectDestruction = protectDowngrade = false;
        quote = preview = null; generation++; ReplayMotion(); RefreshQuote();
    }

    private void SelectTrack(TranscendTrack track)
    {
        if (busy || popup != PopupKind.None || uncertain || tab != UiTab.Transcend || transcendTrack == track || itemId == null
            || StarCount < TranscendRules.StarsNeeded(track)) return;
        transcendTrack = track; protectDowngrade = false;
        quote = preview = null; generation++; ReplayMotion(); RefreshQuote();
    }

    private void SetProtection(bool destruction, bool enabled)
    {
        if (busy || popup != PopupKind.None || uncertain) return;
        if (destruction) protectDestruction = enabled; else protectDowngrade = enabled;
        generation++; quote = null; destructionConfirmed = false;
        RefreshQuote();
    }

    private void SetTicket(bool enabled)
    {
        if (busy || popup != PopupKind.None || uncertain || !Evolving && !MysteryTab) return;
        useTicket = enabled; generation++; quote = null; destructionConfirmed = false;
        RefreshQuote();
    }

    // Material picks change locally at once; the quote follows 0.3 s after the last click.
    private void ToggleBoost(BoostMaterial kind)
    {
        if (!Steady || popup != PopupKind.None || uncertain) return;
        var index = (int)kind;
        if (!boostOn[index])
        {
            var picked = 0;
            foreach (var on in boostOn) if (on) picked++;
            if (picked >= BoostTuning().MaxKinds) return;
            boostCount[index] = Math.Max(1, Math.Min(boostCount[index], BoostLimit(kind)));
        }
        boostOn[index] = !boostOn[index];
        BoostChanged();
    }

    private void SetBoostCount(BoostMaterial kind, int count)
    {
        if (!Steady || popup != PopupKind.None || uncertain) return;
        var index = (int)kind;
        count = Math.Max(1, Math.Min(count, BoostLimit(kind)));
        if (count == boostCount[index]) return;
        boostCount[index] = count;
        BoostChanged();
    }

    // At most the cap's worth and what the stash holds (when the quote has told us).
    private int BoostLimit(BoostMaterial kind)
    {
        var max = SuccessBoost.MaxCount(kind, BoostTuning());
        var owned = preview?.BoostOwned;
        return owned != null && owned.Length > (int)kind && preview.BoostAvailable ? (int)Math.Max(1, Math.Min(max, owned[(int)kind])) : Math.Max(1, max);
    }

    private void BoostChanged()
    {
        generation++; destructionConfirmed = false;
        boostDirtyAt = Clock + 0.3;
    }

    private void TickBoost()
    {
        if (boostDirtyAt < 0 || Clock < boostDirtyAt || busy || popup != PopupKind.None) return;
        boostDirtyAt = -1;
        LoadQuote(null, true);
    }

    // Charge (or instant) attempt of the current tab.
    private void Execute(bool instant)
    {
        if (busy || popup != PopupKind.None || quote == null || uncertain || attemptTimeline.Active) return;
        var expired = DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= quote.ExpiresUnixSeconds;
        if (expired || boostDirtyAt >= 0) { RefreshQuote(); return; }
        if (instant && !quote.InstantAvailable) return;
        // Design 12.1: an attempt that can destroy the gear asks once more; ??? always asks
        // (with its ticket too: the 8,000,000 ₽ and the ticket are still spent).
        if ((quote.CanDestroy || quote.Action == EnhanceAction.Mystery) && !destructionConfirmed)
        {
            // F12 "파괴 확인 창" off, or "don't ask again" ticked in this window: no question (never for ???).
            var ask = quote.Action == EnhanceAction.Mystery || destructionPrompt?.Value != false && !skipRiskConfirm;
            if (ask)
            {
                OpenConfirm(quote.Action == EnhanceAction.Mystery ? PopupKind.MysteryConfirm : PopupKind.RiskConfirm, instant);
                return;
            }
            destructionConfirmed = true;
        }
        if (instant) { StartInstant(); return; }
        // Design 7: star evolution and ??? have their own 4.412 s charges; enhance,
        // transcend and mythic use the trimmed 3.647 s charge. The gauge is the clip length.
        var cue = quote.Action == EnhanceAction.Mystery ? FeedbackCue.MysteryAttempt
            : quote.Action == EnhanceAction.Evolve || quote.Action == EnhanceAction.EvolveThird ? FeedbackCue.EvolutionAttempt : FeedbackCue.Attempt;
        var duration = cue == FeedbackCue.MysteryAttempt ? mysteryAttemptDuration
            : cue == FeedbackCue.EvolutionAttempt ? evolutionAttemptDuration : attemptDuration;
        if (quote.ExpiresWithin(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), duration + QuoteSubmitMargin))
        {
            LoadQuote(Loc.L("견적 유효 시간이 얼마 남지 않아 새로 불러왔습니다. 비용을 확인하고 다시 눌러 주세요.",
                "The quote was about to expire, so it was reloaded. Check the cost and press again."));
            return;
        }
        var started = Clock;
        attemptUsesAudioClock = false;
        if (soundsEnabled.Value && providedSounds.Value && feedbackAudio != null && !providedFeedbackUnavailable)
        {
            try { started = feedbackAudio.StartAttempt(soundVolume.Value / 100, cue); attemptUsesAudioClock = true; }
            catch (Exception ex) { ProvidedAudioFailed(ex); }
        }
        if (!soundsEnabled.Value) LogDetailed("강화 충전음 생략: F12 효과음 OFF");
        if (!attemptUsesAudioClock && soundsEnabled.Value) PlayFeedback(cue, false);
        if (!attemptTimeline.Begin(started, duration)) return;
        attemptWallStart = Clock;
        pendingAttempt = quote; pendingDestructionConfirmation = destructionConfirmed; pendingGeneration = generation;
        busy = true; quote = null; message = "";
        OpenCharging(pendingAttempt);
    }

    // Instant: the same attempt with the fee, quoted again and paid only if nothing else moved.
    private async void StartInstant()
    {
        var shown = quote;
        if (busy || shown == null) return;
        busy = true; quote = null; message = Loc.L("즉시 강화 준비 중…", "Preparing instant…");
        var current = generation;
        EnhanceQuote fast = null;
        try
        {
            await GameBridge.Flush(session);
            fast = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>("/rf/gear-enhance/quote", QuoteBody(shown.Action, true)));
        }
        catch (Exception ex) { message = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        if (fast == null || current != generation || fast.Cost != shown.Cost + fast.InstantFee || fast.SuccessPercent != shown.SuccessPercent
            || fast.CanDestroy != shown.CanDestroy)
        {
            busy = false;
            if (fast != null) LoadQuote(Loc.L("비용이나 확률이 바뀌었습니다. 확인하고 다시 눌러 주세요.", "The cost or chance changed. Check it and press again."));
            return;
        }
        SubmitAttempt(fast, destructionConfirmed, generation);
    }

    private async void SubmitAttempt(EnhanceQuote attempt, bool confirmed, int expectedGeneration)
    {
        var submitted = false;
        var completed = false;
        string result = null;
        try
        {
            if (expectedGeneration != generation || !visible || !GameBridge.InStash(uiContext))
                throw Loc.Invalid("강화 준비 중 설정이나 화면이 바뀌었습니다. 견적을 다시 불러와 주세요.", "Settings or screen changed while preparing. Reload the quote.");
            await GameBridge.Flush(session);
            if (expectedGeneration != generation || !visible || !GameBridge.InStash(uiContext))
                throw Loc.Invalid("강화 준비가 취소됐습니다. 견적을 다시 불러와 주세요.", "Preparation was cancelled. Reload the quote.");
            message = Loc.L("강화 결과를 확인하는 중입니다…", "Checking the result…");
            submitted = true;
            var ok = await GameBridge.WithTimeout(GameBridge.Execute(session, attempt.QuoteId, confirmed && attempt.CanDestroy), 30000);
            if (!ok)
            {
                CloseCharging();
                message = Loc.L("강화가 거절됐습니다. 서버 오류 안내를 확인하고 견적을 다시 불러와 주세요.", "The enhancement was rejected. Check the server error notice and reload the quote.");
                return;
            }
            var receipt = await GameBridge.WithTimeout(GameBridge.Query<EnhanceReceipt>("/rf/gear-enhance/receipt", new { ProtocolVersion = EnhanceProtocol.Version, attempt.QuoteId }));
            StatPatches.Registry.ApplyReceipt(receipt, GameBridge.ProfileId(session));
            completed = true;
            result = ResultText.Status(receipt);
            ShowResult(receipt, attempt);
            RefreshGameplay();
            await SyncEffects(session);
            if (!receipt.Success && receipt.FailureMode == FailureMode.Destroy) { itemId = null; selectedItem = null; preview = null; }
        }
        catch (Exception ex)
        {
            CloseCharging();
            uncertain = submitted;
            message = ex.GetBaseException().Message + (submitted
                ? "\n" + UncertainNotice
                : Loc.L("\n아직 강화를 전송하지 않았습니다.", "\nThe enhancement was not sent yet."));
            Logger.LogError(ex);
        }
        finally
        {
            busy = false; destructionConfirmed = false;
            attemptTimeline.Reset(); pendingAttempt = null;
            if (completed && itemId != null && !uncertain) main.Enqueue(() => LoadQuote(result));
        }
    }

    // Ticket purchase, zone reroll, insurance and plate registration: no gauge, the fee is the cost.
    private async void BuyTicket()
    {
        var shown = preview;
        if (busy || popup != PopupKind.None || uncertain || itemId == null || shown == null || shown.TicketStars < 2) return;
        busy = true;
        EnhanceQuote purchase = null;
        try
        {
            await GameBridge.Flush(session);
            purchase = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>("/rf/gear-enhance/quote",
                QuoteBody(EnhanceAction.BuyTicket, ticketStars: shown.TicketStars)));
        }
        catch (Exception ex) { message = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; }
        // The pending server quote is now the purchase; the attempt quote must be reloaded either way.
        quote = null;
        if (purchase == null) { RefreshQuote(); return; }
        if (purchase.Cost != shown.TicketPrice)
        {
            LoadQuote(Loc.L($"방지권 가격이 {purchase.Cost:N0} ₽로 바뀌었습니다. 확인하고 다시 눌러 주세요.", $"The ticket price changed to {purchase.Cost:N0} ₽. Check it and press again."));
            return;
        }
        RunManaged(purchase, Loc.L("파괴 방지권 1장 구매", "Bought one destruction ticket"), false);
    }

    private async void AskReroll(int slot)
    {
        if (busy || popup != PopupKind.None || uncertain || itemId == null || weapon || MythicLevel < 0) return;
        busy = true;
        EnhanceQuote reroll = null;
        try
        {
            await GameBridge.Flush(session);
            reroll = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>("/rf/gear-enhance/quote", QuoteBody(EnhanceAction.Reroll, slot: slot)));
        }
        catch (Exception ex) { message = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; }
        quote = null;
        if (reroll == null) { RefreshQuote(); return; }
        popupQuote = reroll;
        OpenConfirm(PopupKind.RerollConfirm, false);
    }

    private async void RunManaged(EnhanceQuote view, string done, bool showResult)
    {
        if (busy || uncertain || view == null) return;
        if (view.ExpiresWithin(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), QuoteSubmitMargin))
        {
            RefreshTab();
            return;
        }
        busy = true;
        var submitted = false;
        string result = null;
        try
        {
            await GameBridge.Flush(session);
            submitted = true;
            var ok = await GameBridge.WithTimeout(GameBridge.Execute(session, view.QuoteId, false), 30000);
            if (!ok)
            {
                result = Loc.L("서버가 거절했습니다. 서버 오류 안내를 확인해 주세요.", "The server rejected it. Check the server error notice.");
                return;
            }
            var receipt = await GameBridge.WithTimeout(GameBridge.Query<EnhanceReceipt>("/rf/gear-enhance/receipt",
                new { ProtocolVersion = EnhanceProtocol.Version, view.QuoteId }));
            StatPatches.Registry.ApplyReceipt(receipt, GameBridge.ProfileId(session));
            result = done + Loc.L($" · {receipt.Paid:N0} ₽ 소모", $" · spent {receipt.Paid:N0} ₽");
            // The game's own "insured" sound, as in the trader insurance screen.
            if (view.Action == EnhanceAction.Insure) main.Enqueue(PlayInsured);
            if (showResult) ShowResult(receipt, view);
            await SyncEffects(session);
            main.Enqueue(ItemMarkers.RefreshAll);
        }
        catch (Exception ex)
        {
            uncertain = submitted;
            message = insuranceMessage = ex.GetBaseException().Message + (submitted ? "\n" + UncertainNotice : "");
            Logger.LogError(ex);
        }
        finally
        {
            busy = false;
            var text = result;
            if (text != null) main.Enqueue(() => { if (tab == UiTab.Insurance) LoadInsuranceQuote(text); else LoadQuote(text); });
        }
    }
}
