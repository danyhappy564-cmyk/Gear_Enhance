namespace GearEnhance;

internal static class ResultText
{
    internal static string Title(EnhanceReceipt receipt, bool complete) => receipt.Success
        ? receipt.IsEvolution ? receipt.StarLevel >= 3 ? "3차 스타 강화 진화 성공!" : "스타 강화 진화 성공!" : "강화 성공!"
        : receipt.FailureMode == FailureMode.Destroy ? "장비 파괴"
        : receipt.FailureMode == FailureMode.Downgrade ? "강화 단계 하락" : "강화 실패";
    internal static string Detail(EnhanceReceipt receipt) => receipt.Success
        ? receipt.IsEvolution ? StarEvolution.Stars(StarEvolution.Level(true, receipt.StarLevel)) + " 진화 완료 · 모든 강화 효과에 추가 보너스 적용"
            : EnhanceEffects.Name(receipt.Path) + " +" + receipt.Level + " 달성"
        : receipt.DestructionPrevented ? "파괴 방지 발동 · 장비와 모든 강화 단계를 지켰습니다."
        : receipt.DowngradePrevented ? "단계 하락 방지 발동 · 모든 강화 단계를 지켰습니다."
        : receipt.FailureMode == FailureMode.Destroy ? "장비 본체·부착물·탄창·탄약이 파괴되었습니다."
        : receipt.FailureMode == FailureMode.Downgrade ? EnhanceEffects.Name(receipt.Path) + " +" + receipt.Level + "로 하락 · 다른 항목은 유지됩니다."
        : receipt.FailureMode == FailureMode.ZeroDurability ? "현재 내구도 0 · 장비와 강화 단계는 유지됩니다."
        : "비용만 소모 · 장비와 강화 단계는 유지됩니다.";
}
