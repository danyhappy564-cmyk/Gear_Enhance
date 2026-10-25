using BepInEx.Configuration;

namespace GearEnhance;

// 1.4.0 balance in F12 (developer options, password locked): chance materials, instant
// enhancement, evolution destruction tickets, insurance return delays, ??? and the mythic track.
// The values go to the server with every quote.
public sealed partial class Plugin
{
    private const string BoostSection = "14. 성공률 재료";
    private const string InstantSection = "15. 즉시 강화 · 파괴 방지권";
    private const string MythicSection = "16. ??? · 신화";
    private DevSetting<float> dogtagPoints, dogtagCap, cardPoints, cardCap, bitcoinPoints, bitcoinCap;
    private DevSetting<int> dogtagPrice, cardPrice, bitcoinPrice, boostKinds;
    private DevSetting<int> instantFee, secondTickets, secondTicketPrice, thirdTickets, thirdTicketPrice;
    private DevSetting<float> basicHours, advancedHours, premiumHours;
    private DevSetting<float> mysteryChance, mythicPity, mythicPityCap, mythicGrowth, mythicProtection, mythicWeapon, mythicArmor, rerollGrowth;
    private DevSetting<int> mysteryCost, mysteryLedx, mythicCost, mythicLedx, mythicZones, rerollCost, rerollCards;
    private DevSetting<string> mythicChances;
    private DevSetting<bool> mythicMaterials;
    private bool mythicChancesReported;
    private ConfigEntry<bool> reducedMotion, mythicShimmer, destructionPrompt;

