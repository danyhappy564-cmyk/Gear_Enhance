#nullable enable
using System;

namespace GearEnhance;

public enum PriceBasis { Handbook, Flea }
public enum FailureMode { KeepItem, ZeroDurability, Destroy }
public enum EnhancePath { Recoil, Ergonomics, Precision, ArmorDurability, Mobility }

public static class EnhanceProtocol
{
    public const int Version = 2;
    public const string ModVersion = "0.3.1";
    public static void Validate(int version)
    {
        if (version != Version)
            throw new ArgumentException("Gear Enhance 클라이언트와 서버를 모두 0.3.0 이상으로 업데이트해 주세요.");
    }
}

public sealed class EnhanceSettings
{
    public double SuccessPercent { get; set; } = 75;
    public bool AlwaysSucceed { get; set; }
    public int MaxLevel { get; set; } = 10;
    public PriceBasis PriceBasis { get; set; } = PriceBasis.Handbook;
    public double CostPercent { get; set; } = 10;
    public double CostPerLevel { get; set; } = 1.25;
    public int MinimumCost { get; set; } = 1000;
    public FailureMode FailureMode { get; set; } = FailureMode.KeepItem;
    public EffectTuning Effects { get; set; } = new EffectTuning();

    public void Validate()
    {
        if (!Finite(SuccessPercent) || SuccessPercent < 0 || SuccessPercent > 100
            || MaxLevel < 1 || MaxLevel > 100
            || !Finite(CostPercent) || CostPercent < 0 || CostPercent > 1000
            || !Finite(CostPerLevel) || CostPerLevel < 1 || CostPerLevel > 3
            || MinimumCost < 0 || MinimumCost > 100000000
            || !Enum.IsDefined(typeof(PriceBasis), PriceBasis)
            || !Enum.IsDefined(typeof(FailureMode), FailureMode))
            throw new ArgumentException("F12 설정 범위를 확인해 주세요.");
        if (Effects == null) throw new ArgumentException("강화 효과 설정이 없습니다.");
        Effects.Validate();
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

public sealed class EnhanceQuote
{
    public string QuoteId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public int CurrentLevel { get; set; }
    public int NextLevel { get; set; }
    public int Cost { get; set; }
    public long AvailableRoubles { get; set; }
    public double SuccessPercent { get; set; }
    public string PriceSource { get; set; } = "";
    public FailureMode FailureMode { get; set; }
    public int AttachedItems { get; set; }
    public long ExpiresUnixSeconds { get; set; }
    public EnhancePath Path { get; set; }
    public bool IsWeapon { get; set; }
    public bool BindLegacy { get; set; }
    public double CurrentEffect { get; set; }
    public double NextEffect { get; set; }
    public double EffectCap { get; set; }
}

public sealed class EnhanceReceipt
{
    public string QuoteId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public bool Success { get; set; }
    public int Level { get; set; }
    public int Paid { get; set; }
    public FailureMode FailureMode { get; set; }
    public long CompletedUnixSeconds { get; set; }
    public EnhancePath Path { get; set; }
}

public sealed class EnhanceRecord
{
    public string ItemId { get; set; } = "";
    public int Level { get; set; }
    public EnhancePath Path { get; set; }
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
