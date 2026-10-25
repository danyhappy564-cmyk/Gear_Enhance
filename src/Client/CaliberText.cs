using System;
using System.Collections.Generic;

namespace GearEnhance;

// The weapon's caliber beside its name in the window (mockup "Daewoo K2  5.56x45 NATO"). Unity-free for tests.
internal static class CaliberText
{
    // "Caliber556x45NATO" → "5.56x45 NATO". Unknown shapes keep their digits.
    internal static string Name(string caliber)
    {
        if (string.IsNullOrEmpty(caliber)) return null;
        var text = caliber.StartsWith("Caliber", StringComparison.Ordinal) ? caliber.Substring(7) : caliber;
        var digits = 0;
        while (digits < text.Length && char.IsDigit(text[digits])) digits++;
        if (digits == 0) return text;
        var number = text.Substring(0, digits);
        var known = new Dictionary<string, string> { ["556"] = "5.56", ["545"] = "5.45", ["762"] = "7.62", ["127"] = "12.7", ["1143"] = ".45",
            ["86"] = "8.6", ["68"] = "6.8", ["57"] = "5.7", ["46"] = "4.6", ["366"] = ".366", ["93"] = "9.3", ["338"] = ".338" };
        var rest = text.Substring(digits);
        var size = "";
        if (rest.StartsWith("x", StringComparison.Ordinal))
        {
            var end = 1;
            while (end < rest.Length && char.IsDigit(rest[end])) end++;
            size = rest.Substring(0, end);
            rest = rest.Substring(end);
        }
        return (known.TryGetValue(number, out var name) ? name : number) + size + (rest.Length == 0 ? "" : char.IsUpper(rest[0]) ? " " + rest : rest);
    }

}
