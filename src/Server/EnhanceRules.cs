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

    // What enhancing this gear to its current tracks and stars cost at this price:
    // every level's base cost plus the star evolutions. Basis of the insurance fee.
    // From 1.3.0 the held transcendence levels and their materials' book value count too.
    public static long Value(double basePrice, IReadOnlyDictionary<EnhancePath, int>? tracks, int stars, EnhanceSettings settings,
        int second = 0, int third = 0, double materials = 0)
    {
        settings.Validate();
        if (!double.IsFinite(basePrice) || basePrice <= 0) return 0;
        var total = 0.0;
        foreach (var level in tracks?.Values ?? Enumerable.Empty<int>())
            for (var step = 0; step < Math.Clamp(level, 0, 100); step++)
                total += Math.Ceiling(Math.Max(settings.MinimumCost, basePrice * settings.CostPercent / 100 * Math.Pow(settings.CostPerLevel, step)));
        if (stars >= 2) total += Math.Ceiling(Math.Max(2000000, Math.Max(settings.MinimumCost, basePrice * settings.EvolutionCostPercent / 100)));
        if (stars >= 3) total += 4000000;
        total += TranscendRules.Value(basePrice, second, third, settings.Transcend);
        if (double.IsFinite(materials) && materials > 0) total += materials;
        return (long)Math.Min(total, 10_000_000_000d);
    }

    // Plates worth more than the ones paid for: charge the price difference for the average
    // attempts of every level and evolution already held, so enhancing with cheap plates and
    // swapping in better ones later costs what enhancing with the better ones would on average.
    public static int InsertSurcharge(double priceDifference, IReadOnlyDictionary<EnhancePath, int>? tracks, int stars, EnhanceSettings settings,
        int second = 0, int third = 0)
    {
        settings.Validate();
        if (!double.IsFinite(priceDifference) || priceDifference <= 0) return 0;
        var total = 0.0;
        foreach (var level in tracks?.Values ?? Enumerable.Empty<int>())
            for (var step = 0; step < Math.Clamp(level, 0, 100); step++)
                total += priceDifference * settings.CostPercent / 100 * Math.Pow(settings.CostPerLevel, step)
                    * Attempts(EnhanceProgression.Success(step, settings));
        if (stars >= 2) total += priceDifference * settings.EvolutionCostPercent / 100
            * Attempts(settings.AlwaysSucceed ? 100 : settings.EvolutionSuccessPercent);
        total += TranscendRules.Surcharge(priceDifference, second, third, settings.Transcend, settings.AlwaysSucceed);
        total = Math.Ceiling(total * settings.InsertSurchargePercent / 100);
        if (!double.IsFinite(total) || total > 100000000)
            throw Loc.Invalid("방탄판 등록비가 1억 루블을 초과합니다. F12 설정을 낮춰 주세요.", "The plate registration fee exceeds 100,000,000 roubles. Lower the F12 settings.");
        return (int)total;
    }

    // Average attempts at this chance; below 1% counts as 1%.
    private static double Attempts(double successPercent) => 100 / Math.Clamp(successPercent, 1, 100);

    public static int InsuranceFee(long value, InsuranceTier tier, EnhanceSettings settings)
    {
        settings.Validate();
        if (tier == InsuranceTier.None || !Enum.IsDefined(tier)) throw Loc.Argument("보험 등급을 골라 주세요.", "Choose an insurance tier.");
        var fee = Math.Ceiling(Math.Max(settings.MinimumCost, value * settings.FeePercent(tier) / 100));
        if (!double.IsFinite(fee) || fee > 100000000)
            throw Loc.Invalid("강화 보험료가 1억 루블을 초과합니다. F12 설정을 낮춰 주세요.", "The enhancement insurance fee exceeds 100,000,000 roubles. Lower the F12 settings.");
        return (int)fee;
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
