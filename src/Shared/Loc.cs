#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GearEnhance;

// Custom = a translation file a player made (BepInEx/plugins/GearEnhance/lang).
public enum UiLang { Korean, English, Russian, Chinese, Custom }

// Korean is the source text. The client picks the language from F12; the server always
// keeps Korean in its own logs and also returns the English text of its errors.
// 1.4.1: Russian and Chinese come from the client's table (src/Client/Lang/strings.tsv, keyed by the
// Korean text or, for interpolated text, its FormattableString.Format); a missing row shows English.
public static class Loc
{
    public const string EnglishKey = "GearEnhance.English";
    public const string KeyKey = "GearEnhance.Key";
    public const string ArgsKey = "GearEnhance.Args";
    private static volatile int language;
    private static volatile IReadOnlyDictionary<string, string>? table;
    private static readonly Regex Hole = new Regex(@"\{(\d+)(,[^:}]*)?(:[^}]*)?\}");

    public static UiLang Language => (UiLang)language;
    // True for every language but Korean: English is the base text the other two fall back to.
    public static bool English { get => language != (int)UiLang.Korean; set => Set(value ? UiLang.English : UiLang.Korean, null); }
    private static volatile int revision;
    // Changes with every language or table switch; caches of translated text key on it.
    public static int Revision => revision;
    // Rows of the current language (null for Korean and English).
    public static void Set(UiLang value, IReadOnlyDictionary<string, string>? rows)
    {
        table = value == UiLang.Korean || value == UiLang.English ? null : rows;
        language = (int)value;
        revision++;
    }
    // The current language's row for this Korean text, if the table has one.
    public static bool TryRow(string korean, out string text)
    {
        text = "";
        return table is { } rows && rows.TryGetValue(korean, out text!);
    }

    public static string L(string korean, string englishText)
    {
        if (language == (int)UiLang.Korean) return korean;
        return table is { } rows && rows.TryGetValue(korean, out var text) ? text : englishText;
    }

    // Interpolated text: the row is found by the Korean format ("지금 {0}") and filled with its arguments.
    public static string F(FormattableString korean, FormattableString englishText)
    {
        if (language == (int)UiLang.Korean) return korean.ToString();
        if (table is { } rows && rows.TryGetValue(korean.Format, out var format))
        {
            try { return string.Format(format, korean.GetArguments()); }
            catch (FormatException) { }
        }
        return englishText.ToString();
    }

    // A server error in the client's language: key and arguments from the server, else its English text.
    public static string Error(string? key, string[]? args, string englishText)
    {
        if (key == null || !(table is { } rows) || !rows.TryGetValue(key, out var format)) return englishText;
        try { return string.Format(format, args ?? Array.Empty<object>()); }
        catch (FormatException) { return englishText; }
    }

    public static ArgumentException Argument(string korean, string englishText) =>
        Tag(new ArgumentException(L(korean, englishText)), korean, Array.Empty<string>(), englishText);

    public static InvalidOperationException Invalid(string korean, string englishText) =>
        Tag(new InvalidOperationException(L(korean, englishText)), korean, Array.Empty<string>(), englishText);

    // Interpolated errors (C# would pick the string overloads above for $"...", hence the names).
    public static ArgumentException ArgumentF(FormattableString korean, FormattableString englishText) =>
        Tag(new ArgumentException(F(korean, englishText)), korean.Format, Arguments(korean), englishText.ToString());

    public static InvalidOperationException InvalidF(FormattableString korean, FormattableString englishText) =>
        Tag(new InvalidOperationException(F(korean, englishText)), korean.Format, Arguments(korean), englishText.ToString());

    public static string? EnglishOf(Exception ex) => ex.Data[EnglishKey] as string;
    public static string? KeyOf(Exception ex) => ex.Data[KeyKey] as string;
    public static string[]? ArgsOf(Exception ex) => ex.Data[ArgsKey] as string[];

    private static T Tag<T>(T ex, string key, string[] args, string englishText) where T : Exception
    {
        ex.Data[EnglishKey] = englishText;
        ex.Data[KeyKey] = key;
        ex.Data[ArgsKey] = args;
        return ex;
    }

    // Each argument already formatted the way the Korean text shows it ({0:N0} → "5,000,000"), so the
    // client fills any language's row with plain text.
    private static string[] Arguments(FormattableString text)
    {
        var values = text.GetArguments();
        var result = new string[values.Length];
        for (var index = 0; index < values.Length; index++) result[index] = Convert.ToString(values[index]) ?? "";
        foreach (Match hole in Hole.Matches(text.Format))
            if (int.TryParse(hole.Groups[1].Value, out var index) && index < values.Length)
                result[index] = string.Format("{0" + hole.Groups[3].Value + "}", values[index]);
        return result;
    }

    // Enhancement insurance tier names. The server mail passes english explicitly.
    public static string TierName(InsuranceTier tier, bool inEnglish) => tier switch
    {
        InsuranceTier.Basic => inEnglish ? "Basic" : "일반",
        InsuranceTier.Advanced => inEnglish ? "Advanced" : "고급",
        InsuranceTier.Premium => inEnglish ? "Premium" : "프리미엄",
        _ => inEnglish ? "None" : "없음"
    };
    public static string TierName(InsuranceTier tier) => tier switch
    {
        InsuranceTier.Basic => L("일반", "Basic"),
        InsuranceTier.Advanced => L("고급", "Advanced"),
        InsuranceTier.Premium => L("프리미엄", "Premium"),
        _ => L("없음", "None")
    };
    // One letter for the stash cell badge; X when not insured. Latin outside Korean: the game's
    // caption font may lack Cyrillic or Chinese glyphs.
    public static string TierLetter(InsuranceTier tier) => tier switch
    {
        InsuranceTier.Premium => "P",
        InsuranceTier.Advanced => English ? "A" : "고",
        InsuranceTier.Basic => English ? "B" : "일",
        _ => "X"
    };

    // Price basis labels come from the server as Korean data, not as errors.
    public static string PriceSource(string? source) => source switch
    {
        "SPT 플리 기준가" => L("SPT 플리 기준가", "SPT flea price"),
        "북밸류 (플리 가격 없음)" => L("북밸류 (플리 가격 없음)", "Handbook (no flea price)"),
        "북밸류" => L("북밸류", "Handbook"),
        null => "",
        _ => source
    };
}
