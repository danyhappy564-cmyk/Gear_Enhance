#nullable enable
using System;
using System.Collections.Generic;

namespace GearEnhance;

// 1.3.0 transcendence: two tracks above the star evolutions, 0..5 each and independent.
// Their effects never take the star multiplier.
public enum TranscendTrack { None = 0, Second = 2, Third = 3 }
// What a track does on this gear. The weapon third track depends on its fire modes.
public enum TranscendKind { None, HorizontalRecoil, MaxDurability, FireRate, Reliability, ArmorClass }

public sealed class TranscendMaterial
{
    public string Template { get; set; } = "";
    public int Count { get; set; }
    public long Owned { get; set; }
}

public sealed class TranscendTuning
{
    // Chance of the attempt to level 1..5, by current level.
    public double[] SecondSuccess { get; set; } = { 60, 50, 40, 30, 20 };
    public double[] ThirdSuccess { get; set; } = { 40, 30, 20, 15, 10 };
    // Every failure adds to that track's next chance until a success resets it.
    public double SecondPityStep { get; set; } = 5;
    public double ThirdPityStep { get; set; } = 3;
    public double PityCap { get; set; } = 30;
    public double SecondCostPercent { get; set; } = 50;
    public double ThirdCostPercent { get; set; } = 100;
    public int SecondMinimumCost { get; set; } = 1000000;
    public int ThirdMinimumCost { get; set; } = 3000000;
    public double CostGrowth { get; set; } = 1.5;
    public bool RequireMaterials { get; set; } = true;
    // Downgrade protection for level 4 and 5 attempts: this percent of the base cost, flat
    // (the 1.2.0 level² fee would be 5 to 9 times the base cost at these minimum costs).
    public double DowngradeProtectionPercent { get; set; } = 100;
    public double HorizontalRecoilPerLevel { get; set; } = 6;
    public double MaxDurabilityPerLevel { get; set; } = 6;
    public double FireRatePercentPerLevel { get; set; } = 6;
    public double FireRateFlatPerLevel { get; set; } = 50;
    public double MalfunctionPerLevel { get; set; } = 10;
    public double OverheatPerLevel { get; set; } = 8;
    public double BluntPerLevel { get; set; } = 5;
    // A part already at the class cap takes this blunt reduction per class it cannot gain.
    public double BluntPerMissingClass { get; set; } = 10;

    public void Validate()
    {
        if (!Chances(SecondSuccess) || !Chances(ThirdSuccess)
            || !Range(SecondPityStep, 0, 100) || !Range(ThirdPityStep, 0, 100) || !Range(PityCap, 0, 100)
            || !Range(SecondCostPercent, 0, 10000) || !Range(ThirdCostPercent, 0, 10000)
            || SecondMinimumCost < 0 || SecondMinimumCost > 100000000 || ThirdMinimumCost < 0 || ThirdMinimumCost > 100000000
            || !Range(CostGrowth, 1, 3) || !Range(DowngradeProtectionPercent, 0, 1000)
            || !Range(HorizontalRecoilPerLevel, 0, 16) || !Range(MaxDurabilityPerLevel, 0, 20)
            || !Range(FireRatePercentPerLevel, 0, 20) || !Range(FireRateFlatPerLevel, 0, 200)
            || !Range(MalfunctionPerLevel, 0, 20) || !Range(OverheatPerLevel, 0, 20)
            || !Range(BluntPerLevel, 0, 20) || !Range(BluntPerMissingClass, 0, 50))
            throw Loc.Argument("F12 초월 강화 설정 범위를 확인해 주세요.", "Check the F12 transcendence setting ranges.");
    }

    private static bool Chances(double[]? values)
    {
        if (values == null || values.Length != TranscendRules.MaxLevel) return false;
        foreach (var value in values) if (!Range(value, 0, 100)) return false;
        return true;
    }

    private static bool Range(double value, double low, double high) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= low && value <= high;
}

public static class TranscendRules
{
    public const int MaxLevel = 5;
    // Attempts at this level or higher drop one level on failure unless protected.
    public const int DropFrom = 4;

    public static int StarsNeeded(TranscendTrack track) => track == TranscendTrack.Third ? 3 : 2;
    public static bool Valid(TranscendTrack track) => track == TranscendTrack.Second || track == TranscendTrack.Third;

    public static double Success(TranscendTrack track, int current, TranscendTuning tuning)
    {
        var table = track == TranscendTrack.Third ? tuning.ThirdSuccess : tuning.SecondSuccess;
        return table[Math.Max(0, Math.Min(MaxLevel - 1, current))];
    }

    public static double PityStep(TranscendTrack track, TranscendTuning tuning) =>
        track == TranscendTrack.Third ? tuning.ThirdPityStep : tuning.SecondPityStep;

    public static bool DropsOnFailure(int nextLevel) => nextLevel >= DropFrom;

