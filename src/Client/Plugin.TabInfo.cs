using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GearEnhance;

// ??? (before mythic), enhancement insurance and help tabs (design 1, 5.1, 13).
public sealed partial class Plugin
{
    private GUIStyle insuranceNoteStyle, insuranceNoteSource;
    private GUIStyle insuranceCreditStyle, insuranceCreditSource;
    private readonly GUIContent insuranceNoteContent = new GUIContent();

    private void DrawMysteryTab()
    {
        GUI.BeginGroup(new Rect(BodyX, 180, BodyWidth, ButtonY - 180));
        midWidth = BodyWidth;
        SectionTitle(0, "???", Hex(0x5a2f52), Hex(0xff9be4));
        var panel = new Rect(0, 34, BodyWidth, ButtonY - 10 - 180 - 34);
        Box(panel, Hex(0x160f15), Hex(0x5a2f52));
        var view = preview;
        Label(0, panel.y + 70, BodyWidth, 110, "???", numberHuge, Myth);
        Label(0, panel.y + 190, BodyWidth, 24, Loc.L("★★★ 장비만 도전할 수 있는 마지막 단계입니다.", "The final step, open only to ★★★ gear."), richCenter);
        var chance = view?.SuccessPercent ?? MythicTuned.MysteryChance;
        var boxes = new Rect((BodyWidth - 420) / 2, panel.y + 232, 204, 92);
        Box(boxes, Hex(0x121a24), Hex(0x3b5f8a));
        Label(boxes.x, boxes.y + 10, boxes.width, 18, Loc.L("성공", "Success"), smallCenter);
        Label(boxes.x, boxes.y + 32, boxes.width, 50, $"{chance:0.##}%", numberCenter, Blue);
        var lose = new Rect(boxes.xMax + 12, boxes.y, 204, 92);
        // With the ??? ticket a failure keeps the ★★★ gear (the chance itself stays).
        var guarded = view?.UseTicket == true;
        Box(lose, guarded ? Hex(0x122017) : Hex(0x1f1312), guarded ? Hex(0x3d7a4e) : Hex(0x7a3530));
        Label(lose.x, lose.y + 10, lose.width, 18, guarded ? Loc.L("실패 · ★★★ 유지", "Fail · ★★★ kept") : Loc.L("파괴", "Destroyed"), smallCenter);
        Label(lose.x, lose.y + 32, lose.width, 50, $"{100 - chance:0.##}%", numberCenter, guarded ? Good : Loss);
        var zones = view?.ZoneOptions?.Length ?? 0;
        var extra = weapon ? "" : "\n" + Loc.L($"추가로 막을 수 있는 부위 {zones}곳 · 성공하면 무작위 {Math.Min(MythicTuned.ExtraZones, zones)}곳",
            $"{zones} zone(s) it can still cover · success adds {Math.Min(MythicTuned.ExtraZones, zones)} at random");
        Label(60, panel.y + 340, BodyWidth - 120, 60, Loc.L("성공률 올리기·즉시 강화는 쓸 수 없습니다. ??? 파괴 방지권(장비당 1장)을 쓰면 실패해도 장비가 남습니다. 성공하면 신화 단계가 열립니다.",
            "No boosts or instant. The ??? ticket (one per gear) keeps the gear on a failure. Success unlocks the Mythic stage.") + extra, wrapCenter);
        var ledx = view?.Materials?.Length > 0 ? view.Materials[0] : null;
        Label(0, panel.y + 410, BodyWidth, 22, Loc.L("비용 ", "Cost ") + Paint(view == null ? $"{MythicTuned.MysteryCost:N0} ₽" : $"{view.Cost:N0} ₽", Text)
            + (ledx == null ? "" : " · " + Paint($"LEDX {ledx.Count}", ledx.Owned >= ledx.Count ? Good : Loss) + Loc.L($" (보유 {ledx.Owned:N0})", $" (own {ledx.Owned:N0})")), richCenter);
        GUI.BeginGroup(new Rect(20, panel.y + 446, BodyWidth - 40, 44));
        midWidth = BodyWidth - 40;
        DrawTickets(0);
        GUI.EndGroup();
        midWidth = BodyWidth;
        Label(40, panel.yMax - 40, BodyWidth - 80, 30, message ?? "", wrapCenter);
        GUI.EndGroup();
        GUI.enabled = !busy && popup == PopupKind.None && !attemptTimeline.Active;
        if (BigButton(new Rect(BodyX, ButtonY, 233, ButtonHeight), Loc.L("닫기", "Close"), ButtonKind.Dark, UiInteraction.Close)) main.Enqueue(Close);
        var expired = view != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= view.ExpiresUnixSeconds;
        GUI.enabled = itemId != null && !busy && popup == PopupKind.None && !uncertain && (view == null || expired || view.CanPay);
        var caption = busy ? Loc.L("처리 중…", "Working…") : view == null || expired ? Loc.L("견적 다시 불러오기", "Reload quote")
            : !view.CanPay ? Loc.L("루블·LEDX 부족", "Not enough roubles or LEDX") : Loc.L("도전하기", "Challenge");
        if (BigButton(new Rect(BodyX + 243, ButtonY, BodyWidth - 243, ButtonHeight), caption, ButtonKind.Danger))
        {
            if (view == null || expired) main.Enqueue(RefreshQuote); else main.Enqueue(() => Execute(false));
        }
        GUI.enabled = true;
    }

