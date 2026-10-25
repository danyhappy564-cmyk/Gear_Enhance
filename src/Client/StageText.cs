using System.Collections.Generic;

namespace GearEnhance;

// User 2026-10-26: the stash and equipment cell tag shows the stages as Roman numerals ("신화 V · 초월 X");
// the enhancement window keeps numbers ("4 / 5": "V / V" read badly there). No Roman zero: a fresh mythic shows 0.
internal static class StageText
{
    private static readonly string[] Numerals = { "0", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

    internal static string Roman(int value) => value >= 0 && value < Numerals.Length ? Numerals[value] : value.ToString();

    // The window's "4 / 5".
    internal static string Of(int level, int max) => level + " / " + max;

    // Stash and equipment cells: "[신화 V · 초월 X]" (transcendence = both tracks added), or only
    // the part the gear has; empty for plain gear.
    internal static string Tag(int mythic, int second, int third)
    {
        var parts = new List<string>();
        if (mythic >= 0) parts.Add("<color=#FF6AD5>" + Loc.L("신화 ", "Mythic ") + Roman(mythic) + "</color>");
        if (second + third > 0) parts.Add("<color=#40D1D9>" + Loc.L("초월 ", "Trans. ") + Roman(second + third) + "</color>");
        if (parts.Count == 0) return "";
        return "<color=#9AA1A8>[</color>" + string.Join("<color=#9AA1A8> · </color>", parts) + "<color=#9AA1A8>]</color>";
    }
}