    // One attempt from `current`: max(minimum, price × percent × growth^current), at most 100,000,000.
    public static int Cost(double price, TranscendTrack track, int current, TranscendTuning tuning)
    {
        if (double.IsNaN(price) || double.IsInfinity(price) || price <= 0)
            throw Loc.Invalid("가격을 찾을 수 없는 아이템은 초월할 수 없습니다.", "Items without a price cannot be transcended.");
        var cost = Math.Ceiling(Math.Max(Minimum(track, tuning), Step(price, track, current, tuning)));
        if (double.IsNaN(cost) || double.IsInfinity(cost) || cost > 100000000)
            throw Loc.Invalid("초월 비용이 1억 루블을 초과합니다. F12 설정을 낮춰 주세요.", "The transcendence cost exceeds 100,000,000 roubles. Lower the F12 settings.");
        return (int)cost;
    }

    // What the held levels cost at this price (insurance value), without materials.
    public static double Value(double price, int second, int third, TranscendTuning tuning)
    {
        if (double.IsNaN(price) || double.IsInfinity(price) || price <= 0) return 0;
        var total = 0.0;
        for (var step = 0; step < Clamp(second); step++) total += Math.Ceiling(Math.Max(Minimum(TranscendTrack.Second, tuning), Step(price, TranscendTrack.Second, step, tuning)));
        for (var step = 0; step < Clamp(third); step++) total += Math.Ceiling(Math.Max(Minimum(TranscendTrack.Third, tuning), Step(price, TranscendTrack.Third, step, tuning)));
        return total;
    }

    // Plate price difference over the held levels (no minimum), like the enhancement surcharge.
    public static double Surcharge(double priceDifference, int second, int third, TranscendTuning tuning)
    {
        if (double.IsNaN(priceDifference) || double.IsInfinity(priceDifference) || priceDifference <= 0) return 0;
        var total = 0.0;
        for (var step = 0; step < Clamp(second); step++) total += Step(priceDifference, TranscendTrack.Second, step, tuning);
        for (var step = 0; step < Clamp(third); step++) total += Step(priceDifference, TranscendTrack.Third, step, tuning);
        return total;
    }

    public static long ProtectionFee(int baseCost, TranscendTuning tuning) =>
        baseCost < 0 ? 0 : (long)Math.Ceiling(baseCost * tuning.DowngradeProtectionPercent / 100);

    public static int Clamp(int level) => Math.Max(0, Math.Min(MaxLevel, level));

    private static double Minimum(TranscendTrack track, TranscendTuning tuning) =>
        track == TranscendTrack.Third ? tuning.ThirdMinimumCost : tuning.SecondMinimumCost;

    private static double Step(double price, TranscendTrack track, int current, TranscendTuning tuning) =>
        price * (track == TranscendTrack.Third ? tuning.ThirdCostPercent : tuning.SecondCostPercent) / 100 * Math.Pow(tuning.CostGrowth, current);

    // Materials for the attempt to `nextLevel` (design 4, ids from SPT 4.1.6 ItemTpl).
    public const string WeaponParts = "5d1c819a86f774771b0acd6c";
    public const string GunpowderEagle = "5d6fc78386f77449d825f9dc";
    public const string AramidFabric = "5e2af4d286f7746d4159f07a";
    public const string RipstopFabric = "5e2af4a786f7746d3f3c3400";
    public const string MilitaryBoard = "5d0376a486f7747d8050965c";
    public const string Virtex = "5c05308086f7746b2101e90b";
    public const string GraphicsCard = "57347ca924597744596b4e71";
    public const string Ledx = "5c0530ee86f774697952d952";

    private static readonly (string Template, int Count)[][] WeaponSecond =
    {
        new[] { (WeaponParts, 1) },
        new[] { (WeaponParts, 2) },
        new[] { (WeaponParts, 2), (GunpowderEagle, 1) },
        new[] { (WeaponParts, 3), (GunpowderEagle, 2) },
        new[] { (WeaponParts, 3), (GraphicsCard, 1) },
    };
    private static readonly (string Template, int Count)[][] ArmorSecond =
    {
        new[] { (AramidFabric, 1) },
        new[] { (AramidFabric, 2) },
        new[] { (AramidFabric, 2), (RipstopFabric, 1) },
        new[] { (AramidFabric, 3), (RipstopFabric, 2) },
        new[] { (AramidFabric, 3), (GraphicsCard, 1) },
    };
    private static readonly (string Template, int Count)[][] WeaponThird =
    {
        new[] { (MilitaryBoard, 1) },
        new[] { (MilitaryBoard, 2) },
        new[] { (MilitaryBoard, 2), (Virtex, 1) },
        new[] { (Virtex, 1), (GraphicsCard, 1) },
        new[] { (Virtex, 2), (GraphicsCard, 2) },
    };
    private static readonly (string Template, int Count)[][] ArmorThird =
    {
        new[] { (AramidFabric, 2) },
        new[] { (AramidFabric, 2), (RipstopFabric, 1) },
        new[] { (AramidFabric, 3), (GraphicsCard, 1) },
        new[] { (GraphicsCard, 2), (RipstopFabric, 2) },
        new[] { (GraphicsCard, 2), (Ledx, 1) },
    };

