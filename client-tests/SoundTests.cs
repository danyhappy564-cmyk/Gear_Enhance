using System.Security.Cryptography;
using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

public sealed class SoundTests
{
    [Theory]
    [InlineData(0, 0.7f)]
    [InlineData(12000, 0.7f)]
    [InlineData(12000, 0f)]
    [InlineData(211765, 1f)]
    public void Ui_audio_recovery_keeps_remaining_stereo_frames_and_scales_only_our_clip(int offset, float volume)
    {
        var wave = EmbeddedSounds.Load(0);
        var first = wave.Samples[offset * wave.Channels];
        var remaining = wave.SamplesFrom(offset, volume);
        Assert.Equal((wave.Frames - offset) * wave.Channels, remaining.Length);
        for (var i = 0; i < remaining.Length; i++)
            Assert.Equal(wave.Samples[offset * wave.Channels + i] * volume, remaining[i]);
        Assert.Equal(first, wave.Samples[offset * wave.Channels]);
        Assert.Equal(wave.Duration - offset / (double)wave.SampleRate,
            remaining.Length / (double)wave.Channels / wave.SampleRate, 12);
    }

    [Fact]
    public void Ui_audio_recovery_rejects_invalid_offsets_and_volume_without_modifying_cached_pcm()
    {
        var wave = EmbeddedSounds.Load(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => wave.SamplesFrom(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wave.SamplesFrom(wave.Frames, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wave.SamplesFrom(0, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => wave.SamplesFrom(0, 1.1f));
        Assert.Same(wave.Samples, wave.SamplesFrom(0, 1));
    }

    [Theory]
    [InlineData(0, "charge", 211766, 48000, 2, "E614D77CA16F837821E4B50C502464CFB693BA5E5028645FEA36967DE03E140B")]
    [InlineData(1, "success", 44100, 44100, 2, "028E08883B7740DD9F881F56BAEE7348AC7BA4741F9B74CEAECE4494FFBB9DCB")]
    [InlineData(2, "failure", 108288, 48000, 2, "0207580EA45EA1A9B9C5B95E917955FC7F4BC76680878BAA1F3AFBE649275DF6")]
    [InlineData(3, "destroy", 51577, 48000, 1, "409E4225A5A81B7CDCE88E1398C27BACEBF72E3ACFD8FEBE2A9E717D0ABB2A51")]
    [InlineData(4, "star2", 183764, 44100, 2, "83EF2077A8FC4B4DF187A4DD9BEDD32AF9D89C8270443729D4835B4D121DA3B6")]
    [InlineData(5, "evolution_charge", 211766, 48000, 2, "13A0EC246A4EDD5FCD2E832CD4764F6BAC8C8720F9EF5B14F826848A385DDD07")]
    public void Actual_embedded_audio_matches_the_converted_user_files_and_preserves_sample_count(
        int index, string name, int frames, int rate, int channels, string sha256)
    {
        using var stream = typeof(GameFeedback).Assembly.GetManifestResourceStream("GearEnhance.Audio." + name + ".wav")!;
        Assert.NotNull(stream);
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(stream)));
        var sound = EmbeddedSounds.Load(index);
        Assert.Equal(frames, sound.Frames);
        Assert.Equal(rate, sound.SampleRate);
        Assert.Equal(channels, sound.Channels);
        Assert.Equal(frames * channels, sound.Samples.Length);
        Assert.Equal(frames / (double)rate, sound.Duration);
        Assert.True(sound.Samples.All(float.IsFinite));
        Assert.InRange(sound.Samples.Max(s => Math.Abs(s)), 0.05f, 1);
    }

    [Theory]
    [InlineData((int)FeedbackCue.Attempt, 0)]
    [InlineData((int)FeedbackCue.Success, 1)]
    [InlineData((int)FeedbackCue.Complete, 4)]
    [InlineData((int)FeedbackCue.EvolutionAttempt, 5)]
    [InlineData((int)FeedbackCue.Failure, 2)]
    [InlineData((int)FeedbackCue.Downgrade, 2)]
    [InlineData((int)FeedbackCue.Destroy, 3)]
    public void Each_outcome_selects_the_file_requested_by_the_user(int cue, int index) =>
        Assert.Equal(index, EmbeddedSounds.Index((FeedbackCue)cue));

    [Fact]
    public void Gauge_uses_the_actual_charge_audio_duration_and_scheduled_audio_clock()
    {
        var duration = EmbeddedSounds.Load(0).Duration;
        Assert.Equal(4.411791666666667, duration, 12);
        Assert.Equal(duration, EmbeddedSounds.Load(5).Duration);
        const double scheduledStart = 1000.05;
        var timeline = new AttemptTimeline();
        Assert.True(timeline.Begin(scheduledStart, duration));
        Assert.Equal(0, timeline.Fraction(1000));
        Assert.Equal(0.5, timeline.Fraction(scheduledStart + duration / 2), 10);
        Assert.False(timeline.TryComplete(scheduledStart + 1));
        Assert.False(timeline.Begin(scheduledStart + 1, 1));
        Assert.False(timeline.TryComplete(scheduledStart + duration - 0.000001));
        Assert.Equal(1, timeline.Fraction(scheduledStart + duration));
        Assert.True(timeline.TryComplete(scheduledStart + duration));
        Assert.False(timeline.TryComplete(scheduledStart + duration + 1));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(61)]
    public void Invalid_duration_cannot_leave_an_attempt_waiting_forever(double duration) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttemptTimeline().Begin(0, duration));

    [Fact]
    public void Broken_embedded_wav_is_rejected_before_creating_a_game_audio_clip()
    {
        using var original = typeof(GameFeedback).Assembly.GetManifestResourceStream("GearEnhance.Audio.success.wav")!;
        using var copy = new MemoryStream(); original.CopyTo(copy);
        var bytes = copy.ToArray();
        Assert.Throws<InvalidDataException>(() => WaveSound.Read(new MemoryStream(bytes[..^1])));
        bytes[34] = 24; // unsupported PCM sample width
        Assert.Throws<InvalidDataException>(() => WaveSound.Read(new MemoryStream(bytes)));
        Assert.Throws<InvalidDataException>(() => WaveSound.Read(new MemoryStream(new byte[44])));
    }

    [Fact]
    public void Sounds_follow_confirmed_outcomes_and_success_never_plays_a_destruction_cue()
    {
        Assert.Equal(FeedbackCue.Success, GameFeedback.Result(new() { Success = true, FailureMode = FailureMode.Destroy }, false));
        Assert.Equal(FeedbackCue.Complete, GameFeedback.Result(new() { Success = true }, true));
        Assert.Equal(FeedbackCue.Downgrade, GameFeedback.Result(new() { FailureMode = FailureMode.Downgrade }, true));
        Assert.Equal(FeedbackCue.Failure, GameFeedback.Result(new() { FailureMode = FailureMode.KeepItem }, false));
        Assert.Equal(FeedbackCue.Destroy, GameFeedback.Result(new() { FailureMode = FailureMode.Destroy }, false));
    }
}
