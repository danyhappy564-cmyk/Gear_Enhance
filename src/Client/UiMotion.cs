using System;

namespace GearEnhance;

// 1.4.0 window motion (design 1, 6; mockup CSS) as pure functions of the seconds since the
// animation started, so IMGUI only offsets, scales or tints what it draws: no per-frame objects.
// Timing follows the mockup's keyframes and cubic-bezier curves (gear-enhance-1.4.0.html 107-185).
// With `reduced` (F12 "연출 줄이기") everything is at its end state at once.
internal static class UiMotion
{
    // Status panel: slides in from 22 px to the left over 0.28 s (ease-out).
    internal const double PanelTime = 0.28;
    // Stat rows: drop from 30 px above with a small bounce, 0.46 s. Base rows start at
    // 0.14 s, 55 ms apart; the enhanced rows at 0.52 s, 70 ms apart.
    internal const double RowTime = 0.46;
    // Effect and chance bars fill from the left over 0.75 s.
    internal const double FillTime = 0.75;
    // Popups grow from 92 % over 0.3 s; they never start transparent (design 6).
    internal const double PopTime = 0.3;

    internal static double BaseRowDelay(int index) => 0.14 + Math.Max(0, index) * 0.055;
    internal static double EnhancedRowDelay(int index) => 0.52 + Math.Max(0, index) * 0.07;

    internal static (float Offset, float Alpha) Panel(double elapsed, bool reduced)
    {
        if (reduced || elapsed >= PanelTime) return (0, 1);
        var e = EaseOut(Clamp(elapsed / PanelTime));
        return ((float)(-22 * (1 - e)), (float)e);
    }

    // dropA: 0 % (-30 px, hidden) → 65 % (+4 px, shown) → 100 % (0), cubic-bezier(.2,.9,.3,1.3) per step.
    internal static (float Offset, float Alpha) Row(double elapsed, double delay, bool reduced)
    {
        if (reduced) return (0, 1);
        var t = (elapsed - delay) / RowTime;
        if (t <= 0) return (-30, 0);
        if (t >= 1) return (0, 1);
        if (t < 0.65)
        {
            var e = Bezier(0.2, 0.9, 0.3, 1.3, t / 0.65);
            return ((float)(-30 + 34 * e), (float)Math.Min(1, Math.Max(0, e)));
        }
        return ((float)(4 * (1 - Bezier(0.2, 0.9, 0.3, 1.3, (t - 0.65) / 0.35))), 1);
    }

    // Bars: scaleX 0 → 1, cubic-bezier(.25,.8,.3,1), after `delay`.
    internal static float Fill(double elapsed, double delay, bool reduced) =>
        reduced ? 1 : (float)Bezier(0.25, 0.8, 0.3, 1, (elapsed - delay) / FillTime);

    // popIn: scale .92 → 1, cubic-bezier(.2,.9,.3,1.2) (overshoots a little).
    internal static float PopScale(double elapsed, bool reduced) =>
        reduced || elapsed >= PopTime ? 1 : (float)(0.92 + 0.08 * Bezier(0.2, 0.9, 0.3, 1.2, elapsed / PopTime));

    // mythIn: 0 % scale .45 rotate -5° → 60 % scale 1.07 → 100 % none, cubic-bezier(.2,.9,.25,1.25) per step.
    internal static (float Scale, float Angle) MythIn(double elapsed, bool reduced)
    {
        var t = elapsed / 0.75;
        if (reduced || t >= 1) return (1, 0);
        if (t <= 0) return (0.45f, -5);
        if (t < 0.6)
        {
            var e = Bezier(0.2, 0.9, 0.25, 1.25, t / 0.6);
            return ((float)(0.45 + 0.62 * e), (float)(-5 * (1 - e)));
        }
        return ((float)(1.07 - 0.07 * Bezier(0.2, 0.9, 0.25, 1.25, (t - 0.6) / 0.4)), 0);
    }

