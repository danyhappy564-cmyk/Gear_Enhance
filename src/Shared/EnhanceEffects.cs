#nullable enable
using System;

namespace GearEnhance;

public sealed class EffectTuning
{
    public double RecoilPerLevel { get; set; } = 1.5;
    public double RecoilCap { get; set; } = 15;
    public double ErgonomicsPerLevel { get; set; } = 1;
    public double ErgonomicsCap { get; set; } = 10;
    public double PrecisionPerLevel { get; set; } = 1;
    public double PrecisionCap { get; set; } = 10;
    public double ArmorDurabilityPerLevel { get; set; } = 3;
    public double ArmorDurabilityCap { get; set; } = 30;
    public double MobilityPerLevel { get; set; } = 2;
    public double MobilityCap { get; set; } = 20;

    public void Validate()
    {
        Check(RecoilPerLevel, 10); Check(RecoilCap, 80);
        Check(ErgonomicsPerLevel, 10); Check(ErgonomicsCap, 50);
        Check(PrecisionPerLevel, 10); Check(PrecisionCap, 80);
        Check(ArmorDurabilityPerLevel, 10); Check(ArmorDurabilityCap, 80);
        Check(MobilityPerLevel, 10); Check(MobilityCap, 80);
    }

    private static void Check(double value, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > max)
            throw new ArgumentException("F12 강화 효과 수치 범위를 확인해 주세요.");
    }
}

public static class EnhanceEffects
{
    public static bool IsWeaponPath(EnhancePath path) => path == EnhancePath.Recoil
        || path == EnhancePath.Ergonomics || path == EnhancePath.Precision;

    public static bool Allowed(EnhancePath path, bool weapon) =>
        Enum.IsDefined(typeof(EnhancePath), path) && IsWeaponPath(path) == weapon;

    public static double Cap(EnhancePath path, EffectTuning tuning) => path switch
    {
        EnhancePath.Recoil => tuning.RecoilCap,
        EnhancePath.Ergonomics => tuning.ErgonomicsCap,
        EnhancePath.Precision => tuning.PrecisionCap,
        EnhancePath.ArmorDurability => tuning.ArmorDurabilityCap,
        EnhancePath.Mobility => tuning.MobilityCap,
        _ => throw new ArgumentException("강화 방향이 올바르지 않습니다.")
    };

    public static double Amount(EnhancePath path, int level, EffectTuning tuning)
    {
        var step = path switch
        {
            EnhancePath.Recoil => tuning.RecoilPerLevel,
            EnhancePath.Ergonomics => tuning.ErgonomicsPerLevel,
            EnhancePath.Precision => tuning.PrecisionPerLevel,
            EnhancePath.ArmorDurability => tuning.ArmorDurabilityPerLevel,
            EnhancePath.Mobility => tuning.MobilityPerLevel,
            _ => throw new ArgumentException("강화 방향이 올바르지 않습니다.")
        };
        return Math.Min(Cap(path, tuning), step * Math.Max(0, Math.Min(100, level)));
    }

    public static string Name(EnhancePath path) => path switch
    {
        EnhancePath.Recoil => "반동 제어", EnhancePath.Ergonomics => "조작성",
        EnhancePath.Precision => "정밀성", EnhancePath.ArmorDurability => "내구성",
        EnhancePath.Mobility => "기동성", _ => "미선택"
    };

    public static string StatName(EnhancePath path) => path switch
    {
        EnhancePath.Recoil => "반동 감소", EnhancePath.Ergonomics => "인체공학 증가",
        EnhancePath.Precision => "탄 퍼짐 감소", EnhancePath.ArmorDurability => "내구도 소모 감소",
        EnhancePath.Mobility => "장비 페널티 완화", _ => ""
    };

    public static string Description(EnhancePath path) => path switch
    {
        EnhancePath.Recoil => "수직·수평 반동에 적용",
        EnhancePath.Ergonomics => "해당 무기의 인체공학에 더함 · 최종 100 상한",
        EnhancePath.Precision => "총알 탄 퍼짐과 산탄 퍼짐에 적용",
        EnhancePath.ArmorDurability => "피격·폭발로 소모되는 방어구 내구도에 적용",
        EnhancePath.Mobility => "해당 장비의 이동·회전·인체공학 페널티에 적용",
        _ => ""
    };

    public static string Format(EnhancePath path, double amount) =>
        path == EnhancePath.Ergonomics ? $"+{amount:0.##}" : $"{amount:0.##}%";

    public static float Reduce(float value, double percent) =>
        value * (float)(1 - Math.Max(0, Math.Min(80, percent)) / 100);

    // EFT's RecoilDelta is an attachment delta, not the final recoil force.
    // Scale the whole multiplier so a -40% attachment delta becomes -49% at +10.
    public static float RecoilDelta(float delta, double percent) => Reduce(1 + delta, percent) - 1;

    public static float Ergonomics(float value, double amount) =>
        (float)Math.Max(0, Math.Min(100, value + amount));
}
