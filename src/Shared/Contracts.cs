#nullable enable
using System;

namespace GearEnhance;

public enum PriceBasis { Handbook, Flea }
public enum FailureMode { KeepItem, ZeroDurability, Destroy, Downgrade }
public enum EnhancePath { Recoil, Ergonomics, Precision, ArmorDurability, Mobility }
// Appended only: receipts store the number. 1.4.0 adds ???, the mythic track, evolution
// destruction tickets and the armor mythic zone reroll.
public enum EnhanceAction { Upgrade, Evolve, EvolveThird, Insure, RegisterInserts, Transcend, Mystery, Mythic, BuyTicket, Reroll }
// Enhancement insurance (1.2.0): bought in the enhancement window, separate from traders.
public enum InsuranceTier { None, Basic, Advanced, Premium }

public static class EnhanceProtocol
{
    public const int Version = 11;
    public const string ModVersion = "1.4.0";
    public static void Validate(int version)
    {
        if (version != Version)
            throw Loc.Argument($"Gear Enhance 클라이언트와 서버를 모두 {ModVersion}으로 업데이트해 주세요.",
                $"Update both the Gear Enhance client and server to {ModVersion}.");
    }
}

public sealed class EnhanceSettings
{
    public double SuccessPercent { get; set; } = 75;
    public double SuccessLossPerLevel { get; set; } = 5;
    public bool AlwaysSucceed { get; set; }
    public int MaxLevel { get; set; } = 10;
    public PriceBasis PriceBasis { get; set; } = PriceBasis.Handbook;
    public double CostPercent { get; set; } = 10;
    public double CostPerLevel { get; set; } = 1.25;
    public int MinimumCost { get; set; } = 1000;
    public FailureMode FailureMode { get; set; } = FailureMode.KeepItem;
    public bool HighRiskEnabled { get; set; } = true;
    public double HighRiskDestructionPercent { get; set; } = 25;
    // 1.4.0: 1/5 of 1.3.0 (50 / 35); the formula (base × % × level²) is unchanged.
    public double DestructionProtectionPercent { get; set; } = 10;
    public double DowngradeProtectionPercent { get; set; } = 7;
    public double EvolutionSuccessPercent { get; set; } = 30;
    public double ThirdStarSuccessPercent { get; set; } = 10;
    public double EvolutionCostPercent { get; set; } = 300;
    // Pricier plates pay the difference for the average attempts of every held level; this scales it.
    public double InsertSurchargePercent { get; set; } = 100;
    // Chance that lost gear comes back enhanced, and fee as % of its enhancement value.
    public double BasicInsuranceReturnPercent { get; set; } = 20;
    public double AdvancedInsuranceReturnPercent { get; set; } = 55;
    public double PremiumInsuranceReturnPercent { get; set; } = 99;
    public double BasicInsuranceFeePercent { get; set; } = 5;
    public double AdvancedInsuranceFeePercent { get; set; } = 15;
    public double PremiumInsuranceFeePercent { get; set; } = 30;
    // 1.4.0: a successful return is mailed this many real hours after the raid (0 = at once).
    public double BasicInsuranceReturnHours { get; set; } = 24;
    public double AdvancedInsuranceReturnHours { get; set; } = 12;
    public double PremiumInsuranceReturnHours { get; set; }
    // 1.4.0: skip the charge gauge for a flat fee (enhancement, transcendence, mythic).
    public int InstantFee { get; set; } = 50000;
    // 1.4.0 star evolution destruction tickets: lifetime purchases per gear and price of one.
    public int SecondTicketMax { get; set; } = 3;
    public int SecondTicketPrice { get; set; } = 1000000;
    public int ThirdTicketMax { get; set; } = 2;
    public int ThirdTicketPrice { get; set; } = 2700000;
    // ??? destruction ticket (TicketStars = MythicRules.MysteryTicket): one per gear, ever.
    public int MysteryTicketMax { get; set; } = 1;
    public int MysteryTicketPrice { get; set; } = 5000000;
    // 1.4.0 developer sandbox: true while the F12 developer options are unlocked; LaunchId is
    // new for every game launch. Not balance: the server copies the records while it is on.
    public bool Developer { get; set; }
    public string LaunchId { get; set; } = "";
    // The F12 save password matches (user 2026-10-26): nothing is copied, what is enhanced stays.
    public bool DeveloperSave { get; set; }
    // 1.4.0 F12 quick switches (developer only, user request): no roubles / no materials for anything.
    public bool FreeRoubles { get; set; }
    public bool FreeMaterials { get; set; }
    public EffectTuning Effects { get; set; } = new EffectTuning();
    public TranscendTuning Transcend { get; set; } = new TranscendTuning();
    public BoostTuning Boost { get; set; } = new BoostTuning();
    public MythicTuning Mythic { get; set; } = new MythicTuning();

