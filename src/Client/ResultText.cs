using System;
using System.Collections.Generic;

namespace GearEnhance;

internal static class ResultText
{
    internal static string Title(EnhanceReceipt receipt, bool complete) => receipt.Transcend != TranscendTrack.None ? TranscendTitle(receipt)
        : receipt.Success
        ? receipt.IsEvolution ? receipt.StarLevel >= 3 ? Loc.L("3차 스타 강화 진화 성공!", "3rd star evolved!") : Loc.L("스타 강화 진화 성공!", "Star evolved!")
            : Loc.L("강화 성공!", "Enhanced!")
        : receipt.FailureMode == FailureMode.Destroy ? Loc.L("장비 파괴", "Gear destroyed")
        : receipt.FailureMode == FailureMode.Downgrade ? Loc.L("강화 단계 하락", "Level dropped") : Loc.L("강화 실패", "Enhancement failed");
    internal static string Detail(EnhanceReceipt receipt) => receipt.Transcend != TranscendTrack.None ? TranscendDetail(receipt)
        : receipt.Success
        ? receipt.IsEvolution ? StarEvolution.Stars(StarEvolution.Level(true, receipt.StarLevel))
                + Loc.L(" 진화 완료 · 모든 강화 효과에 추가 보너스 적용", " evolved · bonus applied to every effect")
            : EnhanceEffects.Name(receipt.Path) + " +" + receipt.Level + Loc.L(" 달성", " reached")
        : receipt.DestructionPrevented ? Loc.L("파괴 방지 발동 · 장비와 모든 강화 단계를 지켰습니다.", "Destruction protection triggered · gear and all levels kept.")
        : receipt.DowngradePrevented ? Loc.L("단계 하락 방지 발동 · 모든 강화 단계를 지켰습니다.", "Downgrade protection triggered · all levels kept.")
        : receipt.FailureMode == FailureMode.Destroy ? Loc.L("장비 본체·부착물·탄창·탄약이 파괴되었습니다.", "The gear, attachments, magazines and ammo were destroyed.")
        : receipt.FailureMode == FailureMode.Downgrade ? Loc.F($"{EnhanceEffects.Name(receipt.Path)} +{receipt.Level}로 하락 · 다른 항목은 유지됩니다.",
            $"{EnhanceEffects.Name(receipt.Path)} dropped to +{receipt.Level} · other tracks kept.")
        : receipt.FailureMode == FailureMode.ZeroDurability ? Loc.L("현재 내구도 0 · 장비와 강화 단계는 유지됩니다.", "Current durability 0 · gear and levels kept.")
        : Loc.L("비용만 소모 · 장비와 강화 단계는 유지됩니다.", "Fee only · gear and levels kept.");

    // One status line under the window after an attempt (the popup shows the details).
    internal static string Status(EnhanceReceipt receipt) => receipt.Action switch
    {
        EnhanceAction.Mystery => receipt.Success ? Loc.L("신화 달성!", "Mythic reached!")
            : receipt.TicketUsed ? Loc.L("??? 실패 · 방지권으로 ★★★ 유지", "??? failed · ticket kept ★★★") : Loc.L("??? 실패 · 장비 파괴", "??? failed · gear destroyed"),
        EnhanceAction.Mythic => receipt.Success ? Loc.F($"신화 {receipt.Level}단계 달성", $"Mythic level {receipt.Level}")
            : receipt.FailureMode == FailureMode.Downgrade ? Loc.F($"신화 {receipt.Level}단계로 하락", $"Mythic dropped to {receipt.Level}")
            : Loc.F($"신화 강화 실패 · 천장 +{receipt.Pity:0.##}%p", $"Mythic failed · pity +{receipt.Pity:0.##}%p"),
        EnhanceAction.Reroll => Loc.L("추가 부위를 다시 뽑았습니다", "Extra zone rerolled"),
        _ => Title(receipt, receipt.IsEvolution && receipt.Success) + " · " + Detail(receipt)
    } + Loc.F($" · {receipt.Paid:N0} ₽ 소모", $" · spent {receipt.Paid:N0} ₽");

