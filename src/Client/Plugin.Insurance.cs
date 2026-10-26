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
        var action = tab == UiTab.Prestige ? EnhanceAction.PrestigeInsure : EnhanceAction.Insure;
        try
        {
            await GameBridge.Flush(session);
            // Plates swapped since the last sync are applied first, so the window shows the result.
            if (!weapon) await ApplyInserts(session);
            if (StatPatches.Registry.TotalLevel(itemId) == 0)
            {
                insuranceMessage = result ?? (action == EnhanceAction.PrestigeInsure
                    ? Loc.L("강화된 장비만 프레스티지 보험에 등록할 수 있습니다.", "Only enhanced gear can be registered for prestige insurance.")
                    : Loc.L("강화된 장비만 강화 보험에 가입할 수 있습니다.", "Only enhanced gear can be insured."));
                return;
            }
            var response = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>("/rf/gear-enhance/quote", QuoteBody(action)));
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
            if (ManagedTab) LoadInsuranceQuote(changed); else LoadQuote(changed);
            return;
        }
        RunManaged(registration, Loc.L("끼운 방탄판 적용 완료", "Inserted plates applied"),
            registration.CurrentInsurance != InsuranceTier.None && registration.InsertSurcharge > 0);
    }

    // Lapsed enhancement insurance in the stash, and prestige registrations owing a difference.
    private static int RenewCount()
    {
        var count = 0;
        foreach (var link in StatPatches.Registry.Policies.Insurance ?? Array.Empty<InsuranceLink>()) if (link?.Lapsed == true) count++;
        return count;
    }

    private static PrestigeState Prestige => new PrestigeState(StatPatches.Registry.Policies);
    private static bool PrestigeAtMax => Prestige.AtMax;
    private static int PrestigeDueCount()
    {
        if (PrestigeAtMax) return 0;
        var count = 0;
        foreach (var link in StatPatches.Registry.Policies.Prestige ?? Array.Empty<PrestigeLink>()) if (link != null && link.Fee > link.Paid) count++;
        return count;
    }

    // A renewal from the insurance or prestige list: quote that gear (it may not be the one in the
    // window) and pay only if the cost is still the one the row showed.
    private async void RenewFrom(string target, EnhanceAction action, InsuranceTier tier, int shown)
    {
        if (busy || uncertain || popup != PopupKind.None || string.IsNullOrEmpty(target)) return;
        busy = true;
        EnhanceQuote renewal = null;
        try
        {
            await GameBridge.Flush(session);
            renewal = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>("/rf/gear-enhance/quote",
                QuoteBody(action, target: target, tier: tier)));
        }
        catch (Exception ex) { insuranceMessage = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; }
        // The pending server quote is now the renewal; the shown quote must be reloaded either way.
        quote = insuranceQuote = null;
        if (renewal == null) { RefreshTab(); return; }
        var can = action == EnhanceAction.PrestigeInsure ? renewal.CanBuyPrestige : renewal.CanBuyInsurance;
        if (!can || renewal.Cost != shown)
        {
            // The list rows come from the state sync: refresh them, or the same mismatch repeats.
            try { await SyncEffects(session); }
            catch (Exception ex) { Logger.LogWarning(ex); }
            LoadInsuranceQuote(can ? Loc.L($"금액이 {renewal.Cost:N0} ₽로 바뀌었습니다. 확인하고 다시 눌러 주세요.", $"The amount changed to {renewal.Cost:N0} ₽. Check it and press again.")
                : Loc.L("지금은 갱신할 수 없습니다. 목록을 확인해 주세요.", "It cannot be renewed now. Check the list."));
            return;
        }
        RunManaged(renewal, action == EnhanceAction.PrestigeInsure ? Loc.L("프레스티지 보험 갱신 완료", "Prestige insurance renewed")
            : Loc.L($"{Loc.TierName(tier)} 강화 보험 다시 가입", $"{Loc.TierName(tier)} insurance renewed"), false);
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
