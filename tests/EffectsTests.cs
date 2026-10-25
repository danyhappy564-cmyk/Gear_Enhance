using GearEnhance;
using Xunit;

namespace GearEnhanceTests;

public sealed class EffectsTests
{
    [Theory]
    [InlineData(EnhancePath.Recoil, 1.5, 15)]
    [InlineData(EnhancePath.Ergonomics, 1, 10)]
    [InlineData(EnhancePath.Precision, 1, 10)]
    [InlineData(EnhancePath.ArmorDurability, 3, 30)]
    [InlineData(EnhancePath.Mobility, 2, 20)]
    public void Weighted_progression_preserves_the_terminal_effect_caps(EnhancePath path, double step, double cap)
    {
        Assert.Equal(0, EnhanceEffects.Amount(path, 0, new()));
        Assert.Equal(step * 10 / 19, EnhanceEffects.Amount(path, 1, new()), 10);
        Assert.Equal(cap, EnhanceEffects.Amount(path, 10, new()));
        Assert.Equal(cap, EnhanceEffects.Amount(path, 100, new()));
    }

    [Fact]
    public void Early_steps_are_equal_and_late_steps_accelerate_to_four_times_the_gain()
    {
        var values = Enumerable.Range(0, 11).Select(level => EnhanceEffects.Amount(EnhancePath.Recoil, level, new())).ToArray();
        var earlyGain = values[1];
        for (var level = 1; level <= 5; level++) Assert.Equal(earlyGain, values[level] - values[level - 1], 10);
        for (var level = 6; level <= 10; level++) Assert.True(values[level] - values[level - 1] > values[level - 1] - values[level - 2]);
        Assert.Equal(earlyGain * 4, values[10] - values[9], 10);
        Assert.Equal(15, values[10]);
    }

    [Fact]
    public void Profile_switch_never_reuses_another_profiles_records()
    {
        var registry = new EffectRegistry();
        registry.Replace(new() { ProfileId = "a", Items = [new() { ItemId = "gun", Level = 10, Path = EnhancePath.Recoil }] }, "a");
        Assert.Throws<InvalidOperationException>(() => registry.Replace(new() { ProfileId = "b" }, "a"));
        registry.Clear();
        registry.Replace(new() { ProfileId = "b" }, "b");
        Assert.Equal(0, registry.Amount("gun", EnhancePath.Recoil));
    }

