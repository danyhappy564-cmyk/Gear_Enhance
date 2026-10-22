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
    public void Agreed_progression_and_caps_are_preserved(EnhancePath path, double step, double cap)
    {
        Assert.Equal(0, EnhanceEffects.Amount(path, 0, new()));
        Assert.Equal(step, EnhanceEffects.Amount(path, 1, new()));
        Assert.Equal(cap, EnhanceEffects.Amount(path, 10, new()));
        Assert.Equal(cap, EnhanceEffects.Amount(path, 100, new()));
    }

    [Fact]
    public void Mockup_four_to_five_recoil_values_are_exact()
    {
        Assert.Equal(6, EnhanceEffects.Amount(EnhancePath.Recoil, 4, new()));
        Assert.Equal(7.5, EnhanceEffects.Amount(EnhancePath.Recoil, 5, new()));
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
    public void Receipt_switches_specialization_and_destruction_clears_effects()
    {
        var registry = new EffectRegistry();
        registry.ApplyReceipt(new() { Success = true, ItemId = "gun", Level = 5, Path = EnhancePath.Recoil }, "a");
        Assert.Equal(7.5, registry.Amount("gun", EnhancePath.Recoil));
        registry.ApplyReceipt(new() { Success = true, ItemId = "gun", Level = 5, Path = EnhancePath.Ergonomics }, "a");
        Assert.Equal(0, registry.Amount("gun", EnhancePath.Recoil));
        Assert.Equal(5, registry.Amount("gun", EnhancePath.Ergonomics));
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
}
