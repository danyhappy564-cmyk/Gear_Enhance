namespace GearEnhance;

// Pure rules shared by the real SPT adapter and the executable tests.
public static class EnhanceRules
{
    public static int EvolutionCost(double basePrice, EnhanceSettings settings)
    {
        settings.Validate();
        if (!double.IsFinite(basePrice) || basePrice <= 0)
            throw Loc.Invalid("가격을 찾을 수 없는 아이템은 진화할 수 없습니다.", "Items without a price cannot be evolved.");
        var cost = Math.Ceiling(Math.Max(2000000, Math.Max(settings.MinimumCost, basePrice * settings.EvolutionCostPercent / 100)));
        if (!double.IsFinite(cost) || cost > 100000000)
            throw Loc.Invalid("진화 비용이 1억 루블을 초과합니다. F12 설정을 낮춰 주세요.", "The evolution cost exceeds 100,000,000 roubles. Lower the F12 settings.");
        return (int)cost;
    }
    public static int Cost(double basePrice, int level, EnhanceSettings settings)
    {
        settings.Validate();
        if (!double.IsFinite(basePrice) || basePrice <= 0)
            throw Loc.Invalid("가격을 찾을 수 없는 아이템은 강화할 수 없습니다.", "Items without a price cannot be enhanced.");
        if (level < 0 || level > 100 || level >= settings.MaxLevel)
            throw Loc.Invalid("최대 강화 단계입니다.", "Already at the maximum enhancement level.");
        var cost = Math.Ceiling(Math.Max(settings.MinimumCost,
            basePrice * settings.CostPercent / 100 * Math.Pow(settings.CostPerLevel, level)));
        if (!double.IsFinite(cost) || cost > 100000000)
            throw Loc.Invalid("강화 비용이 1억 루블을 초과합니다. F12 설정을 낮춰 주세요.", "The enhancement cost exceeds 100,000,000 roubles. Lower the F12 settings.");
        return checked((int)cost);
    }

    public static bool Succeeds(double percent, double draw)
    {
        if (!double.IsFinite(percent) || percent < 0 || percent > 100
            || !double.IsFinite(draw) || draw < 0 || draw >= 1)
            throw new ArgumentOutOfRangeException(nameof(percent));
        return percent == 100 || (percent > 0 && draw < percent / 100);
    }

    public static FailureMode Failure(EnhanceQuote quote, double draw) => quote.DestructionPercent > 0
        && Succeeds(quote.DestructionPercent, draw) ? FailureMode.Destroy : quote.FailureMode;

    // Walk with a visited set and inventory-size bound. Malformed parent cycles
    // must never hang the game/server or qualify an equipped item as stash loot.
    public static bool IsBelow(string itemId, string root, IReadOnlyDictionary<string, string> parents)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var step = 0; step <= parents.Count; step++)
        {
            if (itemId == root) return true;
            if (!seen.Add(itemId) || !parents.TryGetValue(itemId, out var parent)) return false;
            itemId = parent;
        }
        return false;
    }
}