    public double ReturnPercent(InsuranceTier tier) => tier == InsuranceTier.Premium ? PremiumInsuranceReturnPercent
        : tier == InsuranceTier.Advanced ? AdvancedInsuranceReturnPercent : tier == InsuranceTier.Basic ? BasicInsuranceReturnPercent : 0;
    public double FeePercent(InsuranceTier tier) => tier == InsuranceTier.Premium ? PremiumInsuranceFeePercent
        : tier == InsuranceTier.Advanced ? AdvancedInsuranceFeePercent : tier == InsuranceTier.Basic ? BasicInsuranceFeePercent : 0;
    public double ReturnHours(InsuranceTier tier) => tier == InsuranceTier.Premium ? PremiumInsuranceReturnHours
        : tier == InsuranceTier.Advanced ? AdvancedInsuranceReturnHours : tier == InsuranceTier.Basic ? BasicInsuranceReturnHours : 0;
    public int TicketMax(int stars) => stars == MythicRules.MysteryTicket ? MysteryTicketMax : stars == 3 ? ThirdTicketMax : stars == 2 ? SecondTicketMax : 0;
    public int TicketPrice(int stars) => stars == MythicRules.MysteryTicket ? MysteryTicketPrice : stars == 3 ? ThirdTicketPrice : stars == 2 ? SecondTicketPrice : 0;

