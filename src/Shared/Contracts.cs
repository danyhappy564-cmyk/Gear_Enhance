#nullable enable
using System;

namespace GearEnhance;

public enum PriceBasis { Handbook, Flea }
public enum FailureMode { KeepItem, ZeroDurability, Destroy, Downgrade }
public enum EnhancePath { Recoil, Ergonomics, Precision, ArmorDurability, Mobility }
public enum EnhanceAction { Upgrade, Evolve, EvolveThird, Insure, RegisterInserts }
// Enhancement insurance (1.2.0): bought in the enhancement window, separate from traders.
public enum InsuranceTier { None, Basic, Advanced, Premium }

public static class EnhanceProtocol
{
    public const int Version = 8;
    public const string ModVersion = "1.2.0";
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
    public double DestructionProtectionPercent { get; set; } = 50;
    public double DowngradeProtectionPercent { get; set; } = 35;
    public double EvolutionSuccessPercent { get; set; } = 30;
    public double ThirdStarSuccessPercent { get; set; } = 10;
    public double EvolutionCostPercent { get; set; } = 300;
    // Chance that lost gear comes back enhanced, and fee as % of its enhancement value.
    public double BasicInsuranceReturnPercent { get; set; } = 20;
    public double AdvancedInsuranceReturnPercent { get; set; } = 55;
    public double PremiumInsuranceReturnPercent { get; set; } = 90;
    public double BasicInsuranceFeePercent { get; set; } = 5;
    public double AdvancedInsuranceFeePercent { get; set; } = 15;
    public double PremiumInsuranceFeePercent { get; set; } = 30;
    public EffectTuning Effects { get; set; } = new EffectTuning();

    public double ReturnPercent(InsuranceTier tier) => tier == InsuranceTier.Premium ? PremiumInsuranceReturnPercent
        : tier == InsuranceTier.Advanced ? AdvancedInsuranceReturnPercent : tier == InsuranceTier.Basic ? BasicInsuranceReturnPercent : 0;
    public double FeePercent(InsuranceTier tier) => tier == InsuranceTier.Premium ? PremiumInsuranceFeePercent
        : tier == InsuranceTier.Advanced ? AdvancedInsuranceFeePercent : tier == InsuranceTier.Basic ? BasicInsuranceFeePercent : 0;

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
            || !Percent(BasicInsuranceReturnPercent) || !Percent(AdvancedInsuranceReturnPercent) || !Percent(PremiumInsuranceReturnPercent)
            || !Finite(BasicInsuranceFeePercent) || BasicInsuranceFeePercent < 0 || BasicInsuranceFeePercent > 1000
            || !Finite(AdvancedInsuranceFeePercent) || AdvancedInsuranceFeePercent < 0 || AdvancedInsuranceFeePercent > 1000
            || !Finite(PremiumInsuranceFeePercent) || PremiumInsuranceFeePercent < 0 || PremiumInsuranceFeePercent > 1000
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
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool Percent(double value) => Finite(value) && value >= 0 && value <= 100;
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
    public int ProtectionCost => Cost - BaseCost - BoostCost - InsertSurcharge;
    public bool ChanceBoost { get; set; }
    public int BoostCost { get; set; }
    public int BoostBitcoins { get; set; }
    public long AvailableBitcoins { get; set; }
    public double BaseSuccessPercent { get; set; }
    public double BoostPoints => SuccessPercent - BaseSuccessPercent;
    public bool ChanceBoostAvailable => !BindLegacy && BaseSuccessPercent < 100;
    public bool CanPay => AvailableRoubles >= Cost && AvailableBitcoins >= BoostBitcoins;
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
    public bool DestructionProtectionAvailable => !IsEvolution && NextLevel >= 6 && SuccessPercent < 100 && (FailureMode == FailureMode.Destroy || DestructionPercent > 0);
    public bool DowngradeProtectionAvailable => !IsEvolution && SuccessPercent < 100 && NextLevel >= 6
        && FailureMode == FailureMode.Downgrade && DestructionPercent < 100;
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
