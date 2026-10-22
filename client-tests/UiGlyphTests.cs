using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

public sealed class UiGlyphTests
{
    [Theory]
    [InlineData(EnhancePath.Recoil)]
    [InlineData(EnhancePath.Ergonomics)]
    [InlineData(EnhancePath.Precision)]
    [InlineData(EnhancePath.ArmorDurability)]
    [InlineData(EnhancePath.Mobility)]
    public void Baked_icon_is_nonempty_bounded_and_deterministic(EnhancePath path)
    {
        var icon = UiGlyphs.Rasterize(path);
        Assert.Equal(48 * 48, icon.Length);
        Assert.Equal(icon, UiGlyphs.Rasterize(path));
        Assert.InRange(icon.Count(x => x > 0), 100, 1600);
        Assert.Contains(icon, x => x > 0 && x < 255); // anti-aliased edges
        Assert.Equal(0, icon[0]);
        Assert.Equal(0, icon[^1]);
    }
    [Fact]
    public void Attempt_success_and_failure_select_distinct_native_sound_cues()
    {
        Assert.Equal("MenuInstallModFunc", GameFeedback.SoundName(FeedbackCue.Attempt));
        Assert.Equal("RepairComplete", GameFeedback.SoundName(FeedbackCue.Success));
        Assert.Equal("ErrorMessage", GameFeedback.SoundName(FeedbackCue.Failure));
    }
}
