using System.Text.RegularExpressions;
using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

// 1.4.1 Russian, Chinese and player files (LangFiles + Loc). Loc is process-wide; LanguageTests turns
// parallel runs off for the assembly.
public sealed class TranslationTests : IDisposable
{
    public void Dispose() => Loc.English = false;

    private static readonly Regex Hole = new Regex(@"\{(\d+)[^}]*\}");

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GearEnhance.sln"))) directory = directory.Parent;
        return directory!.FullName;
    }

    [Fact]
    public void Built_in_russian_and_chinese_fill_plain_and_interpolated_text()
    {
        Loc.Set(UiLang.Russian, LangFiles.Load(UiLang.Russian, null!, null!, out var note));
        Assert.Null(note);
        Assert.Equal("Закрыть", Loc.L("닫기", "Close"));
        var level = 3;
        Assert.Equal("Сейчас 3", Loc.F($"지금 {level}", $"Now {level}"));
        var paid = 1234567;
        Assert.Equal(" · потрачено " + paid.ToString("N0") + " ₽", Loc.F($" · {paid:N0} ₽ 소모", $" · spent {paid:N0} ₽"));
        // Unknown text shows English, never Korean.
        Assert.Equal("Fallback", Loc.L("표에 없는 문구", "Fallback"));
        Assert.Equal("A", Loc.TierLetter(InsuranceTier.Advanced));
        Assert.Equal("Расширенная", Loc.TierName(InsuranceTier.Advanced));

        Loc.Set(UiLang.Chinese, LangFiles.Load(UiLang.Chinese, null!, null!, out _));
        Assert.Equal("关闭", Loc.L("닫기", "Close"));
        Assert.True(LangFiles.NeedsCjkFont(UiLang.Chinese, new Dictionary<string, string>()));
        Assert.False(LangFiles.NeedsCjkFont(UiLang.Russian, new Dictionary<string, string>()));
        // F12 text follows the table; developer-only rows ("-") stay English.
        Assert.Equal("强化音效", ConfigText.Get("강화 효과음", true));
        Assert.Equal("No roubles for anything", ConfigText.Get("모든 강화 루블 필요 없음", true));
    }

    [Fact]
    public void Server_errors_use_the_key_and_preformatted_arguments()
    {
        var max = 2;
        var error = Loc.InvalidF($"성공률 재료는 {max}종까지 고를 수 있습니다.", $"Choose at most {max} kinds of chance material.");
        Assert.Equal("성공률 재료는 {0}종까지 고를 수 있습니다.", Loc.KeyOf(error));
        Assert.Equal(new[] { "2" }, Loc.ArgsOf(error));
        var cost = 5000000;
        Assert.Equal(new[] { cost.ToString("N0") }, Loc.ArgsOf(Loc.InvalidF($"{cost:N0} ₽", $"{cost:N0} ₽")));
        Loc.Set(UiLang.Russian, LangFiles.Load(UiLang.Russian, null!, null!, out _));
        Assert.Equal("Можно выбрать не больше 2 видов материалов шанса.", Loc.Error(Loc.KeyOf(error), Loc.ArgsOf(error), "en"));
        // An old server sends no key: its English text stays.
        Assert.Equal("en", Loc.Error(null, null, "en"));
    }

    [Fact]
    public void A_player_file_overrides_rows_and_a_custom_file_stands_alone()
    {
        var plugin = Path.Combine(Path.GetTempPath(), "ge-lang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(plugin, LangFiles.Folder));
        try
        {
            File.WriteAllText(Path.Combine(plugin, LangFiles.Folder, "ru.tsv"),
                "﻿# note\nko\ten\ttext\n닫기\tClose\tЗакрой\n취소\tCancel\t\n두 줄\tTwo\tA\\nB\n");
            var rows = LangFiles.Load(UiLang.Russian, plugin, null!, out var note);
            Assert.Null(note);
            Assert.Equal("Закрой", rows["닫기"]);
            Assert.Equal("Отмена", rows["취소"]);
            Assert.Equal("A\nB", rows["두 줄"]);

            File.WriteAllText(Path.Combine(plugin, LangFiles.Folder, "de.tsv"), "ko\ten\ttext\n닫기\tClose\tSchließen\n");
            var custom = LangFiles.Load(UiLang.Custom, plugin, "../de.tsv", out note);
            Assert.Null(note);
            Assert.Equal("Schließen", custom["닫기"]);
            Assert.False(custom.ContainsKey("취소"));
            Assert.False(LangFiles.NeedsCjkFont(UiLang.Custom, custom));
            LangFiles.Load(UiLang.Custom, plugin, "missing.tsv", out note);
            Assert.NotNull(note);
        }
        finally { Directory.Delete(plugin, true); }
    }

    [Fact]
    public void Every_table_row_keeps_the_korean_placeholders()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "src", "Client", "Lang", "strings.tsv"));
        var korean = LangFiles.Parse(text, 0);
        Assert.True(korean.Count > 800, korean.Count.ToString());
        foreach (var column in new[] { 2, 3 })
        foreach (var row in LangFiles.Parse(text, column))
        {
            var holes = Hole.Matches(row.Value).Select(m => int.Parse(m.Groups[1].Value)).ToList();
            var count = Hole.Matches(row.Key).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1;
            Assert.True(holes.All(index => index < count), column + ": " + row.Key + " → " + row.Value);
            Assert.Equal(row.Key.StartsWith(' '), row.Value.StartsWith(' '));
        }
    }

    [Fact]
    public void Player_files_ship_beside_the_dll()
    {
        foreach (var file in new[] { "ru.tsv", "zh.tsv", "template.tsv" })
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, LangFiles.Folder, file)), file);
        var targets = File.ReadAllText(Path.Combine(Root(), "tools", "Packaging.targets"));
        Assert.Contains("BepInEx/plugins/GearEnhance/lang", targets);
    }

    [Fact]
    public void Insurance_mark_settings_choose_what_shows()
    {
        try
        {
            InsuranceMarks.Shape = InsuranceMark.Corner; InsuranceMarks.ShowUninsured = false;
            Assert.True(InsuranceMarks.Marked(InsuranceTier.Basic));
            Assert.False(InsuranceMarks.Marked(InsuranceTier.None));
            Assert.Equal("", InsuranceMarks.NameTag(InsuranceTier.Premium));
            InsuranceMarks.ShowUninsured = true;
            Assert.True(InsuranceMarks.Marked(InsuranceTier.None));
            InsuranceMarks.Shape = InsuranceMark.NameTag;
            Assert.Contains("[P]", InsuranceMarks.NameTag(InsuranceTier.Premium));
            InsuranceMarks.Shape = InsuranceMark.Off;
            Assert.False(InsuranceMarks.Marked(InsuranceTier.Premium));
        }
        finally { InsuranceMarks.Shape = InsuranceMark.Corner; InsuranceMarks.ShowUninsured = false; }
    }
}