    private void DrawInsuranceTab()
    {
        var view = new Rect(BodyX, 180, BodyWidth, ButtonY - 10 - 180);
        var scrolls = midHeight > view.height + 1;
        midWidth = scrolls ? BodyWidth - 16 : BodyWidth;
        midScroll = GUI.BeginScrollView(view, midScroll, new Rect(0, 0, midWidth, Math.Max(midHeight, view.height)));
        var shown = insuranceQuote;
        UpdateInsuranceText(shown);
        if (!ReferenceEquals(insuranceCreditSource, small))
        {
            insuranceCreditSource = small;
            insuranceCreditStyle = new GUIStyle(small) { fontSize = 11, wordWrap = true };
        }
        SectionTitle(0, Loc.L("강화 보험", "Enhancement insurance"), Hex(0x3a4148), Heading);
        var y = 34f;
        if (insuranceHeaderNotice.Length > 0)
        {
            Label(2, y, midWidth - 4, 24, insuranceHeaderNotice, bodyBold, Caution);
            y += 28;
        }
        var half = (midWidth - 10) / 2;
        var current = new Rect(0, y, half, 72);
        Box(current, Section, Line);
        Label(16, y + 10, half - 32, 18, Loc.L("지금 가입", "Current"), small);
        var tier = shown?.CurrentInsurance ?? StatPatches.Registry.Coverage(itemId ?? "");
        var percent = shown?.CurrentInsurancePercent ?? StatPatches.Registry.CoveragePercent(itemId ?? "");
        Label(16, y + 30, half - 32, 32, tier == InsuranceTier.None ? Loc.L("미가입", "None") : Loc.TierName(tier) + Loc.L($" · 반환 {percent:0.##}%", $" · return {percent:0.##}%"),
            new GUIStyle(itemTitle) { fontSize = 22 }, TierColor(tier));
        var value = new Rect(half + 10, y, half, 72);
        Box(value, Section, Line);
        Label(value.x + 16, y + 10, half - 32, 18, Loc.L("강화 가치", "Enhanced value"), small);
        Label(value.x + 16, y + 28, half - 32, 36, shown == null ? "—" : $"{shown.InsuredValue:N0} ₽", new GUIStyle(numberBig) { fontSize = 28 }, Text);
        y += 82;
        var width = (midWidth - 20) / 3;
        for (var index = 0; index < Tiers.Length; index++) DrawTier(new Rect(index * (width + 10), y, width, 210), Tiers[index], shown);
        y += 220;
        y = DrawReturning(y);
        if (!weapon) y = DrawPlateSection(y + 10, shown);
        var note = Loc.L("레이드에서 잃으면 확률만큼 강화된 채 우편으로 돌아옵니다. 한 번 쓰면 소모되고, 강화에 성공하거나 더 비싼 방탄판 차액을 내면 다시 가입해야 합니다. 상인 보험은 강화를 지켜 주지 않습니다(돌아와도 +0).",
            "If lost in a raid, it comes back enhanced by mail at this chance. Used up once it pays out; a successful upgrade or a paid plate difference needs a new policy. Trader insurance does not keep enhancements (returns +0).");
        if (!ReferenceEquals(insuranceNoteSource, wrap))
        {
            insuranceNoteSource = wrap;
            // insNote uses normal line height, 13 px text and 12 x 16 px padding inside its border.
            insuranceNoteStyle = new GUIStyle(wrap) { fontSize = 13 };
            insuranceNoteStyle.normal.textColor = Soft;
        }
        insuranceNoteContent.text = note;
        var noteHeight = insuranceNoteStyle.CalcHeight(insuranceNoteContent, midWidth - 34) + 26;
        Box(new Rect(0, y + 10, midWidth, noteHeight), Section, Line);
        Label(17, y + 23, midWidth - 34, noteHeight - 26, note, insuranceNoteStyle);
        y += 10 + noteHeight;
        Label(2, y + 8, midWidth - 4, 40, insuranceMessage ?? "", new GUIStyle(wrap) { fontSize = 12 });
        y += 48;
        if (Event.current.type == EventType.Repaint) midHeight = y;
        GUI.EndScrollView();
        GUI.enabled = !busy && popup == PopupKind.None;
        if (BigButton(new Rect(BodyX, ButtonY, 233, ButtonHeight), Loc.L("닫기", "Close"), ButtonKind.Dark, UiInteraction.Close)) main.Enqueue(Close);
        GUI.enabled = !busy && popup == PopupKind.None && !uncertain && shown?.CanBuyInsurance == true;
        var tierName = Loc.TierName(insuranceTier);
        var caption = shown == null ? Loc.L("견적 확인 중…", "Checking quote…")
            : shown.CanBuyInsurance ? Loc.L($"{tierName} 보험 가입 · {shown.Cost:N0} ₽", $"Buy {tierName} · {shown.Cost:N0} ₽")
            : shown.RegistrationFee > 0 ? Loc.L("방탄판 차액을 먼저 내 주세요", "Pay the plate difference first")
            : Loc.L("이미 같은 등급 이상 가입됨", "Already insured at this tier or higher");
        if (BigButton(new Rect(BodyX + 243, ButtonY, BodyWidth - 243, ButtonHeight), caption, ButtonKind.Gold) && shown != null)
            main.Enqueue(() => RunManaged(shown, Loc.L($"{tierName} 강화 보험 가입 완료", $"{tierName} insurance purchased"), false));
        GUI.enabled = true;
    }

