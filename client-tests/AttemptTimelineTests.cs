using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

public sealed class AttemptTimelineTests
{
    [Fact]
    public void A_full_second_is_required_and_fast_clicks_cannot_restart_or_double_submit()
    {
        var timeline = new AttemptTimeline();
        Assert.True(timeline.Begin(50));
        Assert.False(timeline.Begin(50.1));
        Assert.Equal(0, timeline.Fraction(49));
        Assert.Equal(0.5, timeline.Fraction(50.5));
        Assert.False(timeline.TryComplete(50.999));
        Assert.True(timeline.TryComplete(51));
        Assert.Equal(1, timeline.Fraction(100));
        for (var frame = 0; frame < 10000; frame++) Assert.False(timeline.TryComplete(100 + frame));
        timeline.Reset();
        Assert.False(timeline.Active);
        Assert.False(timeline.TryComplete(200));
        Assert.True(timeline.Begin(200));
        Assert.False(timeline.TryComplete(200.999));
        Assert.True(timeline.TryComplete(201));
    }

    [Fact]
    public void A_stalled_audio_clock_can_be_completed_once_by_the_wall_clock_fallback()
    {
        var timeline = new AttemptTimeline();
        Assert.False(timeline.ForceComplete());
        timeline.Begin(10, 4.4);
        Assert.False(timeline.TryComplete(10.5));
        Assert.True(timeline.ForceComplete());
        Assert.False(timeline.ForceComplete());
        Assert.False(timeline.TryComplete(100));
        timeline.Reset();
        Assert.False(timeline.ForceComplete());
    }

    [Fact]
    public void Closing_or_cancelling_the_window_discards_an_unsubmitted_attempt()
    {
        var timeline = new AttemptTimeline();
        timeline.Begin(0);
        timeline.Reset();
        Assert.False(timeline.TryComplete(1));
        Assert.Equal(0, timeline.Fraction(1));
    }
}