    [Fact]
    public void Receipt_adds_independent_tracks_and_destruction_clears_all_effects()
    {
        var registry = new EffectRegistry();
        registry.ApplyReceipt(new() { Success = true, ItemId = "gun", Level = 5, Path = EnhancePath.Recoil }, "a");
        Assert.Equal(75.0 / 19, registry.Amount("gun", EnhancePath.Recoil), 10);
        registry.ApplyReceipt(new() { Success = true, ItemId = "gun", Level = 5, Path = EnhancePath.Ergonomics }, "a");
        Assert.Equal(75.0 / 19, registry.Amount("gun", EnhancePath.Recoil), 10);
        Assert.Equal(50.0 / 19, registry.Amount("gun", EnhancePath.Ergonomics), 10);
        Assert.Equal("★ +10", registry.Badge("gun"));
        registry.ApplyReceipt(new() { ItemId = "gun", FailureMode = FailureMode.Destroy }, "a");
        Assert.Null(registry.Find("gun"));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(81)]
    public void Invalid_tuning_is_rejected(double value) =>
        Assert.Throws<ArgumentException>(() => new EffectTuning { RecoilCap = value }.Validate());

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(100)]
    public void Changing_the_maximum_keeps_the_curve_finite_monotonic_and_at_the_configured_cap(int maximum)
    {
        var tuning = new EffectTuning { MaxLevel = maximum, RecoilCap = 1.5 * maximum > 80 ? 80 : 1.5 * maximum };
        tuning.Validate();
        var previous = 0.0;
        for (var level = 1; level <= maximum; level++)
        {
            var current = EnhanceEffects.Amount(EnhancePath.Recoil, level, tuning);
            Assert.True(double.IsFinite(current));
            Assert.True(current > previous);
            previous = current;
        }
        Assert.Equal(tuning.RecoilCap, previous, 10);
        Assert.Equal(previous, EnhanceEffects.Amount(EnhancePath.Recoil, 100, tuning), 10);
    }

    [Fact]
    public void Evolution_bonus_requires_a_separate_success_and_survives_later_level_changes()
    {
        var registry = new EffectRegistry();
        foreach (var path in new[] { EnhancePath.Recoil, EnhancePath.Ergonomics })
            registry.ApplyReceipt(new() { Success = true, ItemId = "gun", Path = path, Level = 10 }, "a");
        Assert.False(registry.IsComplete("gun"));
        Assert.False(registry.IsEvolved("gun"));
        Assert.Equal(15, registry.Amount("gun", EnhancePath.Recoil));
        registry.ApplyReceipt(new() { Success = true, ItemId = "gun", Path = EnhancePath.Precision, Level = 10 }, "a");
        Assert.True(registry.IsComplete("gun"));
        Assert.False(registry.IsEvolved("gun"));
        Assert.Equal(15, registry.Amount("gun", EnhancePath.Recoil));
        registry.ApplyReceipt(new() { Success = true, ItemId = "gun", IsEvolution = true, Evolved = true }, "a");
        Assert.True(registry.IsEvolved("gun"));
        Assert.Equal(30, registry.Amount("gun", EnhancePath.Recoil));
        Assert.Equal(20, registry.Amount("gun", EnhancePath.Ergonomics));
        Assert.Equal(20, registry.Amount("gun", EnhancePath.Precision));
        Assert.Equal("★★", registry.Badge("gun"));
        registry.ApplyReceipt(new() { ItemId = "gun", Path = EnhancePath.Precision, Level = 9, FailureMode = FailureMode.Downgrade, Evolved = true }, "a");
        Assert.False(registry.IsComplete("gun"));
        Assert.True(registry.IsEvolved("gun"));
        Assert.Equal(30, registry.Amount("gun", EnhancePath.Recoil));
        Assert.Equal(10, registry.Find("gun", EnhancePath.Recoil)!.Level);
        Assert.Equal(9, registry.Find("gun", EnhancePath.Precision)!.Level);
        Assert.Equal("★★", registry.Badge("gun"));
        registry.ApplyReceipt(new() { ItemId = "gun", Path = EnhancePath.Precision, Level = 0, FailureMode = FailureMode.Downgrade, Evolved = true }, "a");
        Assert.Null(registry.Find("gun", EnhancePath.Precision));
        Assert.NotNull(registry.Find("gun", EnhancePath.Recoil));
    }

    [Fact]
    public void Armor_completion_tuning_and_profile_restore_use_both_tracks_and_safety_caps()
    {
        var registry = new EffectRegistry();
        var snapshot = new EnhanceSnapshot { ProfileId = "a", Items = [
            new() { ItemId = "vest", Path = EnhancePath.ArmorDurability, Level = 10, Evolved = true },
            new() { ItemId = "vest", Path = EnhancePath.Mobility, Level = 10, Evolved = true }] };
        registry.Replace(snapshot, "a");
        Assert.True(registry.IsComplete("vest"));
        Assert.Equal(60, registry.Amount("vest", EnhancePath.ArmorDurability));
        Assert.Equal(40, registry.Amount("vest", EnhancePath.Mobility));
        registry.Configure(new() { CompletionBonusPercent = 100, ArmorDurabilityPerLevel = 10, ArmorDurabilityCap = 80 });
        Assert.Equal(80, registry.Amount("vest", EnhancePath.ArmorDurability));
        registry.Configure(new() { MaxLevel = 11 });
        Assert.False(registry.IsComplete("vest"));
        registry.Clear();
        Assert.False(registry.IsComplete("vest"));
    }
    [Theory]
    [InlineData(EnhancePath.Recoil, 30, 45)]
    [InlineData(EnhancePath.Ergonomics, 20, 30)]
    [InlineData(EnhancePath.Precision, 20, 30)]
    [InlineData(EnhancePath.ArmorDurability, 60, 80)]
    [InlineData(EnhancePath.Mobility, 40, 60)]
    public void Star_defaults_double_and_triple_each_base_effect_with_existing_safety_caps(EnhancePath path, double second, double third)
    {
        var tuning = new EffectTuning(); tuning.Validate();
        Assert.Equal(second, EnhanceEffects.Amount(path, 10, tuning, 2));
        Assert.Equal(third, EnhanceEffects.Amount(path, 10, tuning, 3));
        Assert.Equal(80, EnhanceEffects.Amount(EnhancePath.ArmorDurability, 10, new() { ThirdStarBonusPercent = 300 }, 3));
        Assert.Equal(50, EnhanceEffects.Amount(EnhancePath.Ergonomics, 10, new() { ErgonomicsPerLevel = 10, ErgonomicsCap = 50 }, 3));
    }

    [Theory]
    [InlineData(2, 20, 100)]
    [InlineData(3, 35, 200)]
    [InlineData(2, 35, 35)]
    [InlineData(3, 20, 20)]
    [InlineData(2, 70, 70)]
    [InlineData(3, 300, 300)]
    public void First_upgrade_replaces_only_old_shipped_bonus_defaults_and_preserves_custom_tuning(int stars, double old, double expected)
    {
        Assert.Equal(expected, StarEvolution.UpgradeLegacyBonus(stars, old));
    }

}
