#nullable enable
using System;
using System.Collections.Generic;

namespace GearEnhance;

public static class EnhanceProgression
{
    public static int HalfLevel(int maximum) => Math.Max(1, (maximum + 1) / 2);

    public static double Success(int current, EnhanceSettings settings) => settings.AlwaysSucceed
        ? 100 : Math.Max(0, settings.SuccessPercent - settings.SuccessLossPerLevel * current);

    public static bool AllAtLeast(IReadOnlyDictionary<EnhancePath, int>? tracks, bool weapon, int minimum)
    {
        if (tracks == null) return false;
        int Level(EnhancePath path) => tracks.TryGetValue(path, out var level) ? level : 0;
        return weapon
            ? Level(EnhancePath.Recoil) >= minimum && Level(EnhancePath.Ergonomics) >= minimum && Level(EnhancePath.Precision) >= minimum
            : Level(EnhancePath.ArmorDurability) >= minimum && Level(EnhancePath.Mobility) >= minimum;
    }

    // Early gains are equal; gains after halfway rise linearly to four times
    // the early gain. Normalize the sum so MaxLevel still reaches the tuned cap.
    public static double GainMultiplier(int level, int maximum)
    {
        var half = HalfLevel(maximum);
        if (level < half) return 1;
        return maximum <= half ? 4 : 1 + 3.0 * Math.Min(maximum - half, level - half) / (maximum - half);
    }
    public static double Points(int level, int maximum)
    {
        level = Math.Max(0, Math.Min(maximum, level));
        var half = HalfLevel(maximum);
        if (maximum <= half) return level < maximum ? level : level + 3;
        var high = Math.Max(0, level - half);
        return level + 3.0 * high * (high + 1) / (2 * (maximum - half));
    }
}
