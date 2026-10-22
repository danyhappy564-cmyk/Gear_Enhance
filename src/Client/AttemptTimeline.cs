using System;

namespace GearEnhance;

// Driven by the main-thread Update and a monotonic clock, never by a blocking wait.
internal sealed class AttemptTimeline
{
    internal const double Duration = 1;
    private double started;
    private bool submitted;
    internal bool Active { get; private set; }
    internal bool Begin(double now)
    {
        if (Active) return false;
        started = now; Active = true; submitted = false;
        return true;
    }
    internal double Fraction(double now) => Active ? Math.Max(0, Math.Min(1, (now - started) / Duration)) : 0;
    internal bool TryComplete(double now)
    {
        if (!Active || submitted || Fraction(now) < 1) return false;
        submitted = true;
        return true;
    }
    internal void Reset() { Active = false; submitted = false; }
}