    // 1.3.0 transcendence: never destroys; a failure keeps or drops one level and raises pity.
    internal static string Track(TranscendTrack track) => track == TranscendTrack.Third ? Loc.L("3성 초월", "3-star transcendence") : Loc.L("2성 초월", "2-star transcendence");
    private static string TranscendTitle(EnhanceReceipt receipt) => receipt.Success ? Loc.L("초월 성공!", "Transcended!")
        : receipt.DowngradePrevented ? Loc.L("초월 실패 · 하락 방지", "Failed · downgrade prevented")
        : receipt.FailureMode == FailureMode.Downgrade ? Loc.L("초월 단계 하락", "Transcendence dropped") : Loc.L("초월 실패", "Transcendence failed");
    private static string TranscendDetail(EnhanceReceipt receipt)
    {
        var pity = Loc.F($" · 천장 +{receipt.Pity:0.##}%p", $" · pity +{receipt.Pity:0.##}%p");
        return receipt.Success ? Track(receipt.Transcend) + Loc.F($" {receipt.Level}단계 달성", $" level {receipt.Level} reached")
            : receipt.DowngradePrevented ? Loc.F($"단계 하락 방지 발동 · {receipt.Level}단계 유지", $"Downgrade protection triggered · level {receipt.Level} kept") + pity
            : receipt.FailureMode == FailureMode.Downgrade ? Track(receipt.Transcend) + Loc.F($" {receipt.Level}단계로 하락 · 장비 파괴 없음", $" dropped to level {receipt.Level} · nothing destroyed") + pity
            : Track(receipt.Transcend) + Loc.F($" {receipt.Level}단계 유지 · 재료 소모", $" level {receipt.Level} kept · materials used") + pity;
    }
}

// Transcendence window text without Unity types (testable).
internal static class TranscendText
{
    // Fixed names of the materials the server asks for (TranscendRules ids).
    private static readonly Dictionary<string, (string Korean, string English)> Names = new Dictionary<string, (string, string)>
    {
        [TranscendRules.WeaponParts] = ("무기 부품", "Weapon parts"),
        [TranscendRules.GunpowderEagle] = ("화약 \"Eagle\"", "Gunpowder \"Eagle\""),
        [TranscendRules.AramidFabric] = ("아라미드 섬유", "Aramid fiber fabric"),
        [TranscendRules.RipstopFabric] = ("립스탑", "Ripstop fabric"),
        [TranscendRules.MilitaryBoard] = ("군용 회로 기판", "Military circuit board"),
        [TranscendRules.Virtex] = ("Virtex 프로세서", "Virtex processor"),
        [TranscendRules.GraphicsCard] = ("그래픽 카드", "Graphics card"),
        [TranscendRules.Ledx] = ("LEDX", "LEDX")
    };
    internal static string MaterialName(string template) =>
        template != null && Names.TryGetValue(template, out var name) ? Loc.L(name.Korean, name.English) : template ?? "";
    internal static string Missing(EnhanceQuote view)
    {
        if (view?.Materials == null) return "";
        var parts = new List<string>();
        foreach (var material in view.Materials)
            if (material != null && material.Owned < material.Count)
                parts.Add(MaterialName(material.Template) + Loc.F($" {material.Count - material.Owned:N0}개", $" ×{material.Count - material.Owned:N0}"));
        return string.Join(", ", parts);
    }
}

// 1.4.0 mythic window and stash text without Unity types (testable).
internal static class MythicText
{
    // Body and head zone names (MythicRules zones, BodyPartColliderType).
    internal static string ZoneName(string zone) => zone switch
    {
        "NeckFront" => Loc.L("목 앞", "Front of neck"),
        "NeckBack" => Loc.L("목 뒤", "Back of neck"),
        "LeftUpperArm" => Loc.L("위팔 (왼쪽)", "Left upper arm"),
        "RightUpperArm" => Loc.L("위팔 (오른쪽)", "Right upper arm"),
        "LeftForearm" => Loc.L("아래팔 (왼쪽)", "Left forearm"),
        "RightForearm" => Loc.L("아래팔 (오른쪽)", "Right forearm"),
        "Pelvis" => Loc.L("사타구니", "Groin"),
        "PelvisBack" => Loc.L("엉덩이", "Buttocks"),
        "LeftThigh" => Loc.L("허벅지 (왼쪽)", "Left thigh"),
        "RightThigh" => Loc.L("허벅지 (오른쪽)", "Right thigh"),
        "ParietalHead" => Loc.L("정수리", "Top of head"),
        "BackHead" => Loc.L("뒤통수", "Back of head"),
        "Ears" => Loc.L("귀", "Ears"),
        "Eyes" => Loc.L("눈", "Eyes"),
        "Jaw" => Loc.L("턱", "Jaw"),
        _ => zone ?? ""
    };

    // A collider of the head (helmets, masks, visors): the mythic damage reduction is half there.
    internal static bool HeadZone(string zone) => zone == "HeadCommon" || Array.IndexOf(MythicRules.HeadZones, zone) >= 0;
}