    public void Validate()
    {
        if (!Finite(SuccessPercent) || SuccessPercent < 0 || SuccessPercent > 100
            || !Finite(SuccessLossPerLevel) || SuccessLossPerLevel < 0 || SuccessLossPerLevel > 100
            || !Finite(HighRiskDestructionPercent) || HighRiskDestructionPercent < 0 || HighRiskDestructionPercent > 100
            || !Finite(DestructionProtectionPercent) || DestructionProtectionPercent < 0 || DestructionProtectionPercent > 1000
            || !Finite(DowngradeProtectionPercent) || DowngradeProtectionPercent < 0 || DowngradeProtectionPercent > 1000
            || !Finite(EvolutionSuccessPercent) || EvolutionSuccessPercent < 0 || EvolutionSuccessPercent > 100
            || !Finite(ThirdStarSuccessPercent) || ThirdStarSuccessPercent < 0 || ThirdStarSuccessPercent > 100
            || !Finite(EvolutionCostPercent) || EvolutionCostPercent < 0 || EvolutionCostPercent > 10000
            || !Finite(InsertSurchargePercent) || InsertSurchargePercent < 0 || InsertSurchargePercent > 1000
            || !Percent(BasicInsuranceReturnPercent) || !Percent(AdvancedInsuranceReturnPercent) || !Percent(PremiumInsuranceReturnPercent)
            || !Finite(BasicInsuranceFeePercent) || BasicInsuranceFeePercent < 0 || BasicInsuranceFeePercent > 1000
            || !Finite(AdvancedInsuranceFeePercent) || AdvancedInsuranceFeePercent < 0 || AdvancedInsuranceFeePercent > 1000
            || !Finite(PremiumInsuranceFeePercent) || PremiumInsuranceFeePercent < 0 || PremiumInsuranceFeePercent > 1000
            || !Hours(BasicInsuranceReturnHours) || !Hours(AdvancedInsuranceReturnHours) || !Hours(PremiumInsuranceReturnHours)
            || InstantFee < 0 || InstantFee > 100000000
            || SecondTicketMax < 0 || SecondTicketMax > 10 || ThirdTicketMax < 0 || ThirdTicketMax > 10
            || SecondTicketPrice < 0 || SecondTicketPrice > 100000000 || ThirdTicketPrice < 0 || ThirdTicketPrice > 100000000
            || MysteryTicketMax < 0 || MysteryTicketMax > 10 || MysteryTicketPrice < 0 || MysteryTicketPrice > 100000000
            || (LaunchId?.Length ?? 0) > 64
            || MaxLevel < 1 || MaxLevel > 100
            || !Finite(CostPercent) || CostPercent < 0 || CostPercent > 1000
            || !Finite(CostPerLevel) || CostPerLevel < 1 || CostPerLevel > 3
            || MinimumCost < 0 || MinimumCost > 100000000
            || !Enum.IsDefined(typeof(PriceBasis), PriceBasis)
            || !Enum.IsDefined(typeof(FailureMode), FailureMode))
            throw Loc.Argument("F12 설정 범위를 확인해 주세요.", "Check the F12 setting ranges.");
        if (Effects == null) throw Loc.Argument("강화 효과 설정이 없습니다.", "Enhancement effect settings are missing.");
        Effects.MaxLevel = MaxLevel;
        Effects.Validate();
        if (Transcend == null) throw Loc.Argument("초월 강화 설정이 없습니다.", "Transcendence settings are missing.");
        Transcend.Validate();
        if (Boost == null) throw Loc.Argument("성공률 재료 설정이 없습니다.", "Chance material settings are missing.");
        Boost.Validate();
        if (Mythic == null) throw Loc.Argument("신화 설정이 없습니다.", "Mythic settings are missing.");
        Mythic.Validate();
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool Percent(double value) => Finite(value) && value >= 0 && value <= 100;
    private static bool Hours(double value) => Finite(value) && value >= 0 && value <= 720;
}

public sealed class EnhanceProtection
{
    public bool Destruction { get; set; }
    public bool Downgrade { get; set; }
}

public sealed class EnhanceQuote
{
    public string QuoteId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public int CurrentLevel { get; set; }
    public int NextLevel { get; set; }
    public int Cost { get; set; }
    public int BaseCost { get; set; }
    public long DestructionProtectionFee { get; set; }
    public long DowngradeProtectionFee { get; set; }
    public int ProtectionCost => Cost - BaseCost - BoostCost - InsertSurcharge - InstantFee;
    // 1.4.0 chance materials (at most two kinds) and their rouble price. BoostOwned is the
    // usable stock of every kind (indexed by BoostMaterial), chosen or not, for the window.
    public BoostLine[] Boost { get; set; } = Array.Empty<BoostLine>();
    public long[] BoostOwned { get; set; } = new long[3];
    public int BoostCost { get; set; }
    public double BaseSuccessPercent { get; set; }
    public double BoostPoints => SuccessPercent - BaseSuccessPercent;
    // ??? is a fixed 50/50; insurance, plates, tickets and rerolls are not rolled.
    public bool BoostAvailable => !BindLegacy && BaseSuccessPercent < 100 && (Action == EnhanceAction.Upgrade
        || Action == EnhanceAction.Evolve || Action == EnhanceAction.EvolveThird || Action == EnhanceAction.Transcend || Action == EnhanceAction.Mythic);
    // The charge gauge can be skipped for a fee, except for star evolution and ???.
    public bool InstantAvailable => !BindLegacy && (Action == EnhanceAction.Upgrade || Action == EnhanceAction.Transcend || Action == EnhanceAction.Mythic);
    public bool Instant { get; set; }
    public int InstantFee { get; set; }
    public bool CanPay => AvailableRoubles >= Cost
        && (Materials == null || Array.TrueForAll(Materials, m => m != null && m.Owned >= m.Count))
        && (Boost == null || Array.TrueForAll(Boost, b => b != null && b.Owned >= b.Count));
    // The charge gauge runs before submission; the server must still accept the quote then.
    public bool ExpiresWithin(long nowUnixSeconds, double seconds) => ExpiresUnixSeconds - nowUnixSeconds < seconds;
    public EnhanceProtection Protection { get; set; } = new EnhanceProtection();
    public bool IsEvolution { get; set; }
    public int CurrentStars { get; set; } = 1;
    public int NextStars { get; set; } = 1;
    public long AvailableRoubles { get; set; }
    public double SuccessPercent { get; set; }
    public string PriceSource { get; set; } = "";
    public FailureMode FailureMode { get; set; }
    public bool HighRisk { get; set; }
    // Conditional chance after a failed attempt. FailureMode is the other outcome.
    public double DestructionPercent { get; set; }
    public bool DestructionProtectionAvailable => Action == EnhanceAction.Upgrade && NextLevel >= 6 && SuccessPercent < 100
        && (FailureMode == FailureMode.Destroy || DestructionPercent > 0);
    public bool DowngradeProtectionAvailable => SuccessPercent < 100 && FailureMode == FailureMode.Downgrade && DestructionPercent < 100
        && (Action == EnhanceAction.Upgrade && NextLevel >= 6
            || Action == EnhanceAction.Transcend && NextLevel >= TranscendRules.DropFrom && NextLevel <= TranscendRules.ProtectUpTo
            || Action == EnhanceAction.Mythic && NextLevel >= MythicRules.DropFrom && NextLevel <= MythicRules.ProtectUpTo);
    public bool CanDestroy => SuccessPercent < 100 && (FailureMode == FailureMode.Destroy || DestructionPercent > 0) && !Protection.Destruction;
    public int AttachedItems { get; set; }
    public long ExpiresUnixSeconds { get; set; }
    public EnhancePath Path { get; set; }
    public bool IsWeapon { get; set; }
    public bool BindLegacy { get; set; }
    public double CurrentEffect { get; set; }
    public double NextEffect { get; set; }
    public double EffectCap { get; set; }
    public bool Complete { get; set; }
    public bool CompletesOnSuccess { get; set; }
    public double NextGainMultiplier { get; set; }
    public EnhanceAction Action { get; set; }
    // Armor: value of the parts in its slots (plates, soft inserts, visors), already in the price.
    public double PartsPrice { get; set; }
    public int InsertedParts { get; set; }
    public int UnregisteredParts { get; set; }
    // Catch-up for parts worth more than the ones paid for at earlier levels.
    public int InsertSurcharge { get; set; }
    // Shown with the insurance quote; charged by a separate registration.
    public int RegistrationFee { get; set; }
    public bool CanRegister { get; set; }
    public bool CanBuyInsurance { get; set; }
    public InsuranceTier InsuranceTier { get; set; }
    public InsuranceTier CurrentInsurance { get; set; }
    public double CurrentInsurancePercent { get; set; }
    public long InsuredValue { get; set; }
    // Indexed by InsuranceTier; [0] is unused.
    public int[] InsuranceFees { get; set; } = new int[4];
    public double[] InsuranceChances { get; set; } = new double[4];
    // 1.3.0 transcendence. CurrentLevel/NextLevel are the chosen track's levels.
    public TranscendTrack Transcend { get; set; }
    public TranscendKind TranscendKind { get; set; }
    public int TranscendSecond { get; set; }
    public int TranscendThird { get; set; }
    // Already in BaseSuccessPercent; a failure adds PityStep, up to PityCap.
    public double PityPercent { get; set; }
    public double PityStep { get; set; }
    public double PityCap { get; set; }
    public bool Automatic { get; set; }
    // Overheat for Reliability, the flat rounds-per-minute limit for FireRate.
    public double CurrentSecondary { get; set; }
    public double NextSecondary { get; set; }
    public int CurrentClassBonus { get; set; }
    public int NextClassBonus { get; set; }
    public TranscendMaterial[] Materials { get; set; } = Array.Empty<TranscendMaterial>();
    // Armor items whose max durability the next registration brings back in line.
    public int DurabilityPending { get; set; }
    // Indexed by InsuranceTier: real hours until a successful return is mailed.
    public double[] InsuranceHours { get; set; } = new double[4];
    // 1.4.0 destruction tickets of the evolution to TicketStars (2 or 3), or of ??? (4), on every quote of the gear.
    public int TicketStars { get; set; }
    public int TicketsHeld { get; set; }
    public int TicketsBought { get; set; }
    public int TicketMax { get; set; }
    public int TicketPrice { get; set; }
    public bool UseTicket { get; set; }
    // 1.4.0 mythic state of the gear (-1 = not mythic) and, for armor, its added zones.
    public int MythicLevel { get; set; } = -1;
    public string[] Zones { get; set; } = Array.Empty<string>();
    // ???: zones the success may add. Reroll: zones the chosen slot may become.
    public string[] ZoneOptions { get; set; } = Array.Empty<string>();
    public int Rerolls { get; set; }
    public int Slot { get; set; }
    // F12 quick switch: required and chance materials are not taken (the server skips them).
    public bool FreeMaterials { get; set; }
}

public sealed class EnhanceReceipt
{
    public string QuoteId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public bool Success { get; set; }
    public int Level { get; set; }
    public int Paid { get; set; }
    public int BitcoinsPaid { get; set; }
    public FailureMode FailureMode { get; set; }
    public bool DestructionPrevented { get; set; }
    public bool DowngradePrevented { get; set; }
    public bool IsEvolution { get; set; }
    public bool Evolved { get; set; }
    public int StarLevel { get; set; }
    public long CompletedUnixSeconds { get; set; }
    public EnhancePath Path { get; set; }
    // Upgrade for receipts written before 1.2.0.
    public EnhanceAction Action { get; set; }
    public InsuranceTier InsuranceTier { get; set; }
    public TranscendTrack Transcend { get; set; }
    // Pity of that track after the attempt (0 after a success).
    public double Pity { get; set; }
    // 1.4.0
    public bool Instant { get; set; }
    public BoostLine[] Boost { get; set; } = Array.Empty<BoostLine>();
    public bool TicketUsed { get; set; }
    public int TicketsLeft { get; set; }
    // Tickets of the finished evolution that vanished with its success (no refund).
    public int TicketsVoided { get; set; }
    public int MythicLevel { get; set; } = -1;
    public string[] Zones { get; set; } = Array.Empty<string>();
    public string OldZone { get; set; } = "";
    public string NewZone { get; set; } = "";
}

public sealed class EnhanceRecord
{
    public string ItemId { get; set; } = "";
    public int Level { get; set; }
    public EnhancePath Path { get; set; }
    public bool Evolved { get; set; }
    public int StarLevel { get; set; }
}

public sealed class EnhanceSnapshot
{
    public string ProfileId { get; set; } = "";
    public EnhanceRecord[] Items { get; set; } = Array.Empty<EnhanceRecord>();
    // Parts registered to each enhanced armor; only these share its effect while inserted.
    public InsertLink[] Inserts { get; set; } = Array.Empty<InsertLink>();
    // 1.3.0: transcendence levels per gear, and max durability added per armor item.
    public TranscendLink[] Transcend { get; set; } = Array.Empty<TranscendLink>();
    public DurabilityLink[] Durability { get; set; } = Array.Empty<DurabilityLink>();
    // Enhancement insurance per gear, for the stash label under the badge.
    public CoverageLink[] Coverage { get; set; } = Array.Empty<CoverageLink>();
    // 1.4.0: mythic gear, and insured gear still waiting to be mailed back.
    public MythicLink[] Mythic { get; set; } = Array.Empty<MythicLink>();
    public ReturnLink[] Returns { get; set; } = Array.Empty<ReturnLink>();
    // 1.4.0: shown once (records restored after a hand edit, developer sandbox rolled back).
    public string Notice { get; set; } = "";
}

public sealed class MythicLink
{
    public string ItemId { get; set; } = "";
    public int Level { get; set; }
    public string[] Zones { get; set; } = Array.Empty<string>();
}

public sealed class ReturnLink
{
    public string ItemId { get; set; } = "";
    public string Template { get; set; } = "";
    public bool IsWeapon { get; set; }
    public int Stars { get; set; }
    public int MythicLevel { get; set; } = -1;
    public InsuranceTier Tier { get; set; }
    public long StartUnixSeconds { get; set; }
    public long DueUnixSeconds { get; set; }
}

public sealed class CoverageLink
{
    public string ItemId { get; set; } = "";
    public InsuranceTier Tier { get; set; }
    // The return chance fixed when it was bought.
    public double Percent { get; set; }
}

public sealed class TranscendLink
{
    public string ItemId { get; set; } = "";
    public int Second { get; set; }
    public int Third { get; set; }
}

// Sale prices treat the added durability as missing (no profit from the grant).
public sealed class DurabilityLink
{
    public string ItemId { get; set; } = "";
    public double Amount { get; set; }
}

public sealed class InsertLink
{
    public string ArmorId { get; set; } = "";
    public string[] PartIds { get; set; } = Array.Empty<string>();
}

public sealed class ApiReply<T> where T : class
{
    public bool Ok { get; set; }
    public string Error { get; set; } = "";
    // Added in 1.1.0. Older servers leave it empty and the Korean text is shown.
    public string ErrorEnglish { get; set; } = "";
    public T? Data { get; set; }
}