    // ??? question, confirmHit: 1.22 s in, 0.45 s: scale 1 → 1.035 (30 %) → 1, ease per step.
    internal static float ConfirmHit(double elapsed, bool reduced)
    {
        var t = (elapsed - 1.22) / 0.45;
        if (reduced || t <= 0 || t >= 1) return 1;
        return t < 0.3 ? (float)(1 + 0.035 * Ease(t / 0.3)) : (float)(1.035 - 0.035 * Ease((t - 0.3) / 0.7));
    }

    // shake: 0.5 s from 0.04 s through 0, -10, 8, -6, 3, 0 px (ease per 20 % step).
    private static readonly double[] ShakeAt = { 0, -10, 8, -6, 3, 0 };
    internal static float Shake(double elapsed, bool reduced)
    {
        var t = (elapsed - 0.04) / 0.5;
        if (reduced || t <= 0 || t >= 1) return 0;
        var step = Math.Min(4, (int)(t * 5));
        var local = Ease(t * 5 - step);
        return (float)(ShakeAt[step] + (ShakeAt[step + 1] - ShakeAt[step]) * local);
    }

    // A one-time glow (0..1): rises to its peak at `peak` of `time` after `start`, then fades,
    // ease-out per step (titleGlow 12 %, qHit 25 %).
    internal static float Glow(double elapsed, double start, double time, double peak, bool reduced)
    {
        var t = (elapsed - start) / time;
        if (reduced || t <= 0 || t >= 1) return 0;
        return t < peak ? (float)EaseOut(t / peak) : (float)(1 - EaseOut((t - peak) / (1 - peak)));
    }

    // Status "after" values, glowA: none until 40 %, 12 px blue at 60 %, gone at 100 % (1.2 s, ease-out).
    internal static float ValueGlow(double elapsed, bool reduced)
    {
        var t = elapsed / 1.2;
        if (reduced || t <= 0.4 || t >= 1) return 0;
        return t < 0.6 ? (float)EaseOut((t - 0.4) / 0.2) : (float)(1 - EaseOut((t - 0.6) / 0.4));
    }

    // effIn: the new value drops 8 px into place, 0.5 s from 0.35 s (ease-out).
    internal static float EffectDrop(double elapsed, bool reduced) =>
        reduced ? 0 : (float)(8 * (1 - EaseOut(Clamp((elapsed - 0.35) / 0.5))));

    // starIn: 0.9 s from 0.06 s: scale 0 / -200° / hidden → 70 % 1.35 / 15° / shown → 100 % none.
    internal static (float Scale, float Angle, float Alpha) StarIn(double elapsed, bool reduced)
    {
        var t = (elapsed - 0.06) / 0.9;
        if (reduced || t >= 1) return (1, 0, 1);
        if (t <= 0) return (0, -200, 0);
        if (t < 0.7)
        {
            var e = Bezier(0.2, 0.9, 0.3, 1.3, t / 0.7);
            return ((float)Math.Max(0, 1.35 * e), (float)(-200 + 215 * e), (float)Math.Min(1, Math.Max(0, e)));
        }
        var f = Bezier(0.2, 0.9, 0.3, 1.3, (t - 0.7) / 0.3);
        return ((float)(1.35 - 0.35 * f), (float)(15 - 15 * f), 1);
    }

    // mythGlow on the popup: 0 → 1 → 0 (ease-in-out per half). Mythic: 2.4 s from 0.75 s, forever.
    // Level-up and reroll: 1.2 s from 0.3 s, twice. -1 = no glow (before it starts or after).
    internal static float MythPulse(double elapsed, bool lite, bool reduced)
    {
        var start = lite ? 0.3 : 0.75;
        var period = lite ? 1.2 : 2.4;
        if (reduced || elapsed < start || lite && elapsed >= start + 2 * period) return -1;
        var t = (elapsed - start) / period % 1;
        return t < 0.5 ? (float)EaseInOut(t / 0.5) : (float)(1 - EaseInOut((t - 0.5) / 0.5));
    }