    private void DrawTier(Rect rect, InsuranceTier tier, EnhanceQuote view)
    {
        var selected = tier == insuranceTier;
        var color = TierColor(tier);
        GUI.enabled = !busy && !uncertain && popup == PopupKind.None;
        var hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
        Box(rect, selected ? Hex(0x211d14) : Section, selected ? Gold : hovered ? Hex(0x4a525b) : Line);
        if (UiButton(rect, GUIContent.none, card)) main.Enqueue(() => SelectInsuranceTier(tier));
        GUI.enabled = true;
        var owned = view != null && view.CurrentInsurance == tier;
        Label(rect.x + 16, rect.y + 12, rect.width - 32, 22, Loc.TierName(tier), new GUIStyle(bodyBold) { fontSize = 16 }, color);
        if (owned) Label(rect.x + 16, rect.y + 12, rect.width - 32, 22, Loc.L("가입됨", "Owned"), tinyRight, color);
        Label(rect.x + 16, rect.y + 38, rect.width - 32, 16, Loc.L("강화된 채 반환", "Returned enhanced"), small);
        Label(rect.x + 16, rect.y + 54, rect.width - 32, 44, $"{view?.InsuranceChances[(int)tier] ?? 0:0.##}%", new GUIStyle(numberBig) { fontSize = 38 }, color);
        Label(rect.x + 16, rect.y + 100, rect.width - 32, 16, Loc.L("보험료", "Fee"), small);
        Label(rect.x + 16, rect.y + 116, rect.width - 32, 24, view == null ? "—" : $"{view.InsuranceFees[(int)tier]:N0} ₽", new GUIStyle(bodyBold) { fontSize = 18 }, Text);
        Label(rect.x + 16, rect.y + 140, rect.width - 32, 30, insuranceCreditText, insuranceCreditStyle, Caution);
        Label(rect.x + 16, rect.y + 174, rect.width - 32, 16, Loc.L("돌아오는 시간", "Return time"), small);
        Label(rect.x + 16, rect.y + 188, rect.width - 32, 20, ReturnDelay(view?.InsuranceHours[(int)tier] ?? 0), new GUIStyle(bodyBold) { fontSize = 15 }, color);
    }

