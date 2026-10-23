namespace GearEnhance;

internal static class ResultText
{
    internal static string Title(EnhanceReceipt receipt, bool complete) => receipt.Success
        ? receipt.IsEvolution ? receipt.StarLevel >= 3 ? Loc.L("3차 스타 강화 진화 성공!", "3rd star evolved!") : Loc.L("스타 강화 진화 성공!", "Star evolved!")
            : Loc.L("강화 성공!", "Enhanced!")
        : receipt.FailureMode == FailureMode.Destroy ? Loc.L("장비 파괴", "Gear destroyed")
        : receipt.FailureMode == FailureMode.Downgrade ? Loc.L("강화 단계 하락", "Level dropped") : Loc.L("강화 실패", "Enhance failed");
    internal static string Detail(EnhanceReceipt receipt) => receipt.Success
        ? receipt.IsEvolution ? StarEvolution.Stars(StarEvolution.Level(true, receipt.StarLevel))
                + Loc.L(" 진화 완료 · 모든 강화 효과에 추가 보너스 적용", " evolved · bonus applied to every effect")
            : EnhanceEffects.Name(receipt.Path) + " +" + receipt.Level + Loc.L(" 달성", " reached")
        : receipt.DestructionPrevented ? Loc.L("파괴 방지 발동 · 장비와 모든 강화 단계를 지켰습니다.", "Destruction protection triggered · gear and all levels kept.")
        : receipt.DowngradePrevented ? Loc.L("단계 하락 방지 발동 · 모든 강화 단계를 지켰습니다.", "Downgrade protection triggered · all levels kept.")
        : receipt.FailureMode == FailureMode.Destroy ? Loc.L("장비 본체·부착물·탄창·탄약이 파괴되었습니다.", "The gear, attachments, magazines and ammo were destroyed.")
        : receipt.FailureMode == FailureMode.Downgrade ? Loc.L(EnhanceEffects.Name(receipt.Path) + " +" + receipt.Level + "로 하락 · 다른 항목은 유지됩니다.",
            EnhanceEffects.Name(receipt.Path) + " dropped to +" + receipt.Level + " · other tracks kept.")
        : receipt.FailureMode == FailureMode.ZeroDurability ? Loc.L("현재 내구도 0 · 장비와 강화 단계는 유지됩니다.", "Current durability 0 · gear and levels kept.")
        : Loc.L("비용만 소모 · 장비와 강화 단계는 유지됩니다.", "Fee only · gear and levels kept.");
}