    public static IReadOnlyList<(string Template, int Count)> Materials(bool weapon, TranscendTrack track, int nextLevel)
    {
        if (!Valid(track) || nextLevel < 1 || nextLevel > MaxLevel) return Array.Empty<(string, int)>();
        var table = track == TranscendTrack.Third ? weapon ? WeaponThird : ArmorThird : weapon ? WeaponSecond : ArmorSecond;
        return table[nextLevel - 1];
    }
}

// Effect sizes shared by the server quote and the client patches.
public static class TranscendEffects
{
    public const int ClassCap = 6;

    public static TranscendKind Kind(bool weapon, TranscendTrack track, bool automatic) =>
        track == TranscendTrack.Second ? weapon ? TranscendKind.HorizontalRecoil : TranscendKind.MaxDurability
        : track == TranscendTrack.Third ? weapon ? automatic ? TranscendKind.FireRate : TranscendKind.Reliability : TranscendKind.ArmorClass
        : TranscendKind.None;

    // What a level gives, as the window shows it: the main effect (percent), the second line
    // (overheat % for Reliability, the rpm limit for FireRate) and the armor class bonus.
    public static (double Main, double Secondary, int Class) Display(TranscendKind kind, int level, TranscendTuning tuning) => kind switch
    {
        TranscendKind.HorizontalRecoil => (HorizontalRecoil(level, tuning), 0, 0),
        TranscendKind.MaxDurability => (MaxDurability(level, tuning), 0, 0),
        TranscendKind.FireRate => (FireRatePercent(level, tuning), FireRateFlat(level, tuning), 0),
        TranscendKind.Reliability => (Malfunction(level, tuning), Overheat(level, tuning), 0),
        // Blunt reduction of a part below the class cap; capped parts get more.
        TranscendKind.ArmorClass => (Blunt(level, 1, tuning), 0, ClassBonus(level)),
        _ => (0, 0, 0)
    };

    public static double HorizontalRecoil(int level, TranscendTuning tuning) => TranscendRules.Clamp(level) * tuning.HorizontalRecoilPerLevel;
    public static double MaxDurability(int level, TranscendTuning tuning) => TranscendRules.Clamp(level) * tuning.MaxDurabilityPerLevel;
    public static double FireRatePercent(int level, TranscendTuning tuning) => TranscendRules.Clamp(level) * tuning.FireRatePercentPerLevel;
    public static double FireRateFlat(int level, TranscendTuning tuning) => TranscendRules.Clamp(level) * tuning.FireRateFlatPerLevel;
    // Rounds per minute added: the smaller of the flat amount and the percent of the base rate.
    public static double FireRateBonus(double baseRate, int level, TranscendTuning tuning) =>
        double.IsNaN(baseRate) || double.IsInfinity(baseRate) || baseRate <= 0 ? 0
            : Math.Min(FireRateFlat(level, tuning), baseRate * FireRatePercent(level, tuning) / 100);
    public static double Malfunction(int level, TranscendTuning tuning) => Math.Min(100, TranscendRules.Clamp(level) * tuning.MalfunctionPerLevel);
    public static double Overheat(int level, TranscendTuning tuning) => Math.Min(100, TranscendRules.Clamp(level) * tuning.OverheatPerLevel);
    public static int ClassBonus(int level) => level >= 5 ? 2 : level >= 3 ? 1 : 0;

    // Backward kick reduction to apply on top of the enhancement recoil reduction, so the
    // two together never exceed the 80% limit (1 − kept × (1 − this) ≤ 0.8).
    public static double HorizontalWithCap(double existingPercent, double transcendPercent)
    {
        var kept = 1 - Math.Max(0, Math.Min(80, existingPercent)) / 100;
        var limit = kept <= 0.2 ? 0 : (1 - 0.2 / kept) * 100;
        return Math.Max(0, Math.Min(Math.Min(80, transcendPercent), limit));
    }

    // Left/right kick range narrowed around its centre.
    public static (float Low, float High) Narrow(float low, float high, double percent)
    {
        var centre = (low + high) / 2;
        var half = (high - low) / 2 * (float)(1 - Math.Max(0, Math.Min(80, percent)) / 100);
        return (centre - half, centre + half);
    }

    // F12 text "60, 50, 40, 30, 20" → five chances, or null when it is not that.
    public static double[]? ParseChances(string? text)
    {
        var parts = (text ?? "").Split(new[] { ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != TranscendRules.MaxLevel) return null;
        var values = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i])
                || double.IsNaN(values[i]) || values[i] < 0 || values[i] > 100) return null;
        return values;
    }

    // A part's class with the bonus, never above the cap; parts at or above it keep theirs.
    public static int ArmorClass(int level, int partClass) =>
        partClass <= 0 || partClass >= ClassCap ? partClass : Math.Min(ClassCap, partClass + ClassBonus(level));

    // Blunt reduction for one part: the level steps plus the class bonus it could not take.
    public static double Blunt(int level, int partClass, TranscendTuning tuning)
    {
        var bonus = partClass <= 0 ? 0 : ClassBonus(level);
        var missing = bonus - (ArmorClass(level, partClass) - partClass);
        return Math.Min(90, TranscendRules.Clamp(level) * tuning.BluntPerLevel + Math.Max(0, missing) * tuning.BluntPerMissingClass);
    }
}
