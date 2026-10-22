using System;

namespace GearEnhance;

// Update consumes completion once. The caller uses the audio DSP clock for a
// scheduled clip, or a monotonic clock when sound is disabled/unavailable.
internal sealed class AttemptTimeline
{
    internal double Duration { get; private set; } = 1;
    private double started;
    private bool submitted;
    internal bool Active { get; private set; }
    internal bool Begin(double now, double duration = 1)
    {
        if (Active) return false;
        if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0 || duration > 60)
            throw new ArgumentOutOfRangeException(nameof(duration));
        Duration = duration;
        started = now; Active = true; submitted = false;
        return true;
    }
    internal double Fraction(double now) => !Active ? 0 : now >= started + Duration
        ? 1 : Math.Max(0, (now - started) / Duration);
    internal bool TryComplete(double now)
    {
        if (!Active || submitted || Fraction(now) < 1) return false;
        submitted = true;
        return true;
    }
    // Wall-clock fallback when the scheduled audio clock stops advancing.
    internal bool ForceComplete()
    {
        if (!Active || submitted) return false;
        submitted = true;
        return true;
    }
    internal void Reset() { Active = false; submitted = false; }
}
