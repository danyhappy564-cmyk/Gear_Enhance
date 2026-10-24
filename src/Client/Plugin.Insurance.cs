using System;
using UnityEngine;

namespace GearEnhance;

// Enhancement insurance and plate registration (1.2.0), drawn over the main window.
public sealed partial class Plugin
{
    private bool insuranceVisible, drawingInsurance;
    private InsuranceTier insuranceTier = InsuranceTier.Advanced;
    private EnhanceQuote insuranceQuote;
    private string insuranceMessage = "";
    // Controls under the overlay must not take its clicks (IMGUI hands events to the first drawn).
    private bool InputLocked => insuranceVisible && !drawingInsurance;
    private static readonly InsuranceTier[] Tiers = { InsuranceTier.Basic, InsuranceTier.Advanced, InsuranceTier.Premium };

    private void OpenInsurance()
    {
        if (busy || resultVisible || uncertain || itemId == null || evolutionMode) return;
        insuranceVisible = true; insuranceQuote = null; insuranceMessage = "";
        generation++; quote = preview = null;
        PlayInteraction(UiInteraction.Open);
        LoadInsuranceQuote(null);
    }

    private void CloseInsurance()
    {
        if (busy) return;
        insuranceVisible = false; insuranceQuote = null; generation++;
        // The insurance quote replaced the server's pending enhancement quote.
        RefreshQuote();
    }

    private void SelectInsuranceTier(InsuranceTier tier)
    {
        if (busy || insuranceTier == tier) return;
        insuranceTier = tier;
        LoadInsuranceQuote(null);
    }