    private void BindMythic()
    {
        reducedMotion = BindGeneral("7. 피드백", "연출 줄이기", false, "켜면 상태창 떨어짐·막대 채움·창 흔들림·신화 섬광·빛 고리·불꽃을 끄고 바로 보여 줍니다.");
        mythicShimmer = BindGeneral("7. 피드백", "창고 신화 칸 반짝임", true, "창고에서 신화 장비 칸 테두리가 분홍↔금빛으로 반짝입니다. 끄면 고정된 분홍 테두리만 보입니다.");
        destructionPrompt = BindGeneral("7. 피드백", "파괴 확인 창", true, "파괴될 수 있는 시도(파괴 방지 없는 +6 이상, 스타 진화) 전에 한 번 더 묻습니다. ??? 는 이 설정과 관계없이 항상 묻습니다.");
        const string hoursNote = "레이드에서 잃은 장비가 강화 보험으로 돌아올 때 우편이 오기까지의 실제 시간입니다. 0이면 즉시. 가입할 때 값으로 고정됩니다.";
        basicHours = BindDeveloper("12. 강화 보험", "일반 보험 돌아오는 시간 (시간)", 24f, new ConfigDescription(hoursNote, new AcceptableValueRange<float>(0, 720)));
        advancedHours = BindDeveloper("12. 강화 보험", "고급 보험 돌아오는 시간 (시간)", 12f, new ConfigDescription(hoursNote, new AcceptableValueRange<float>(0, 720)));
        premiumHours = BindDeveloper("12. 강화 보험", "프리미엄 보험 돌아오는 시간 (시간)", 0f, new ConfigDescription(hoursNote, new AcceptableValueRange<float>(0, 720)));

        const string pointsNote = "재료 1개가 더하는 성공률입니다. 2종까지 고를 수 있고 성공률은 100%를 넘지 않습니다.";
        const string capNote = "이 재료로 올릴 수 있는 최대 성공률입니다. 최대 개수 = 최대 ÷ 개당.";
        const string priceNote = "재료 1개마다 함께 내는 루블입니다. 재료와 루블은 성공·실패와 관계없이 소모됩니다.";
        dogtagPoints = BindDeveloper(BoostSection, "독택 개당 성공률 (%p)", 0.1f, new ConfigDescription(pointsNote, new AcceptableValueRange<float>(0, 100)));
        dogtagCap = BindDeveloper(BoostSection, "독택 최대 (%p)", 5f, new ConfigDescription(capNote, new AcceptableValueRange<float>(0, 100)));
        dogtagPrice = BindDeveloper(BoostSection, "독택 개당 비용 (루블)", 5000, new ConfigDescription(priceNote, new AcceptableValueRange<int>(0, 100000000)));
        cardPoints = BindDeveloper(BoostSection, "그래픽 카드 개당 성공률 (%p)", 1f, new ConfigDescription(pointsNote, new AcceptableValueRange<float>(0, 100)));
        cardCap = BindDeveloper(BoostSection, "그래픽 카드 최대 (%p)", 10f, new ConfigDescription(capNote, new AcceptableValueRange<float>(0, 100)));
        cardPrice = BindDeveloper(BoostSection, "그래픽 카드 개당 비용 (루블)", 100000, new ConfigDescription(priceNote, new AcceptableValueRange<int>(0, 100000000)));
        bitcoinPoints = BindDeveloper(BoostSection, "비트코인 개당 성공률 (%p)", 5f, new ConfigDescription(pointsNote, new AcceptableValueRange<float>(0, 100)));
        bitcoinCap = BindDeveloper(BoostSection, "비트코인 최대 (%p)", 20f, new ConfigDescription(capNote, new AcceptableValueRange<float>(0, 100)));
        bitcoinPrice = BindDeveloper(BoostSection, "비트코인 개당 비용 (루블)", 300000, new ConfigDescription(priceNote, new AcceptableValueRange<int>(0, 100000000)));
        boostKinds = BindDeveloper(BoostSection, "한 번에 쓸 재료 종류 (최대)", 2, new ConfigDescription("한 시도에 고를 수 있는 재료 종류 수입니다. ??? 에는 재료를 쓸 수 없습니다.", new AcceptableValueRange<int>(0, 3)));

        instantFee = BindDeveloper(InstantSection, "즉시 강화 비용 (루블)", 50000, new ConfigDescription(
            "충전 게이지 없이 바로 결과를 봅니다. 일반 강화·초월·신화에만 있고 스타 진화·??? 에는 없습니다.", new AcceptableValueRange<int>(0, 100000000)));
        const string ticketNote = "장비 하나가 평생 살 수 있는 장수입니다. 다 사면 더 살 수 없습니다.";
        const string ticketPriceNote = "방지권 1장 가격입니다. 쓰면 진화 시도마다 1장 소모, 실패해도 장비가 남습니다.";
        secondTickets = BindDeveloper(InstantSection, "★★ 진화 방지권 최대 (장)", 3, new ConfigDescription(ticketNote, new AcceptableValueRange<int>(0, 10)));
        secondTicketPrice = BindDeveloper(InstantSection, "★★ 진화 방지권 가격 (루블)", 3000000, new ConfigDescription(ticketPriceNote, new AcceptableValueRange<int>(0, 100000000)));
        thirdTickets = BindDeveloper(InstantSection, "★★★ 진화 방지권 최대 (장)", 2, new ConfigDescription(ticketNote, new AcceptableValueRange<int>(0, 10)));
        thirdTicketPrice = BindDeveloper(InstantSection, "★★★ 진화 방지권 가격 (루블)", 8000000, new ConfigDescription(ticketPriceNote, new AcceptableValueRange<int>(0, 100000000)));

        mysteryChance = BindDeveloper(MythicSection, "??? 성공 확률 (%)", 50f, new ConfigDescription(
            "★★★ 장비만 도전합니다. 실패하면 장비·부착물·기록이 파괴됩니다. 재료·방지권·즉시 강화는 쓸 수 없습니다.", new AcceptableValueRange<float>(0, 100)));
        mysteryCost = BindDeveloper(MythicSection, "??? 비용 (루블)", 8000000, new ConfigDescription("??? 한 번 도전 비용입니다.", new AcceptableValueRange<int>(0, 100000000)));
        mysteryLedx = BindDeveloper(MythicSection, "??? LEDX (개)", 1, new ConfigDescription("??? 한 번에 필요한 LEDX 수입니다.", new AcceptableValueRange<int>(0, 10)));
        mythicChances = BindDeveloper(MythicSection, "신화 성공률 (→1~→5, %)", "40, 30, 20, 15, 10", "다섯 숫자를 쉼표로 구분합니다. 형식이 틀리면 기본값을 씁니다.");
        mythicPity = BindDeveloper(MythicSection, "신화 천장 (실패당 %p)", 3f, new ConfigDescription(
            "실패할 때마다 그 장비의 다음 신화 성공률에 더합니다. 성공하면 0으로 돌아갑니다.", new AcceptableValueRange<float>(0, 100)));
        mythicPityCap = BindDeveloper(MythicSection, "신화 천장 최대 (%p)", 30f, new ConfigDescription("쌓이는 천장의 최대치입니다.", new AcceptableValueRange<float>(0, 100)));
        mythicCost = BindDeveloper(MythicSection, "신화 기본 비용 (루블)", 5000000, new ConfigDescription("→1 시도 비용입니다. 단계마다 아래 배수를 곱합니다.", new AcceptableValueRange<int>(0, 100000000)));
        mythicGrowth = BindDeveloper(MythicSection, "신화 단계별 비용 배수", 1.1f, new ConfigDescription("기본 1.1: 500만 → 550만 → 605만 → 665.5만 → 732.05만.", new AcceptableValueRange<float>(1, 3)));
        mythicLedx = BindDeveloper(MythicSection, "신화 LEDX (개)", 1, new ConfigDescription("신화 강화 한 번에 필요한 LEDX 수입니다.", new AcceptableValueRange<int>(0, 10)));
        mythicMaterials = BindDeveloper(MythicSection, "신화 재료 요구", true, "끄면 ???·신화·다시 뽑기에 LEDX·그래픽 카드 없이 루블만 냅니다.");
        mythicProtection = BindDeveloper(MythicSection, "신화 하락 방지 비용 (기본 비용 대비 %)", 100f, new ConfigDescription(
            "→4·→5 시도에만 씁니다. 실패하면 1단계 하락하며, 방지하면 이 비율만큼 더 냅니다.", new AcceptableValueRange<float>(0, 1000)));
        mythicWeapon = BindDeveloper(MythicSection, "신화 무기 탄 데미지·관통 / 단계 (%)", 2f, new ConfigDescription(
            "그 총으로 쏜 탄에만 붙습니다. 스타 배율을 받지 않습니다.", new AcceptableValueRange<float>(0, 10)));
        mythicArmor = BindDeveloper(MythicSection, "신화 방어구 받는 피해 감소 / 단계 (%)", 2f, new ConfigDescription(
            "신화 방어구·헬멧을 입은 플레이어가 받는 피해를 줄입니다.", new AcceptableValueRange<float>(0, 10)));
        mythicZones = BindDeveloper(MythicSection, "신화 방어구 추가 부위 (곳)", 2, new ConfigDescription(
            "??? 성공 때 아직 못 막는 부위 중 무작위로 더 막는 곳의 수입니다.", new AcceptableValueRange<int>(0, 5)));
        rerollCost = BindDeveloper(MythicSection, "다시 뽑기 기본 비용 (루블)", 2000000, new ConfigDescription(
            "추가 부위 한 곳을 다시 뽑는 첫 비용입니다. 그 장비에서 뽑을 때마다 아래 배수를 곱합니다.", new AcceptableValueRange<int>(0, 100000000)));
        rerollGrowth = BindDeveloper(MythicSection, "다시 뽑기 비용 배수", 1.5f, new ConfigDescription("기본 1.5: 200만 → 300만 → 450만 → 675만.", new AcceptableValueRange<float>(1, 3)));
        rerollCards = BindDeveloper(MythicSection, "다시 뽑기 그래픽 카드 (개)", 1, new ConfigDescription("다시 뽑기 한 번에 필요한 그래픽 카드 수입니다.", new AcceptableValueRange<int>(0, 10)));
    }