    // fxFlash: white .9 → 0 over 0.8 s (ease-out).
    internal static float Flash(double elapsed) => (float)(0.9 * (1 - EaseOut(Clamp(elapsed / 0.8))));

    // fxRays: fade in over 0.9 s (ease-out), one turn every 16 s.
    internal static (float Alpha, float Angle) Rays(double elapsed) =>
        ((float)EaseOut(Clamp(elapsed / 0.9)), (float)(elapsed / 16 % 1 * 360));

    // fxRing: scale .2 → `scale`, opacity `from` → 0, cubic-bezier(.2,.7,.3,1).
    internal static (float Scale, float Alpha) Ring(double elapsed, double delay, double time, float scale, float from)
    {
        var t = (elapsed - delay) / time;
        if (t <= 0 || t >= 1) return (0, 0);
        var e = Bezier(0.2, 0.7, 0.3, 1, t);
        return ((float)(0.2 + (scale - 0.2) * e), (float)(from * (1 - e)));
    }

    // fxShoot: translateX `from` → `reach` and scaleX .3 → 1 over `time` after `delay`; opacity
    // 0 → 1 at `peak` → 0. cubic-bezier(.15,.7,.3,1) per step.
    internal static (float Distance, float Stretch, float Alpha) Spark(double elapsed, double delay, double time, float from, float reach, double peak)
    {
        var t = (elapsed - delay) / time;
        if (t <= 0 || t >= 1) return (0, 0, 0);
        var e = Bezier(0.15, 0.7, 0.3, 1, t);
        var alpha = t < peak ? Bezier(0.15, 0.7, 0.3, 1, t / peak) : 1 - Bezier(0.15, 0.7, 0.3, 1, (t - peak) / (1 - peak));
        return ((float)(from + (reach - from) * e), (float)(0.3 + 0.7 * e), (float)Math.Max(0, Math.Min(1, alpha)));
    }

    // Destroy pieces: 0.95 s from 0.06 s, cubic-bezier(.2,.7,.3,1).
    internal static float Shatter(double elapsed, bool reduced) =>
        reduced ? 1 : (float)Bezier(0.2, 0.7, 0.3, 1, (elapsed - 0.06) / 0.95);

    internal static double Clamp(double t) => t <= 0 || double.IsNaN(t) ? 0 : t >= 1 ? 1 : t;
    private static double EaseOut(double t) => Bezier(0, 0, 0.58, 1, t);
    private static double Ease(double t) => Bezier(0.25, 0.1, 0.25, 1, t);
    private static double EaseInOut(double t) => Bezier(0.42, 0, 0.58, 1, t);

    // CSS cubic-bezier(x1, y1, x2, y2) at progress t (0..1): solve x(s) = t, return y(s).
    internal static double Bezier(double x1, double y1, double x2, double y2, double t)
    {
        t = Clamp(t);
        if (t <= 0 || t >= 1) return t;
        var s = t;
        for (var step = 0; step < 8; step++)
        {
            var error = Curve(x1, x2, s) - t;
            if (Math.Abs(error) < 1e-6) return Curve(y1, y2, s);
            var slope = Slope(x1, x2, s);
            if (Math.Abs(slope) < 1e-6) break;
            s = Math.Min(1, Math.Max(0, s - error / slope));
        }
        double low = 0, high = 1;
        s = t;
        for (var step = 0; step < 32; step++)
        {
            var x = Curve(x1, x2, s);
            if (Math.Abs(x - t) < 1e-6) break;
            if (x < t) low = s; else high = s;
            s = (low + high) / 2;
        }
        return Curve(y1, y2, s);
    }
    private static double Curve(double a, double b, double s) => ((1 + 3 * a - 3 * b) * s + (3 * b - 6 * a)) * s * s + 3 * a * s;
    private static double Slope(double a, double b, double s) => 3 * (1 + 3 * a - 3 * b) * s * s + 2 * (3 * b - 6 * a) * s + 3 * a;
}
