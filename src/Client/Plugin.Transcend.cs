using BepInEx.Configuration;

namespace GearEnhance;

// 1.3.0 transcendence balance in F12 "13. 초월 강화" (developer options, password locked).
// The values go to the server with every quote and to the client effect hooks.
public sealed partial class Plugin
{
    private const string TranscendSection = "13. 초월 강화";
    private DevSetting<string> secondChances, thirdChances;
    private DevSetting<float> secondPity, thirdPity, pityCap, secondCost, thirdCost, transcendGrowth;
    private DevSetting<float> horizontalStep, durabilityStep, fireRateStep, fireRateFlat, malfunctionStep, overheatStep, bluntStep, bluntClass;
    private DevSetting<int> secondMinimum, thirdMinimum;
    private DevSetting<bool> requireMaterials;
    private bool chancesReported;

    private void BindTranscend()
    {
        secondChances = BindDeveloper(TranscendSection, "2성 초월 성공률 (→1~→5, %)", "60, 50, 40, 30, 20",
            "다섯 숫자를 쉼표로 구분합니다. 형식이 틀리면 기본값을 씁니다.");
        thirdChances = BindDeveloper(TranscendSection, "3성 초월 성공률 (→1~→5, %)", "40, 30, 20, 15, 10",
            "다섯 숫자를 쉼표로 구분합니다. 형식이 틀리면 기본값을 씁니다.");
        secondPity = BindDeveloper(TranscendSection, "2성 천장 (실패당 %p)", 5f, new ConfigDescription(
            "실패할 때마다 그 장비의 다음 2성 초월 성공률에 더합니다. 성공하면 0으로 돌아갑니다.", new AcceptableValueRange<float>(0, 100)));
        thirdPity = BindDeveloper(TranscendSection, "3성 천장 (실패당 %p)", 3f, new ConfigDescription(
            "실패할 때마다 그 장비의 다음 3성 초월 성공률에 더합니다. 성공하면 0으로 돌아갑니다.", new AcceptableValueRange<float>(0, 100)));
        pityCap = BindDeveloper(TranscendSection, "천장 최대 (%p)", 30f, new ConfigDescription("쌓이는 천장의 최대치입니다.", new AcceptableValueRange<float>(0, 100)));
        secondCost = BindDeveloper(TranscendSection, "2성 비용 (기준가 대비 %)", 50f, new ConfigDescription(
            "비용 = 최소 비용과 기준가 × 이 값/100 × 배수^현재단계 중 큰 값. 방어구는 끼운 부품 가격을 더합니다. 최대 1억 루블.", new AcceptableValueRange<float>(0, 10000)));
        thirdCost = BindDeveloper(TranscendSection, "3성 비용 (기준가 대비 %)", 100f, new ConfigDescription(
            "비용 = 최소 비용과 기준가 × 이 값/100 × 배수^현재단계 중 큰 값. 방어구는 끼운 부품 가격을 더합니다. 최대 1억 루블.", new AcceptableValueRange<float>(0, 10000)));
        secondMinimum = BindDeveloper(TranscendSection, "2성 최소 비용 (루블)", 1000000, new ConfigDescription("한 번 시도의 최소 비용입니다.", new AcceptableValueRange<int>(0, 100000000)));
        thirdMinimum = BindDeveloper(TranscendSection, "3성 최소 비용 (루블)", 3000000, new ConfigDescription("한 번 시도의 최소 비용입니다.", new AcceptableValueRange<int>(0, 100000000)));
        transcendGrowth = BindDeveloper(TranscendSection, "초월 단계별 비용 배수", 1.5f, new ConfigDescription("단계가 오를수록 비용에 곱하는 배수입니다.", new AcceptableValueRange<float>(1, 3)));
        requireMaterials = BindDeveloper(TranscendSection, "초월 재료 요구", true, "끄면 재료 없이 루블만 냅니다. 재료는 성공·실패와 관계없이 소모됩니다.");
        horizontalStep = BindDeveloper(TranscendSection, "수평 반동 감소 / 단계 (%)", 6f, new ConfigDescription(
            "총기 2성 초월. 뒤로 차는 힘과 좌우로 튀는 각도를 함께 줄입니다. 반동 제어 강화와 합쳐 최대 80%.", new AcceptableValueRange<float>(0, 16)));
        durabilityStep = BindDeveloper(TranscendSection, "최대 내구도 증가 / 단계 (%)", 6f, new ConfigDescription(
            "방어구 2성 초월. 원래 최대 내구도 기준. 바꾸면 다음 강화·초월·등록 때 다시 맞춰집니다.", new AcceptableValueRange<float>(0, 20)));
        fireRateStep = BindDeveloper(TranscendSection, "발사속도 증가 / 단계 (%)", 6f, new ConfigDescription(
            "총기 3성 초월(연사·점사 가능 총). 아래 상한과 비교해 작은 값을 더합니다.", new AcceptableValueRange<float>(0, 20)));
        fireRateFlat = BindDeveloper(TranscendSection, "발사속도 증가 상한 / 단계 (rpm)", 50f, new ConfigDescription(
            "단계당 더하는 발사속도의 최대치입니다.", new AcceptableValueRange<float>(0, 200)));
        malfunctionStep = BindDeveloper(TranscendSection, "고장 확률 감소 / 단계 (%)", 10f, new ConfigDescription(
            "총기 3성 초월(반자동·볼트액션 총).", new AcceptableValueRange<float>(0, 20)));
        overheatStep = BindDeveloper(TranscendSection, "과열 증가 감소 / 단계 (%)", 8f, new ConfigDescription(
            "총기 3성 초월(반자동·볼트액션 총). 한 발당 쌓이는 과열을 줄입니다.", new AcceptableValueRange<float>(0, 20)));
        bluntStep = BindDeveloper(TranscendSection, "둔탁상 감소 / 단계 (%)", 5f, new ConfigDescription(
            "방어구 3성 초월. 막았을 때 몸에 들어오는 피해를 줄입니다. 3단계 +1클, 5단계 +2클(최대 6클).", new AcceptableValueRange<float>(0, 20)));
        bluntClass = BindDeveloper(TranscendSection, "6클 대체 둔탁상 감소 / 클 (%)", 10f, new ConfigDescription(
            "이미 6클 이상이라 등급을 못 올리는 부품이 1클 대신 받는 둔탁상 감소입니다.", new AcceptableValueRange<float>(0, 50)));
    }