    private BoostTuning BoostTuning() => new BoostTuning
    {
        // F12 floats: 0.1f is 0.100000001…; rounding keeps 50 dogtags at exactly 5%p.
        DogtagPoints = Round(dogtagPoints.Value), DogtagCap = Round(dogtagCap.Value), DogtagRoubles = dogtagPrice.Value,
        GraphicsCardPoints = Round(cardPoints.Value), GraphicsCardCap = Round(cardCap.Value), GraphicsCardRoubles = cardPrice.Value,
        BitcoinPoints = Round(bitcoinPoints.Value), BitcoinCap = Round(bitcoinCap.Value), BitcoinRoubles = bitcoinPrice.Value, MaxKinds = boostKinds.Value
    };

    private static double Round(float value) => System.Math.Round((double)value, 4);

    private MythicTuning MythicTuning()
    {
        var chances = TranscendEffects.ParseChances(mythicChances.Value);
        if (chances == null && !mythicChancesReported)
        {
            mythicChancesReported = true;
            Logger.LogWarning("Gear Enhance: F12 신화 성공률 형식이 올바르지 않아 기본값을 씁니다(예: 40, 30, 20, 15, 10).");
        }
        return new MythicTuning
        {
            MysteryChance = mysteryChance.Value, MysteryCost = mysteryCost.Value, MysteryLedx = mysteryLedx.Value,
            Success = chances ?? new MythicTuning().Success, PityStep = mythicPity.Value, PityCap = mythicPityCap.Value,
            BaseCost = mythicCost.Value, CostGrowth = Round(mythicGrowth.Value), Ledx = mythicLedx.Value, RequireMaterials = mythicMaterials.Value,
            DowngradeProtectionPercent = mythicProtection.Value, WeaponPercentPerLevel = mythicWeapon.Value, ArmorPercentPerLevel = mythicArmor.Value,
            ExtraZones = mythicZones.Value, RerollCost = rerollCost.Value, RerollGrowth = Round(rerollGrowth.Value), RerollGraphicsCards = rerollCards.Value
        };
    }
}
