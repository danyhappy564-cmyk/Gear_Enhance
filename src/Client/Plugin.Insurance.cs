using System;

namespace GearEnhance;

// Server-confirmed insurance notices and credit labels, formatted when the quote changes.
internal static class InsuranceText
{
    internal static string Lapsed => Loc.L("보험이 풀렸습니다. 다시 가입하세요", "Insurance has lapsed. Please insure again.");
    internal static string Notice(EnhanceQuote view) => view?.InsuranceLapsed == true && view.CurrentInsurance == InsuranceTier.None ? Lapsed : "";
    internal static string Notice(EnhanceReceipt receipt) => receipt?.InsuranceLapsed == true ? Lapsed : null;
    internal static string Credit(EnhanceQuote view) => view?.PreviousInsurancePaid > 0
        ? Loc.L($"이전 납부 {view.PreviousInsurancePaid:N0} ₽ 차감", $"Previous payment {view.PreviousInsurancePaid:N0} ₽ credited") : "";
}

// Enhancement insurance and plate registration (1.2.0); drawn by the insurance tab (1.4.0).
public sealed partial class Plugin
{
    private InsuranceTier insuranceTier = InsuranceTier.Advanced;
    private EnhanceQuote insuranceQuote;
    private string insuranceMessage = "";
    private EnhanceQuote insuranceTextQuote;
    private bool insuranceTextEnglish;
    private string insuranceHeaderNotice = "", insuranceCreditText = "";
    private static readonly InsuranceTier[] Tiers = { InsuranceTier.Basic, InsuranceTier.Advanced, InsuranceTier.Premium };

    private void UpdateInsuranceText(EnhanceQuote view)
    {
        if (ReferenceEquals(insuranceTextQuote, view) && insuranceTextEnglish == Loc.English) return;
        insuranceTextQuote = view; insuranceTextEnglish = Loc.English;
        insuranceHeaderNotice = InsuranceText.Notice(view);
        insuranceCreditText = InsuranceText.Credit(view);
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
            if (StatPatches.Registry.TotalLevel(itemId) == 0)
            {
                insuranceMessage = result ?? Loc.L("강화된 장비만 강화 보험에 가입할 수 있습니다.", "Only enhanced gear can be insured.");
                return;
            }
            var response = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>("/rf/gear-enhance/quote", QuoteBody(EnhanceAction.Insure)));
            if (current != generation) return;
            insuranceQuote = response; insuranceMessage = result ?? "";
        }
        catch (Exception ex) { insuranceMessage = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; }
    }

    // From the insurance tab (its quote names the fee) or the enhance tab (the upgrade quote's
    // plate difference). Never pay more than the fee the button showed.
    private void RegisterParts() => RegisterPartsFrom(insuranceQuote);

    private async void RegisterPartsFrom(EnhanceQuote shown)
    {
        if (busy || uncertain || itemId == null || shown == null || shown.UnregisteredParts == 0 && !shown.CanRegister) return;
        var fee = shown.Action == EnhanceAction.Insure || shown.Action == EnhanceAction.RegisterInserts ? shown.RegistrationFee : shown.InsertSurcharge;
        busy = true;
        EnhanceQuote registration = null;
        try
        {
            await GameBridge.Flush(session);
            registration = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>("/rf/gear-enhance/quote", QuoteBody(EnhanceAction.RegisterInserts)));
        }
        catch (Exception ex) { insuranceMessage = message = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; }
        // The pending server quote is now the registration; the shown quote must be reloaded.
        quote = null;
        if (registration == null) { RefreshTab(); return; }
        if (registration.Cost != fee || !registration.CanRegister)
        {
            var changed = Loc.L($"등록비가 {registration.Cost:N0} ₽로 바뀌었습니다. 확인하고 다시 눌러 주세요.",
                $"The registration fee changed to {registration.Cost:N0} ₽. Check it and press again.");
            if (tab == UiTab.Insurance) LoadInsuranceQuote(changed); else LoadQuote(changed);
            return;
        }
        RunManaged(registration, Loc.L("끼운 방탄판 적용 완료", "Inserted plates applied"),
            registration.CurrentInsurance != InsuranceTier.None && registration.InsertSurcharge > 0);
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
}
