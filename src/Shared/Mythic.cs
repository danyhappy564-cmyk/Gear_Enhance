#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace GearEnhance;

// 1.4.0: ??? (a fixed 50/50 on ★★★ gear: success makes it mythic, failure destroys it) and the
// mythic track above it, 0..5 like a transcendence track. Armor also gains protected zones.
public sealed class MythicTuning
{
    public double MysteryChance { get; set; } = 50;
    public int MysteryCost { get; set; } = 8000000;
    public int MysteryLedx { get; set; } = 1;
    // Chance of the attempt to level 1..5, by current level.
    public double[] Success { get; set; } = { 40, 30, 20, 15, 10 };
    public double PityStep { get; set; } = 3;
    public double PityCap { get; set; } = 30;
    public int BaseCost { get; set; } = 5000000;
    public double CostGrowth { get; set; } = 1.1;
    public int Ledx { get; set; } = 1;
    public bool RequireMaterials { get; set; } = true;
    public double DowngradeProtectionPercent { get; set; } = 100;
    // Weapon: bullet damage and penetration per level. Armor: damage taken reduction per level.
    public double WeaponPercentPerLevel { get; set; } = 2;
    public double ArmorPercentPerLevel { get; set; } = 2;
    public int ExtraZones { get; set; } = 2;
    public int RerollCost { get; set; } = 2000000;
    public double RerollGrowth { get; set; } = 1.5;
    public int RerollGraphicsCards { get; set; } = 1;

    public void Validate()
    {
        if (!Range(MysteryChance, 0, 100) || MysteryCost < 0 || MysteryCost > 100000000 || MysteryLedx < 0 || MysteryLedx > 10
            || Success == null || Success.Length != MythicRules.MaxLevel || Success.Any(c => !Range(c, 0, 100))
            || !Range(PityStep, 0, 100) || !Range(PityCap, 0, 100) || BaseCost < 0 || BaseCost > 100000000
            || !Range(CostGrowth, 1, 3) || Ledx < 0 || Ledx > 10 || !Range(DowngradeProtectionPercent, 0, 1000)
            || !Range(WeaponPercentPerLevel, 0, 10) || !Range(ArmorPercentPerLevel, 0, 10) || ExtraZones < 0 || ExtraZones > 5
            || RerollCost < 0 || RerollCost > 100000000 || !Range(RerollGrowth, 1, 3) || RerollGraphicsCards < 0 || RerollGraphicsCards > 10)
            throw Loc.Argument("F12 신화 설정 범위를 확인해 주세요.", "Check the F12 mythic setting ranges.");
    }

    private static bool Range(double value, double low, double high) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= low && value <= high;
}

public static class MythicRules
{
    public const int MaxLevel = 5;
    // Attempts at this level or higher drop one level on failure unless protected.
    public const int DropFrom = 4;
    public const string Ledx = "5c0530ee86f774697952d952";

    // Collider names SPT and the game share (BodyPartColliderType). Only zones the gear does
    // not already protect are drawn; a helmet draws from the head, everything else from the body.
    public static readonly string[] BodyZones =
        { "NeckFront", "NeckBack", "LeftUpperArm", "RightUpperArm", "LeftForearm", "RightForearm", "Pelvis", "PelvisBack", "LeftThigh", "RightThigh" };
    public static readonly string[] HeadZones = { "ParietalHead", "BackHead", "Ears", "Eyes", "Jaw" };
    // Hits the armor reduction applies to (game EDamageType flags): Explosion, GrenadeFragment, Bullet,
    // Melee, Landmine, Sniper, Blunt, Btr, Artillery, ThermobaricExplosion. Falls, bleeding, poison stay.
    public const int HitDamageTypes = 4 | 32 | 512 | 1024 | 2048 | 4096 | 8192 | 4194304 | 8388608 | 33554432;

    public static int Clamp(int level) => Math.Max(0, Math.Min(MaxLevel, level));

    public static double Success(int current, MythicTuning tuning) => tuning.Success[Math.Max(0, Math.Min(MaxLevel - 1, current))];

    public static bool DropsOnFailure(int nextLevel) => nextLevel >= DropFrom;

    // One attempt from `current`: base × growth^current, rounded (5,000,000 → 7,320,500 at ×1.1).
    public static int Cost(int current, MythicTuning tuning) => Capped(tuning.BaseCost * Math.Pow(tuning.CostGrowth, Math.Max(0, current)),
        "신화 강화 비용이 1억 루블을 초과합니다. F12 설정을 낮춰 주세요.", "The mythic cost exceeds 100,000,000 roubles. Lower the F12 settings.");

    // The n-th reroll of one gear (0 = first): base × growth^n.
    public static int RerollCost(int rerolls, MythicTuning tuning) => Capped(tuning.RerollCost * Math.Pow(tuning.RerollGrowth, Math.Max(0, rerolls)),
        "다시 뽑기 비용이 1억 루블을 초과합니다.", "The reroll cost exceeds 100,000,000 roubles.");

    public static long ProtectionFee(int baseCost, MythicTuning tuning) =>
        baseCost < 0 ? 0 : (long)Math.Ceiling(baseCost * tuning.DowngradeProtectionPercent / 100);

    // What the ??? and the held levels cost (insurance value), without materials.
    public static double Value(int level, MythicTuning tuning)
    {
        var total = (double)tuning.MysteryCost;
        for (var step = 0; step < Clamp(level); step++) total += Math.Round(tuning.BaseCost * Math.Pow(tuning.CostGrowth, step), MidpointRounding.AwayFromZero);
        return total;
    }

    // LEDX spent on the ??? and the held levels.
    public static int LedxSpent(int level, MythicTuning tuning) => tuning.RequireMaterials ? tuning.MysteryLedx + tuning.Ledx * Clamp(level) : 0;

    public static List<string> Candidates(bool head, IEnumerable<string>? covered)
    {
        var have = new HashSet<string>(covered ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return (head ? HeadZones : BodyZones).Where(zone => !have.Contains(zone)).ToList();
    }

    private static int Capped(double cost, string korean, string english)
    {
        cost = Math.Round(cost, MidpointRounding.AwayFromZero);
        if (double.IsNaN(cost) || double.IsInfinity(cost) || cost > 100000000) throw Loc.Invalid(korean, english);
        return (int)cost;
    }
}

public static class MythicEffects
{
    // Damage and penetration bonus of a mythic weapon, percent (2 × 5 = 10 by default). No star multiplier.
    public static double WeaponPercent(int level, MythicTuning tuning) => MythicRules.Clamp(level) * tuning.WeaponPercentPerLevel;
    // Damage taken reduction of mythic armor, percent.
    public static double ArmorPercent(int level, MythicTuning tuning) => MythicRules.Clamp(level) * tuning.ArmorPercentPerLevel;
}
