using System;

namespace GearEnhance;

// 1.4.0 window motion (design 1, 6; mockup CSS) as pure functions of the seconds since the
// animation started, so IMGUI only offsets what it draws: no textures, no per-frame objects.
// With `reduced` (F12 "연출 줄이기") everything is at its end state at once.
internal static class UiMotion
{
    // Status panel: slides in from 22 px to the left over 0.28 s.
    internal const double PanelTime = 0.28;
    // Stat rows: each drops from 30 px above with a small bounce, 0.46 s, 60 ms apart.
    internal const double RowTime = 0.46, RowGap = 0.06;
    // Effect and chance bars fill from the left over 0.75 s.
    internal const double FillTime = 0.75;
    // Popups grow from 92 % over 0.3 s; they never start transparent (design 6).
    internal const double PopTime = 0.3;

    internal static (float Offset, float Alpha) Panel(double elapsed, bool reduced)
    {
        if (reduced || elapsed >= PanelTime) return (0, 1);
        var t = Clamp(elapsed / PanelTime);
        return ((float)(-22 * (1 - EaseOut(t))), (float)t);
    }

    internal static (float Offset, float Alpha) Row(double elapsed, int index, bool reduced)
    {
        if (reduced) return (0, 1);
        var t = (elapsed - Math.Max(0, index) * RowGap) / RowTime;
        if (t <= 0) return (-30, 0);
        if (t >= 1) return (0, 1);
        // Down to 4 px past the line at 65 %, then back up onto it.
        return t < 0.65 ? ((float)(-30 + 34 * EaseOut(t / 0.65)), (float)Math.Min(1, t / 0.65))
            : ((float)(4 * (1 - EaseOut((t - 0.65) / 0.35))), 1);
    }

    internal static float Fill(double elapsed, double delay, bool reduced) =>
        reduced ? 1 : (float)EaseOut(Clamp((elapsed - delay) / FillTime));

    internal static float PopScale(double elapsed, bool reduced) =>
        reduced || elapsed >= PopTime ? 1 : (float)(0.92 + 0.08 * EaseOutBack(Clamp(elapsed / PopTime)));

    // Destroy popups shake once, 0.5 s from 0.04 s (in time with the destroy sound's peak).
    internal static float Shake(double elapsed, bool reduced)
    {
        var t = (elapsed - 0.04) / 0.5;
        if (reduced || t <= 0 || t >= 1) return 0;
        return (float)(Math.Sin(t * Math.PI * 5) * 10 * (1 - t));
    }

    // A one-time glow peak (0..1) around `at` seconds, for titles and changed stat values.
    internal static float Glow(double elapsed, double at, double width, bool reduced)
    {
        if (reduced || width <= 0) return 0;
        var d = Math.Abs(elapsed - at) / width;
        return d >= 1 ? 0 : (float)(1 - d * d);
    }

    // Mythic rings: grow from 20 % to `scale`, fading out, over `time` after `delay`.
    internal static (float Scale, float Alpha) Ring(double elapsed, double delay, double time, float scale)
    {
        var t = (elapsed - delay) / time;
        if (t <= 0 || t >= 1) return (0, 0);
        return ((float)(0.2 + (scale - 0.2) * EaseOut(t)), (float)(1 - t));
    }

    // Sparks shoot outward: distance from the centre and alpha, over `time` after `delay`.
    internal static (float Distance, float Alpha) Spark(double elapsed, double delay, double time, float reach)
    {
        var t = (elapsed - delay) / time;
        if (t <= 0 || t >= 1) return (0, 0);
        return ((float)(20 + (reach - 20) * EaseOut(t)), (float)(t < 0.12 ? t / 0.12 : 1 - (t - 0.12) / 0.88));
    }

    internal static double Clamp(double t) => t <= 0 || double.IsNaN(t) ? 0 : t >= 1 ? 1 : t;
    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    private static double EaseOutBack(double t)
    {
        const double c = 1.70158;
        return 1 + (c + 1) * Math.Pow(t - 1, 3) + c * Math.Pow(t - 1, 2);
    }
}
