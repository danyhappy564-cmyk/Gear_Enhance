namespace GearEnhance;

public static class StarEvolution
{
    public const int Maximum = 3;
    public const double SecondDefaultBonus = 100;
    public const double ThirdDefaultBonus = 200;
    public static double UpgradeLegacyBonus(int stars, double value) => stars == 2 && value == 20 ? SecondDefaultBonus
        : stars == 3 && value == 35 ? ThirdDefaultBonus : value;
    // Before StarLevel was stored, Evolved=true represented exactly two stars.
    public static int Level(bool evolved, int stored) => stored == 3 ? 3 : evolved || stored == 2 ? 2 : 1;
    public static string Stars(int level) => level >= 3 ? "★★★" : level == 2 ? "★★" : "★";
    public static string ColorName(int level) => level >= 3 ? Loc.L("빨간색", "red") : level == 2 ? Loc.L("보라색", "purple") : Loc.L("금색", "gold");
    public static string ColorHex(int level) => level >= 3 ? "#F06A6A" : level == 2 ? "#B989F5" : "#D9B66F";
    public static double Bonus(int level, EffectTuning tuning) => level >= 3 ? tuning.ThirdStarBonusPercent
        : level == 2 ? tuning.CompletionBonusPercent : 0;
}
