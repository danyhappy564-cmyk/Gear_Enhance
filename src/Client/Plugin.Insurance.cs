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
        if (busy || uncertain || itemId == null || shown == null || shown.UnregisteredParts == 0) return;
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
        RunManaged(registration, Loc.L("끼운 방탄판 등록 완료", "Inserted plates registered"));
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

    private void DrawInsurancePanel()
    {
        drawingInsurance = true;
        try
        {
            Fill(new Rect(1, 55, WindowWidth - 2, ActiveWindowHeight - 56), new Color(0, 0, 0, 0.72f));
            Surface(new Rect(50, 140, 660, 590), cardGradient, Gold);
            Label(74, 152, 520, 34, Loc.L("강화 보험 · 방탄판 등록", "Enhancement insurance · plates"), heading);
            GUI.enabled = !busy;
            if (UiButton(new Rect(654, 152, 36, 32), "×", secondary, UiInteraction.Close)) main.Enqueue(CloseInsurance);
            GUI.enabled = true;
            Label(74, 190, 612, 40, Loc.L("레이드에서 잃으면 등급 확률로 강화된 상태 그대로 우편으로 돌아옵니다. 상인 보험은 강화를 지켜 주지 않습니다(돌아와도 +0).",
                "If lost in a raid, the tier's chance mails it back still enhanced. Trader insurance does not keep enhancement (returns +0)."), statusStyle);
            var view = insuranceQuote;
            Label(74, 234, 612, 26, view == null ? "" : Loc.L("현재 강화 보험: ", "Current: ")
                + (view.CurrentInsurance == InsuranceTier.None ? Loc.L("없음", "none") : $"{Loc.TierName(view.CurrentInsurance)} {view.CurrentInsurancePercent:0.##}%")
                + Loc.L($" · 보험 기준 강화 가치 {view.InsuredValue:N0} ₽", $" · insured value {view.InsuredValue:N0} ₽"), body);
            for (var index = 0; index < Tiers.Length; index++)
                DrawTier(new Rect(74 + index * 208, 268, 196, 92), Tiers[index], view);
            GUI.enabled = !busy && !uncertain && view?.CanBuyInsurance == true;
            var buy = new Rect(74, 372, 612, 46);
            Surface(buy, goldGradient, new Color(0.95f, 0.85f, 0.64f));
            var tierName = Loc.TierName(insuranceTier);
            var caption = view == null ? Loc.L("견적 확인 중…", "Checking quote…")
                : view.CanBuyInsurance ? Loc.L($"{tierName} 보험 가입  {view.Cost:N0} ₽", $"Buy {tierName}  {view.Cost:N0} ₽")
                : view.RegistrationFee > 0 ? Loc.L("아래에서 방탄판을 먼저 등록해 주세요", "Register the plates below first")
                : Loc.L("이미 같은 등급 이상 가입됨", "Same or higher tier already bought");
            if (UiButton(buy, caption, primary) && view != null) main.Enqueue(() => RunManaged(view, Loc.L($"{tierName} 강화 보험 가입 완료", $"{tierName} insurance bought")));
            GUI.enabled = true;
            Label(74, 422, 612, 40, Loc.L("한 번 잃으면 보험이 소모됩니다. 강화에 성공하거나 더 비싼 방탄판을 등록하면 강화 가치가 바뀌어 보험이 풀립니다(다시 가입).",
                "Used up when the gear is lost once. A successful upgrade or more valuable plates change the value and end it (buy again)."), statusStyle);

            Fill(new Rect(74, 472, 612, 1), Line);
            if (view != null && view.IsWeapon)
                Label(74, 484, 612, 26, Loc.L("무기는 방탄판 등록이 없습니다.", "Weapons have no plates to register."), small);
            else
            {
                Label(74, 480, 612, 26, view == null ? "" : Loc.L($"끼운 방탄판·삽입물 {view.InsertedParts}개 · 미등록 {view.UnregisteredParts}개",
                    $"Inserted plates/inserts {view.InsertedParts} · unregistered {view.UnregisteredParts}"), body);
                Label(74, 508, 612, 40, Loc.L("강화할 때 끼워 둔 방탄판만 방어구 효과·표식을 받습니다. 새 방탄판은 등록하면 적용되고, 더 비싼 방탄판은 지난 단계의 차액을 냅니다.",
                    "Only plates inserted when enhancing share the armor's effect and badge. Register new ones; pricier plates pay the price difference for past levels."), statusStyle);
                GUI.enabled = !busy && !uncertain && view?.UnregisteredParts > 0;
                var register = new Rect(74, 556, 612, 42);
                Surface(register, cardGradient, Gold);
                var registerCaption = view == null ? "—" : view.UnregisteredParts == 0 ? Loc.L("모두 등록됨", "All registered")
                    : view.RegistrationFee > 0 ? Loc.L($"방탄판 등록  {view.RegistrationFee:N0} ₽", $"Register plates  {view.RegistrationFee:N0} ₽")
                    : Loc.L("방탄판 등록  무료", "Register plates  free");
                if (UiButton(register, registerCaption, secondary)) main.Enqueue(RegisterParts);
                GUI.enabled = true;
            }
            Label(74, 610, 612, 58, insuranceMessage, statusStyle);
            GUI.enabled = !busy;
            var close = new Rect(74, 672, 612, 44);
            Surface(close, cardGradient, Line);
            if (UiButton(close, Loc.L("닫고 강화로 돌아가기", "Close and back to enhance"), secondary, UiInteraction.Close)) main.Enqueue(CloseInsurance);
            GUI.enabled = true;
        }
        finally { drawingInsurance = false; }
    }

    private void DrawTier(Rect rect, InsuranceTier tier, EnhanceQuote view)
    {
        var selected = tier == insuranceTier;
        GUI.enabled = !busy && !uncertain;
        Surface(rect, selected ? selectedGradient : cardGradient, selected ? Gold : Line);
        if (UiButton(rect, GUIContent.none, card)) main.Enqueue(() => SelectInsuranceTier(tier));
        Label(rect.x + 12, rect.y + 8, rect.width - 24, 26, Loc.TierName(tier) + Loc.L(" 보험", ""), pathHeading);
        Label(rect.x + 12, rect.y + 36, rect.width - 24, 24, Loc.L("강화된 채 반환 ", "Returns enhanced ")
            + $"{view?.InsuranceChances[(int)tier] ?? 0:0.##}%", centeredSmall);
        Label(rect.x + 12, rect.y + 60, rect.width - 24, 24, view == null ? "—" : $"{view.InsuranceFees[(int)tier]:N0} ₽", selected ? noteStyleCentered : centeredSmall);
        GUI.enabled = true;
    }
}
