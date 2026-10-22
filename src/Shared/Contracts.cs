#nullable enable
using System;

namespace GearEnhance;

public enum PriceBasis { Handbook, Flea }
public enum FailureMode { KeepItem, ZeroDurability, Destroy, Downgrade }
public enum EnhancePath { Recoil, Ergonomics, Precision, ArmorDurability, Mobility }
public enum EnhanceAction { Upgrade, Evolve, EvolveThird }

public static class EnhanceProtocol
{
    public const int Version = 7;
    public const string ModVersion = "0.7.1";
    public static void Validate(int version)
    {
        if (version != Version)
            throw new ArgumentException("Gear Enhance 클라이언트와 서버를 모두 0.7.1로 업데이트해 주세요.");
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
    public EffectTuning Effects { get; set; } = new EffectTuning();

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
            || MaxLevel < 1 || MaxLevel > 100
            || !Finite(CostPercent) || CostPercent < 0 || CostPercent > 1000
            || !Finite(CostPerLevel) || CostPerLevel < 1 || CostPerLevel > 3
            || MinimumCost < 0 || MinimumCost > 100000000
            || !Enum.IsDefined(typeof(PriceBasis), PriceBasis)
            || !Enum.IsDefined(typeof(FailureMode), FailureMode))
            throw new ArgumentException("F12 설정 범위를 확인해 주세요.");
        if (Effects == null) throw new ArgumentException("강화 효과 설정이 없습니다.");
        Effects.MaxLevel = MaxLevel;
        Effects.Validate();
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
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
    public int ProtectionCost => Cost - BaseCost - BoostCost;
    public bool ChanceBoost { get; set; }
    public int BoostCost { get; set; }
    public int BoostBitcoins { get; set; }
    public long AvailableBitcoins { get; set; }
    public double BaseSuccessPercent { get; set; }
    public double BoostPoints => SuccessPercent - BaseSuccessPercent;
    public bool ChanceBoostAvailable => !BindLegacy && BaseSuccessPercent < 100;
    public bool CanPay => AvailableRoubles >= Cost && AvailableBitcoins >= BoostBitcoins;
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
}

public sealed class ApiReply<T> where T : class
{
    public bool Ok { get; set; }
    public string Error { get; set; } = "";
    public T? Data { get; set; }
}