    private TranscendTuning TranscendTuning()
    {
        var defaults = new TranscendTuning();
        var second = TranscendEffects.ParseChances(secondChances.Value);
        var third = TranscendEffects.ParseChances(thirdChances.Value);
        if ((second == null || third == null) && !chancesReported)
        {
            chancesReported = true;
            Logger.LogWarning("Gear Enhance: F12 초월 성공률 형식이 올바르지 않아 기본값을 씁니다(예: 60, 50, 40, 30, 20).");
        }
        return new TranscendTuning
        {
            SecondSuccess = second ?? defaults.SecondSuccess, ThirdSuccess = third ?? defaults.ThirdSuccess,
            SecondPityStep = secondPity.Value, ThirdPityStep = thirdPity.Value, PityCap = pityCap.Value,
            SecondCostPercent = secondCost.Value, ThirdCostPercent = thirdCost.Value,
            SecondMinimumCost = secondMinimum.Value, ThirdMinimumCost = thirdMinimum.Value,
            CostGrowth = transcendGrowth.Value, RequireMaterials = requireMaterials.Value,
            HorizontalRecoilPerLevel = horizontalStep.Value, MaxDurabilityPerLevel = durabilityStep.Value,
            FireRatePercentPerLevel = fireRateStep.Value, FireRateFlatPerLevel = fireRateFlat.Value,
            MalfunctionPerLevel = malfunctionStep.Value, OverheatPerLevel = overheatStep.Value,
            BluntPerLevel = bluntStep.Value, BluntPerMissingClass = bluntClass.Value
        };
    }
}
