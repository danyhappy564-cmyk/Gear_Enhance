#nullable enable
using System;

namespace GearEnhance;

// Korean is the source text. The client picks English from F12; the server always
// keeps Korean in its own logs and also returns the English text of its errors.
public static class Loc
{
    public const string EnglishKey = "GearEnhance.English";
    private static volatile bool english;
    public static bool English { get => english; set => english = value; }

    public static string L(string korean, string englishText) => english ? englishText : korean;

    public static ArgumentException Argument(string korean, string englishText) =>
        Tag(new ArgumentException(L(korean, englishText)), englishText);

    public static InvalidOperationException Invalid(string korean, string englishText) =>
        Tag(new InvalidOperationException(L(korean, englishText)), englishText);

    public static string? EnglishOf(Exception ex) => ex.Data[EnglishKey] as string;

    private static T Tag<T>(T ex, string englishText) where T : Exception
    {
        ex.Data[EnglishKey] = englishText;
        return ex;
    }

    // "2차" in Korean, "2nd" in English.
    public static string Tier(int level) => english
        ? level + (level % 100 is 11 or 12 or 13 ? "th" : (level % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" })
        : level + "차";

    // Enhancement insurance tier names. The server mail passes english explicitly.
    public static string TierName(InsuranceTier tier, bool inEnglish) => tier switch
    {
        InsuranceTier.Basic => inEnglish ? "Basic" : "일반",
        InsuranceTier.Advanced => inEnglish ? "Advanced" : "고급",
        InsuranceTier.Premium => inEnglish ? "Premium" : "프리미엄",
        _ => inEnglish ? "None" : "없음"
    };
    public static string TierName(InsuranceTier tier) => TierName(tier, english);
    // One letter for the stash cell badge; X when not insured.
    public static string TierLetter(InsuranceTier tier) => tier switch
    {
        InsuranceTier.Premium => "P",
        InsuranceTier.Advanced => L("고", "A"),
        InsuranceTier.Basic => L("일", "B"),
        _ => "X"
    };

    // Price basis labels come from the server as Korean data, not as errors.
    public static string PriceSource(string? source) => source switch
    {
        "SPT 플리 기준가" => L(source, "SPT flea price"),
        "북밸류 (플리 가격 없음)" => L(source, "Handbook (no flea price)"),
        "북밸류" => L(source, "Handbook"),
        null => "",
        _ => source
    };
}
