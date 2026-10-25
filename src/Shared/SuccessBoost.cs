#nullable enable
using System;
using System.Collections.Generic;

namespace GearEnhance;

// 1.4.0 chance boost: each item of a material adds a fixed number of percentage points up to
// that material's cap, and costs a fixed amount of roubles. At most two kinds per attempt.
public enum BoostMaterial { Dogtag = 0, GraphicsCard = 1, Bitcoin = 2 }

public sealed class BoostChoice
{
    public BoostMaterial Kind { get; set; }
    public int Count { get; set; }
}

// One chosen material in a quote or receipt.
public sealed class BoostLine
{
    public BoostMaterial Kind { get; set; }
    public int Count { get; set; }
    public int MaxCount { get; set; }
    public double Points { get; set; }
    public long Roubles { get; set; }
    // Usable in the stash, minus what the attempt's own materials already need.
    public long Owned { get; set; }
}

public sealed class BoostTuning
{
    public double DogtagPoints { get; set; } = 0.1;
    public double GraphicsCardPoints { get; set; } = 1;
    public double BitcoinPoints { get; set; } = 5;
    public double DogtagCap { get; set; } = 5;
    public double GraphicsCardCap { get; set; } = 10;
    public double BitcoinCap { get; set; } = 20;
    public int DogtagRoubles { get; set; } = 5000;
    public int GraphicsCardRoubles { get; set; } = 100000;
    public int BitcoinRoubles { get; set; } = 300000;
    public int MaxKinds { get; set; } = 2;

    public void Validate()
    {
        if (!Range(DogtagPoints, 0, 100) || !Range(GraphicsCardPoints, 0, 100) || !Range(BitcoinPoints, 0, 100)
            || !Range(DogtagCap, 0, 100) || !Range(GraphicsCardCap, 0, 100) || !Range(BitcoinCap, 0, 100)
            || DogtagRoubles < 0 || DogtagRoubles > 100000000 || GraphicsCardRoubles < 0 || GraphicsCardRoubles > 100000000
            || BitcoinRoubles < 0 || BitcoinRoubles > 100000000 || MaxKinds < 0 || MaxKinds > 3)
            throw Loc.Argument("F12 성공률 재료 설정 범위를 확인해 주세요.", "Check the F12 chance material setting ranges.");
    }

    private static bool Range(double value, double low, double high) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= low && value <= high;
}

public static class SuccessBoost
{
    public const string BitcoinTemplate = "59faff1d86f7746c51718c9c";
    public const string GraphicsCardTemplate = "57347ca924597744596b4e71";
    // Every dogtag in SPT 4.1.6 ItemTpl (BEAR/USEC, EOD, TUE, prestige 1..4); faction and level do not matter.
    public static readonly string[] DogtagTemplates =
    {
        "59f32bb586f774757e1e8442", "6662e9aca7e0b43baa3d5f74", "6662e9cda7e0b43baa3d5f76",
        "675dc9d37ae1a8792107ca96", "675dcb0545b1a2d108011b2b", "684180bc51bf8645f7067bc8", "684181208d035f60230f63f9",
        "59f32c3b86f77472a31742f0", "6662e9f37fa79a6d83730fa0", "6662ea05f6259762c56f3189",
        "6764207f2fa5e32733055c4a", "6764202ae307804338014c1a", "68418091b5b0c9e4c60f0e7a", "684180ee9b6d80d840042e8a"
    };
    public static readonly BoostMaterial[] Kinds = { BoostMaterial.Dogtag, BoostMaterial.GraphicsCard, BoostMaterial.Bitcoin };

    public static bool Valid(BoostMaterial kind) => kind == BoostMaterial.Dogtag || kind == BoostMaterial.GraphicsCard || kind == BoostMaterial.Bitcoin;

    public static IReadOnlyList<string> Templates(BoostMaterial kind) => kind == BoostMaterial.Dogtag ? DogtagTemplates
        : kind == BoostMaterial.GraphicsCard ? new[] { GraphicsCardTemplate } : new[] { BitcoinTemplate };

    public static double PointsEach(BoostMaterial kind, BoostTuning tuning) => kind == BoostMaterial.Dogtag ? tuning.DogtagPoints
        : kind == BoostMaterial.GraphicsCard ? tuning.GraphicsCardPoints : tuning.BitcoinPoints;

    public static double Cap(BoostMaterial kind, BoostTuning tuning) => kind == BoostMaterial.Dogtag ? tuning.DogtagCap
        : kind == BoostMaterial.GraphicsCard ? tuning.GraphicsCardCap : tuning.BitcoinCap;

    public static int RoublesEach(BoostMaterial kind, BoostTuning tuning) => kind == BoostMaterial.Dogtag ? tuning.DogtagRoubles
        : kind == BoostMaterial.GraphicsCard ? tuning.GraphicsCardRoubles : tuning.BitcoinRoubles;

    // 5 / 0.1 is 49.999… in doubles (more so from an F12 float); the epsilon keeps it at 50.
    public static int MaxCount(BoostMaterial kind, BoostTuning tuning)
    {
        var each = PointsEach(kind, tuning);
        return each <= 0 ? 0 : (int)Math.Min(1000, Math.Floor(Cap(kind, tuning) / each + 1e-6));
    }

    public static double Points(BoostMaterial kind, int count, BoostTuning tuning) =>
        Math.Min(Cap(kind, tuning), Math.Round(Math.Max(0, count) * PointsEach(kind, tuning), 4));
}