    private async void LoadInsuranceQuote(string result)
    {
        if (busy || uncertain || itemId == null) return;
        busy = true; insuranceQuote = null;
        insuranceMessage = result ?? Loc.L("보험 견적을 불러오는 중입니다…", "Loading insurance quote…");
        var current = generation;
        try
        {
            await GameBridge.Flush(session);
            // Plates swapped since the last sync are applied first, so the window shows the result.
            if (!weapon) await ApplyInserts(session);
            var response = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>("/rf/gear-enhance/quote", new
            {
                ProtocolVersion = EnhanceProtocol.Version, ItemId = itemId, Path = selectedPath, Settings = Settings(),
                Action = EnhanceAction.Insure, Tier = insuranceTier
            }));
            if (current != generation) return;
            insuranceQuote = response; insuranceMessage = result ?? "";
        }
        catch (Exception ex) { insuranceMessage = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; }
    }

    private async void RegisterParts()
    {
        var shown = insuranceQuote;
        if (busy || uncertain || itemId == null || shown == null || !shown.CanRegister) return;
        busy = true;
        EnhanceQuote registration = null;
        try
        {
            await GameBridge.Flush(session);
            registration = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>("/rf/gear-enhance/quote", new
            {
                ProtocolVersion = EnhanceProtocol.Version, ItemId = itemId, Path = selectedPath, Settings = Settings(),
                Action = EnhanceAction.RegisterInserts
            }));
        }
        catch (Exception ex) { insuranceMessage = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; }
        if (registration == null) return;
        // Never pay more than the fee the button showed.
        if (registration.Cost != shown.RegistrationFee)
        {
            LoadInsuranceQuote(Loc.L($"등록비가 {registration.Cost:N0} ₽로 바뀌었습니다. 확인하고 다시 눌러 주세요.",
                $"The registration fee changed to {registration.Cost:N0} ₽. Check it and press again."));
            return;
        }
        RunManaged(registration, Loc.L("끼운 방탄판 적용 완료", "Inserted plates applied"));
    }

    private async void RunManaged(EnhanceQuote view, string done)
    {
        if (busy || uncertain || view == null) return;
        if (view.ExpiresWithin(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), QuoteSubmitMargin))
        {
            LoadInsuranceQuote(Loc.L("견적 유효 시간이 지나 다시 불러왔습니다. 다시 눌러 주세요.", "The quote expired and was reloaded. Press again."));
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
            result = done + Loc.L($" · {receipt.Paid:N0} ₽ 소모", $" · spent {receipt.Paid:N0} ₽");
            // The game's own "insured" sound, as in the trader insurance screen.
            if (view.Action == EnhanceAction.Insure) main.Enqueue(PlayInsured);
            await SyncEffects(session);
            main.Enqueue(ItemMarkers.RefreshAll);
        }
        catch (Exception ex)
        {
            uncertain = submitted;
            insuranceMessage = ex.GetBaseException().Message + (submitted ? "\n" + UncertainNotice : "");
            Logger.LogError(ex);
        }
        finally
        {
            busy = false;
            if (result != null) main.Enqueue(() => LoadInsuranceQuote(result));
        }
    }

    private void PlayInsured()
    {
        if (soundsEnabled?.Value != true || uiFeedbackUnavailable) return;
        try { GameFeedback.PlayInsured(); }
        catch (Exception ex)
        {
            uiFeedbackUnavailable = true;
            Logger.LogWarning("보험 가입 소리 연결 실패: " + ex.GetBaseException().Message);
        }
    }

    // Header line of the main window and the panel: tier in its color, or none.
    private static string InsuranceStatus(InsuranceTier tier, double percent) => tier == InsuranceTier.None
        ? Paint(Loc.L("강화 보험 없음", "Not insured"), Muted)
        : Loc.L("강화 보험 ", "Insured: ") + Paint($"{Loc.TierName(tier)} {percent:0.##}%", TierColor(tier));

    private void DrawInsurancePanel()
    {
        drawingInsurance = true;
        try
        {
            const float left = 64, width = 632, top = 104;
            Fill(new Rect(1, 55, WindowWidth - 2, ActiveWindowHeight - 56), new Color(0, 0, 0, 0.72f));
            Surface(new Rect(40, top, 680, 700), cardGradient, Gold);
            Label(left, top + 12, 560, 34, Loc.L("강화 보험 · 방탄판", "Enhancement insurance · Plates"), heading);
            GUI.enabled = !busy;
            if (UiButton(new Rect(670, top + 12, 36, 32), "×", secondary, UiInteraction.Close)) main.Enqueue(CloseInsurance);
            GUI.enabled = true;
            Label(left, top + 52, width, 40, Loc.L("레이드에서 잃으면 등급 확률로 ", "If lost in a raid, it may be mailed back ")
                + Paint(Loc.L("강화된 상태 그대로", "still enhanced"), Good) + Loc.L(" 우편으로 돌아옵니다. ", " (chance by tier). ")
                + Paint(Loc.L("상인 보험은 강화를 지켜 주지 않습니다(돌아와도 +0).", "Trader insurance does not keep enhancements (returns +0)."), Loss), richStatus);
            var view = insuranceQuote;
            Label(left, top + 98, width, 26, view == null ? "" : InsuranceStatus(view.CurrentInsurance, view.CurrentInsurancePercent)
                + Loc.L(" · 강화 가치 ", " · Enhancement value ") + Paint($"{view.InsuredValue:N0} ₽", Gold), richBody);
            for (var index = 0; index < Tiers.Length; index++)
                DrawTier(new Rect(left + index * 216, top + 132, 200, 100), Tiers[index], view);
            GUI.enabled = !busy && !uncertain && view?.CanBuyInsurance == true;
            var buy = new Rect(left, top + 246, width, 46);
            Surface(buy, goldGradient, new Color(0.95f, 0.85f, 0.64f));
            var tierName = Loc.TierName(insuranceTier);
            var caption = view == null ? Loc.L("견적 확인 중…", "Checking quote…")
                : view.CanBuyInsurance ? Loc.L($"{tierName} 보험 가입  {view.Cost:N0} ₽", $"Buy {tierName} insurance  {view.Cost:N0} ₽")
                : view.RegistrationFee > 0 ? Loc.L("아래에서 방탄판 차액을 먼저 내 주세요", "Pay the plate difference below first")
                : Loc.L("이미 같은 등급 이상 가입됨", "Already insured at this tier or higher");
            if (UiButton(buy, caption, primary) && view != null)
                main.Enqueue(() => RunManaged(view, Loc.L($"{tierName} 강화 보험 가입 완료", $"{tierName} insurance purchased")));
            GUI.enabled = true;
            Label(left, top + 298, width, 40, Paint(Loc.L("한 번 잃으면 보험이 소모됩니다.", "Coverage is used up after one loss."), Caution)
                + Loc.L(" 강화에 성공하거나 더 비싼 방탄판 차액을 내면 강화 가치가 바뀌어 보험이 풀립니다(다시 가입).",
                    " A successful upgrade or paying for pricier plates changes the insured value and ends coverage (buy it again)."), richStatus);

            Fill(new Rect(left, top + 346, width, 1), Line);
            if (view != null && view.IsWeapon)
                Label(left, top + 358, width, 26, Loc.L("무기는 방탄판 등록이 없습니다.", "Weapons have no plates to register."), small);
            else
            {
                Label(left, top + 356, width, 26, view == null ? "" : Loc.L($"끼운 방탄판·내피 {view.InsertedParts}개 · ", $"Inserted plates/inserts: {view.InsertedParts} · ")
                    + (view.UnregisteredParts == 0 ? Paint(Loc.L("모두 적용됨", "All applied"), Good)
                        : view.RegistrationFee > 0 ? Paint(Loc.L($"더 비싼 방탄판 · 차액 {view.RegistrationFee:N0} ₽ 필요", $"Pricier plates · {view.RegistrationFee:N0} ₽ difference due"), Caution)
                        : Paint(Loc.L($"미적용 {view.UnregisteredParts}개", $"Not applied: {view.UnregisteredParts}"), Caution))
                    + (view.DurabilityPending > 0 ? Paint(Loc.L($" · 최대 내구도 초월 효과가 빠진 부품 {view.DurabilityPending}개", $" · {view.DurabilityPending} part(s) missing the max durability bonus"), Caution) : ""), richBody);
                // Slot rule (1.3.0): the armor remembers the plate value paid for, not each plate.
                Label(left, top + 384, width, 58, Paint(Loc.L("방탄판은 칸 기준으로 자동 적용됩니다.", "Plates apply by slot, automatically."), Caution)
                    + Loc.L(" 이 방어구에 값을 치른 방탄판 이하라면 새것·헌것·같은 종류 상관없이 끼우는 즉시 무료로 적용됩니다. 더 비싼 방탄판만 차액을 내고, 그 뒤로는 그 값까지 자유롭게 갈아끼웁니다.",
                        " Plates worth no more than those already paid for apply for free the moment they go in, new or worn. Only pricier plates pay the difference; after that, swap freely up to that value."), richStatus);
                GUI.enabled = !busy && !uncertain && view?.CanRegister == true;
                var register = new Rect(left, top + 448, width, 42);
                Surface(register, cardGradient, Gold);
                // A registered plate taken out and put back returns without the 2-star max durability.
                var registerCaption = view == null ? "—" : view.UnregisteredParts == 0 && view.DurabilityPending > 0 ? Loc.L("최대 내구도 다시 맞추기  무료", "Restore max durability  Free")
                    : view.UnregisteredParts == 0 ? Loc.L("모두 적용됨", "All applied")
                    : view.RegistrationFee > 0 ? Loc.L($"차액 내고 적용  {view.RegistrationFee:N0} ₽", $"Pay difference and apply  {view.RegistrationFee:N0} ₽")
                    : Loc.L("방탄판 적용  무료", "Apply plates  Free");
                if (UiButton(register, registerCaption, secondary)) main.Enqueue(RegisterParts);
                GUI.enabled = true;
            }
            // How it combines with trader insurance (bought separately in the game's own menu).
            Fill(new Rect(left, top + 502, width, 1), Line);
            Label(left, top + 512, width, 22, Loc.L("강화 보험 성공 → ", "Roll succeeds → ")
                + Paint(Loc.L("강화된 그대로 바로 우편 반환", "mailed back enhanced at once"), Good)
                + Loc.L(" (같은 장비의 상인 보험 반환은 빠짐)", " (its trader copy is removed)"), richSmall);
            Label(left, top + 536, width, 22, Loc.L("실패하거나 강화 보험 없음 → ", "Roll fails or not insured → ")
                + Paint(Loc.L("강화 소멸", "enhancement lost"), Loss)
                + Loc.L(" · 상인 보험이 있으면 +0 장비로 반환", " · trader insurance returns it at +0"), richSmall);
            Label(left, top + 566, width, 58, insuranceMessage, statusStyle);
            GUI.enabled = !busy;
            var close = new Rect(left, top + 636, width, 46);
            Surface(close, cardGradient, Line);
            if (UiButton(close, Loc.L("닫고 강화로 돌아가기", "Close and return to enhancing"), secondary, UiInteraction.Close)) main.Enqueue(CloseInsurance);
            GUI.enabled = true;
        }
        finally { drawingInsurance = false; }
    }

    private void DrawTier(Rect rect, InsuranceTier tier, EnhanceQuote view)
    {
        var selected = tier == insuranceTier;
        var color = TierColor(tier);
        GUI.enabled = !busy && !uncertain;
        Surface(rect, selected ? selectedGradient : cardGradient, selected ? color : Line);
        if (selected) Fill(new Rect(rect.x + 2, rect.y + 1, rect.width - 4, 2), color);
        if (UiButton(rect, GUIContent.none, card)) main.Enqueue(() => SelectInsuranceTier(tier));
        Label(rect.x + 12, rect.y + 10, rect.width - 24, 26, Paint(Loc.TierName(tier) + Loc.L(" 보험", ""), color), richHeading);
        Label(rect.x + 12, rect.y + 40, rect.width - 24, 24, Loc.L("강화된 채 반환 ", "Returned enhanced: ")
            + Paint($"{view?.InsuranceChances[(int)tier] ?? 0:0.##}%", color), richCentered);
        Label(rect.x + 12, rect.y + 66, rect.width - 24, 26, view == null ? "—" : $"{view.InsuranceFees[(int)tier]:N0} ₽", selected ? noteStyleCentered : centeredSmall);
        GUI.enabled = true;
    }
}