    private static string ReturnDelay(double hours) => hours <= 0 ? Loc.L("즉시", "Instant")
        : hours % 24 == 0 ? Loc.L($"{hours / 24:0}일 뒤", $"After {hours / 24:0} day(s)") : Loc.L($"{hours:0.#}시간 뒤", $"After {hours:0.#} h");

    // Design 13: insured gear on its way back (this window's kind only), minutes left and a bar.
    private float DrawReturning(float y)
    {
        var list = new List<ReturnLink>();
        foreach (var entry in StatPatches.Registry.Returns) if (entry != null && entry.IsWeapon == weapon) list.Add(entry);
        var height = 34 + Math.Max(1, list.Count) * 34;
        Box(new Rect(0, y, midWidth, height), Section, Line);
        Label(14, y + 8, 200, 18, Loc.L("돌아오는 중", "On the way back"), bodyBold);
        Label(midWidth - 334, y + 8, 320, 18, Loc.L("레이드에서 잃은 강화 장비 · 우편 도착까지", "Enhanced gear lost in raids · until the mail arrives"), smallRight);
        if (list.Count == 0)
        {
            // Premium mails the gear back right after the raid, so it never waits in this list.
            var premium = StatPatches.Registry.Coverage(itemId ?? "") == InsuranceTier.Premium;
            Label(14, y + 34, midWidth - 28, 24, premium ? Loc.L("없음 · 프리미엄은 잃으면 레이드 직후 바로 우편으로 돌아옵니다(기다리는 시간 없음)",
                "None · Premium mails lost gear back right after the raid (no wait)") : Loc.L("없음", "None"), small);
            return y + height;
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (var index = 0; index < list.Count; index++)
        {
            var entry = list[index];
            var top = y + 32 + index * 34;
            var left = Math.Max(0, entry.DueUnixSeconds - now);
            var span = Math.Max(1, entry.DueUnixSeconds - entry.StartUnixSeconds);
            var color = TierColor(entry.Tier);
            var marker = entry.MythicLevel >= 0 ? " ✦" : " " + StarEvolution.Stars(Math.Max(1, entry.Stars));
            Label(14, top, 300, 18, ItemNameOf(entry.Template) + marker, bodyBold, Text);
            Label(320, top, 120, 18, Loc.TierName(entry.Tier), small, color);
            // A return with no delay (Premium) is mailed at once; it never shows a countdown.
            var instant = entry.DueUnixSeconds <= entry.StartUnixSeconds;
            Label(midWidth - 214, top, 200, 18, instant ? Loc.L("즉시", "Instant") : left <= 0 ? Loc.L("도착 처리 중", "Arriving") : Loc.L($"{left / 3600}시간 {left % 3600 / 60}분 남음", $"{left / 3600} h {left % 3600 / 60} min left"), smallRight, Text);
            Bar(new Rect(14, top + 21, midWidth - 28, 6), 0, instant ? 1 : 1 - left / (double)span, 1, BarNow, color);
        }
        return y + height;
    }

    private float DrawPlateSection(float y, EnhanceQuote view)
    {
        var height = 86f;
        Box(new Rect(0, y, midWidth, height), Section, Line);
        Label(14, y + 8, midWidth - 28, 20, view == null ? Loc.L("방탄판", "Plates") : Loc.L($"끼운 방탄판·내피 {view.InsertedParts}개 · ", $"Inserted plates/inserts: {view.InsertedParts} · ")
            + (view.UnregisteredParts == 0 ? Paint(Loc.L("모두 적용됨", "All applied"), Good)
                : view.RegistrationFee > 0 ? Paint(Loc.L($"더 비싼 방탄판 · 차액 {view.RegistrationFee:N0} ₽ 필요", $"Pricier plates · {view.RegistrationFee:N0} ₽ difference due"), Caution)
                : Paint(Loc.L($"미적용 {view.UnregisteredParts}개", $"Not applied: {view.UnregisteredParts}"), Caution))
            + (view.DurabilityPending > 0 ? Paint(Loc.L($" · 최대 내구도 초월 효과가 빠진 부품 {view.DurabilityPending}개", $" · {view.DurabilityPending} part(s) missing the max durability bonus"), Caution) : ""), body);
        Label(14, y + 30, midWidth - 28, 18, Loc.L("값을 치른 방탄판 이하라면 끼우는 즉시 무료로 적용됩니다. 더 비싼 방탄판만 차액을 냅니다.",
            "Plates worth no more than those paid for apply for free at once. Only pricier plates pay the difference."), small);
        GUI.enabled = !busy && !uncertain && popup == PopupKind.None && view?.CanRegister == true;
        // A registered plate taken out and put back returns without the 2-star max durability.
        var caption = view == null ? "—" : view.UnregisteredParts == 0 && view.DurabilityPending > 0 ? Loc.L("최대 내구도 다시 맞추기 · 무료", "Restore max durability · free")
            : view.UnregisteredParts == 0 ? Loc.L("모두 적용됨", "All applied")
            : view.RegistrationFee > 0 ? Loc.L($"차액 내고 적용 · {view.RegistrationFee:N0} ₽", $"Pay difference and apply · {view.RegistrationFee:N0} ₽")
            : Loc.L("방탄판 적용 · 무료", "Apply plates · free");
        MiniButton(new Rect(14, y + 52, midWidth - 28, 28), caption, RegisterParts);
        GUI.enabled = true;
        return y + height;
    }

    private static string ItemNameOf(string template)
    {
        try
        {
            var manager = GameBridge.RuntimeType("EFT.LocalizationManager");
            var localizer = manager?.GetMethod("LocalizedValue", new[] { typeof(string) });
            var instance = manager == null ? null : AccessTools.Property(manager, "Instance")?.GetValue(null);
            if (instance != null && localizer != null) return localizer.Invoke(instance, new object[] { template + " ShortName" })?.ToString() ?? template;
        }
        catch (Exception) { }
        return template ?? "";
    }

    // ---- help (design 1: rules moved out of the window) -----------------------------------

    private static (string Question, string Answer)[] HelpEntries() => new[]
    {
        (Loc.L("일반 강화", "Enhance"), Loc.L("항목마다 +10까지 따로 올립니다.\n성공률 75%에서 단계마다 5%p씩 내려갑니다.\n+5까지는 실패해도 비용만 나갑니다.",
            "Each item goes up to +10 on its own.\nThe chance starts at 75% and drops 5%p per level.\nUp to +5, a failure only costs money.")),
        (Loc.L("고위험 구간 (+6 시도부터)", "High risk (attempts from +6)"), Loc.L("무기·방어구 모두, 실패하면 그중 25%는 파괴, 75%는 1단계 하락입니다.\n파괴 방지·하락 방지로 각각 막을 수 있습니다. 추가 비용은 기본 강화 비용 × 시도 단계² × 파괴 방지 10% / 하락 방지 7% (예: +6 시도 = 기본 비용의 3.6배 / 2.52배).\n파괴될 수 있는 시도(파괴 방지 없이 +6 이상, 스타 진화)는 한 번 더 묻습니다. 창을 닫을 때까지 끌 수 있습니다.",
            "For weapons and armor, a failure destroys the item 25% of the time and drops one level 75% of the time.\nDestroy guard and drop guard block each one. They cost the base cost × attempt level² × 10% (destroy) / 7% (drop), e.g. a +6 attempt adds 3.6× / 2.52× the base cost.\nAn attempt that can destroy the item (unguarded +6 and up, star evolution) asks once more; you can silence it until the window closes.")),
        (Loc.L("성공률 올리기", "Boost chance"), Loc.L("재료를 넣은 개수만큼 성공률이 오릅니다. 재료는 2종까지 고를 수 있습니다.\n독택 개당 +0.1% (최대 5%) · 그래픽 카드 개당 +1% (최대 10%) · 비트코인 개당 +5% (최대 20%).\n재료마다 개당 루블도 함께 냅니다. 재료와 루블은 성공·실패와 관계없이 소모됩니다.",
            "Each piece adds chance. Pick up to 2 kinds.\nDogtag +0.1% each (max 5%) · Graphics card +1% each (max 10%) · Bitcoin +5% each (max 20%).\nEach piece also costs roubles. Materials and roubles are spent on success or failure.")),
        (Loc.L("즉시 강화", "Instant"), Loc.L("충전 게이지 없이 바로 결과를 봅니다.\n한 번에 50,000 ₽가 더 들고, 확률은 같습니다.\n일반 강화·초월·신화에서 쓸 수 있고, ★★·★★★ 진화와 ??? 에는 없습니다.",
            "Skips the charge gauge and shows the result at once.\nCosts 50,000 ₽ more per use; the chance is the same.\nAvailable for enhancing, transcending and mythic attempts, not for ★★ / ★★★ evolution or ???.")),
        (Loc.L("방어구 방탄판", "Armor plates"), Loc.L("방어구에 끼운 방탄판도 방어구 강화를 함께 받습니다.\n값을 치른 방탄판 이하라면 끼우는 즉시 무료로 적용되고, 더 비싼 방탄판만 차액을 한 번 냅니다.",
            "Plates in the armor share its enhancement.\nPlates worth no more than those paid for apply for free; only a pricier plate needs the difference paid once.")),
        (Loc.L("★★ / ★★★ 진화", "★★ / ★★★ evolution"), Loc.L("모든 항목이 +10이면 도전합니다.\n성공률 30% / 10%, 실패하면 장비가 파괴됩니다.\n성공하면 강화 효과 ×2 / ×3.\n파괴 방지권: 쓰면 시도할 때마다(성공·실패 관계없이) 1장 소모, 실패해도 장비가 남습니다. ★★용과 ★★★용은 따로이고, ★★ 성공 뒤 남은 ★★용은 사라집니다. 장비마다 ★★용 최대 3장(1장 100만 ₽), ★★★용 최대 2장(1장 270만 ₽)만 살 수 있고, 다 사면 더 살 수 없습니다.",
            "Open when every item is +10.\n30% / 10% chance; a failure destroys the item.\nSuccess multiplies the effects ×2 / ×3.\nDestruction ticket: one is spent on every attempt it is used for (success or failure); on failure the item stays. ★★ and ★★★ tickets are separate, and ★★ tickets left after the ★★ success are gone. Per item you can buy at most 3 for ★★ (1,000,000 ₽ each) and 2 for ★★★ (2,700,000 ₽ each), never more.")),
        (Loc.L("초월", "Transcendence"), Loc.L("★★ 이상 장비의 추가 트랙(각 5단계)입니다.\n파괴는 없고, 실패할수록 다음 성공률이 오릅니다(최대 +30%p).\n→4·→5는 실패 시 1단계 하락합니다. 하락 방지권은 →4 시도에만 쓸 수 있습니다.",
            "Extra tracks (5 levels each) for ★★ gear and up.\nNo destruction; each failure raises the next chance (up to +30%p).\n→4 and →5 drop one level on failure. Drop protection is for the →4 attempt only.")),
        (Loc.L("??? / 신화", "??? / Mythic"), Loc.L("★★★ 장비에만 ??? 칸이 열립니다. 50% 성공 / 50% 파괴, 도전 비용은 8,000,000 ₽ + LEDX 1개입니다.\n??? 파괴 방지권: 장비당 1장만(추가 구매 불가), 1장 5,000,000 ₽. 쓰면 그 시도에 1장 소모되고, 실패해도 ★★★ 장비가 그대로 남습니다(성공률은 그대로 50%).\n성공하면 별 대신 ✦ 신화 표시가 붙고 신화 단계(0~5)가 열립니다. 성공률 40·30·20·15·10%, 실패할 때마다 +3%p(최대 +30%p). 비용은 500만 ₽에서 단계마다 ×1.1 + LEDX 1개. →3·→4·→5는 실패 시 1단계 하락하고, 하락 방지권은 →3 시도에만 쓸 수 있습니다.\n무기: 탄 데미지·관통력 단계당 +3% (최대 15%), 이 총에 든 탄의 불이익(명중률 −·반동 +·내구도 소모 +·과열 +·고장 확률) 단계당 16% 감소(최대 80%). 이 총으로 쏠 때만 적용됩니다.\n방탄복: 아직 못 막는 부위 중 무작위 2곳을 더 막고, 신화 단계마다 받는 피해 -10% (최대 50%, 머리에 맞으면 절반). 헬멧·마스크: 단계마다 -5% (최대 25%).\n추가 부위는 한 곳씩 다시 뽑을 수 있습니다: 200만 ₽ + 그래픽 카드 1개, 다시 뽑을 때마다 ×1.5.",
            "The ??? slot opens only on ★★★ gear. 50% success / 50% destroyed; a try costs 8,000,000 ₽ + 1 LEDX.\n??? ticket: one per gear, ever, 5,000,000 ₽. Using it spends it on that try; a failure then keeps the ★★★ gear (the chance stays 50%).\nSuccess swaps the stars for a ✦ Mythic mark and opens Mythic levels 0-5: 40·30·20·15·10%, +3%p per failure (up to +30%p), 5,000,000 ₽ ×1.1 per level + 1 LEDX. →3, →4 and →5 drop one level on failure; drop protection is for the →3 attempt only.\nWeapons: bullet damage and penetration +3% per level (max 15%), and the rounds in this gun lose 16% of their penalties (accuracy −, recoil +, wear +, heat +, malfunction chance) per level (max 80%), only when fired from this gun.\nBody armor: two random zones it did not cover become covered, then damage taken -10% per level (max 50%, half on head hits). Helmets and masks: -5% per level (max 25%).\nEach extra zone can be rerolled: 2,000,000 ₽ + 1 graphics card, ×1.5 per reroll.")),
        (Loc.L("강화 보험", "Enhancement insurance"), Loc.L("레이드에서 잃었을 때 확률만큼 강화된 채 우편으로 돌아옵니다.\n일반 20% · 1일 뒤 / 고급 55% · 12시간 뒤 / 프리미엄 99% · 즉시.\n한 번 쓰면 소모되고, 강화하면 다시 가입해야 합니다.",
            "If lost in a raid, the gear comes back enhanced by mail at the tier chance.\nBasic 20% · after 1 day / Advanced 55% · after 12 h / Premium 99% · instant.\nUsed up once it pays out; enhancing again needs a new policy."))
    };

    private void DrawHelpTab()
    {
        var view = new Rect(BodyX, 180, BodyWidth, ButtonY - 10 - 180);
        var scrolls = midHeight > view.height + 1;
        midWidth = scrolls ? BodyWidth - 16 : BodyWidth;
        midScroll = GUI.BeginScrollView(view, midScroll, new Rect(0, 0, midWidth, Math.Max(midHeight, view.height)));
        SectionTitle(0, Loc.L("도움말", "Help"), Hex(0x3a4148), Heading);
        var y = 34f;
        var entries = HelpEntries();
        var top = y;
        for (var index = 0; index < entries.Length; index++)
        {
            var (question, answer) = entries[index];
            var row = new Rect(1, y, midWidth - 2, 48);
            GUI.enabled = popup == PopupKind.None;
            if (GUI.enabled && row.Contains(Event.current.mousePosition)) Fill(row, Hex(0x181c21));
            var open = helpOpen == index;
            var picked = index;
            if (UiButton(row, GUIContent.none, card)) main.Enqueue(() => helpOpen = helpOpen == picked ? -1 : picked);
            GUI.enabled = true;
            Label(17, y, midWidth - 120, 48, question, new GUIStyle(bodyBold) { fontSize = 15 }, Text);
            Label(midWidth - 100, y, 84, 48, open ? Loc.L("접기", "Hide") : Loc.L("열기", "Show"), smallRight);
            y += 48;
            if (open)
            {
                var height = wrap.CalcHeight(new GUIContent(answer), midWidth - 32);
                Label(16, y, midWidth - 32, height, answer, wrap);
                y += height + 14;
            }
            Fill(new Rect(1, y - 1, midWidth - 2, 1), Hex(0x22282e));
        }
        Outline(new Rect(0, top, midWidth, y - top), Line);
        if (Event.current.type == EventType.Repaint) midHeight = y + 4;
        GUI.EndScrollView();
        GUI.enabled = popup == PopupKind.None && !busy;
        if (BigButton(new Rect(BodyX, ButtonY, BodyWidth, ButtonHeight), Loc.L("닫기", "Close"), ButtonKind.Dark, UiInteraction.Close)) main.Enqueue(Close);
        GUI.enabled = true;
    }
}
