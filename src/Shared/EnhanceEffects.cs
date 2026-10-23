#nullable enable
using System;

namespace GearEnhance;

public sealed class EffectTuning
{
    public int MaxLevel { get; set; } = 10;
    public double ThirdStarBonusPercent { get; set; } = StarEvolution.ThirdDefaultBonus;
    public double CompletionBonusPercent { get; set; } = StarEvolution.SecondDefaultBonus;
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
        if (MaxLevel < 1 || MaxLevel > 100) throw Loc.Argument("최대 강화 단계를 확인해 주세요.", "Check the maximum enhancement level.");
        Check(CompletionBonusPercent, 300); Check(ThirdStarBonusPercent, 300);
        Check(RecoilPerLevel, 10); Check(RecoilCap, 80);
        Check(ErgonomicsPerLevel, 10); Check(ErgonomicsCap, 50);
        Check(PrecisionPerLevel, 10); Check(PrecisionCap, 80);
        Check(ArmorDurabilityPerLevel, 10); Check(ArmorDurabilityCap, 80);
        Check(MobilityPerLevel, 10); Check(MobilityCap, 80);
    }

    private static void Check(double value, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > max)
            throw Loc.Argument("F12 강화 효과 수치 범위를 확인해 주세요.", "Check the F12 effect value ranges.");
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
        _ => throw Loc.Argument("강화 항목이 올바르지 않습니다.", "Invalid enhancement track.")
    };

    public static double Amount(EnhancePath path, int level, EffectTuning tuning, bool complete = false)
        => Amount(path, level, tuning, complete ? 2 : 1);

    public static double Amount(EnhancePath path, int level, EffectTuning tuning, int stars)
    {
        var step = path switch
        {
            EnhancePath.Recoil => tuning.RecoilPerLevel,
            EnhancePath.Ergonomics => tuning.ErgonomicsPerLevel,
            EnhancePath.Precision => tuning.PrecisionPerLevel,
            EnhancePath.ArmorDurability => tuning.ArmorDurabilityPerLevel,
            EnhancePath.Mobility => tuning.MobilityPerLevel,
            _ => throw Loc.Argument("강화 항목이 올바르지 않습니다.", "Invalid enhancement track.")
        };
        var maximum = tuning.MaxLevel;
        var total = Math.Min(Cap(path, tuning), step * maximum);
        var amount = total * EnhanceProgression.Points(level, maximum) / EnhanceProgression.Points(maximum, maximum);
        amount *= 1 + StarEvolution.Bonus(stars, tuning) / 100;
        return Math.Min(path == EnhancePath.Ergonomics ? 50 : 80, amount);
    }

    public static string Name(EnhancePath path) => path switch
    {
        EnhancePath.Recoil => Loc.L("반동 제어", "Recoil"), EnhancePath.Ergonomics => Loc.L("조작성", "Handling"),
        EnhancePath.Precision => Loc.L("정밀성", "Precision"), EnhancePath.ArmorDurability => Loc.L("내구성", "Durability"),
        EnhancePath.Mobility => Loc.L("기동성", "Mobility"), _ => Loc.L("미선택", "None")
    };

    // F12 cfg keys are built from this Korean text and must never change with language.
    public static string KoreanStatName(EnhancePath path) => path switch
    {
        EnhancePath.Recoil => "반동 감소", EnhancePath.Ergonomics => "인체공학 증가",
        EnhancePath.Precision => "탄 퍼짐 감소", EnhancePath.ArmorDurability => "내구도 소모 감소",
        EnhancePath.Mobility => "장비 페널티 완화", _ => ""
    };

    public static string StatName(EnhancePath path) => Loc.L(KoreanStatName(path), path switch
    {
        EnhancePath.Recoil => "Recoil reduction", EnhancePath.Ergonomics => "Ergonomics bonus",
        EnhancePath.Precision => "Dispersion reduction", EnhancePath.ArmorDurability => "Durability loss reduction",
        EnhancePath.Mobility => "Gear penalty relief", _ => ""
    });

    public static string Description(EnhancePath path) => path switch
    {
        EnhancePath.Recoil => Loc.L("수직·수평 반동에 적용", "Applies to vertical and horizontal recoil"),
        EnhancePath.Ergonomics => Loc.L("해당 무기의 인체공학에 더함 · 최종 100 상한", "Added to this weapon's ergonomics · final cap 100"),
        EnhancePath.Precision => Loc.L("총알 탄 퍼짐과 산탄 퍼짐에 적용", "Applies to bullet and buckshot dispersion"),
        EnhancePath.ArmorDurability => Loc.L("피격·폭발로 소모되는 방어구 내구도에 적용", "Applies to armor durability lost to hits and blasts"),
        EnhancePath.Mobility => Loc.L("해당 장비의 이동·회전·인체공학 페널티에 적용", "Applies to this gear's move, turn and ergo penalties"),
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
