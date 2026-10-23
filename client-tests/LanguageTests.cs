using System.Text.RegularExpressions;
using GearEnhance;
using Xunit;

// Loc.English is process-wide; Korean-text assertions elsewhere must not race it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GearEnhanceClientTests;

public sealed class LanguageTests : IDisposable
{
    public void Dispose() => Loc.English = false;

    private static string Source(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GearEnhance.sln"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Client", file));
    }

    [Fact]
    public void Every_f12_section_key_and_description_has_an_english_pair()
    {
        var awake = Source("Plugin.cs");
        var binds = Regex.Matches(awake, @"Bind(?:General|Developer)\((?<args>(?:[^;])*?)\);", RegexOptions.Singleline);
        Assert.True(binds.Count >= 25, binds.Count.ToString());
        var missing = new List<string>();
        foreach (Match bind in binds)
        foreach (Match literal in Regex.Matches(bind.Groups["args"].Value, "(?<!\\$)\"(?<text>[^\"]*[가-힣][^\"]*)\""))
            // " / 단계" and " 상한" are fragments of the composed effect keys checked below.
            if (!literal.Groups["text"].Value.StartsWith(" ") && !ConfigText.Has(literal.Groups["text"].Value))
                missing.Add(literal.Groups["text"].Value);
        foreach (EnhancePath path in Enum.GetValues(typeof(EnhancePath)))
        {
            var unit = path == EnhancePath.Ergonomics ? "" : " (%)";
            var name = EnhanceEffects.KoreanStatName(path);
            foreach (var key in new[] { name + " / 단계" + unit, name + " 상한" + unit })
                if (!ConfigText.Has(key)) missing.Add(key);
        }
        foreach (var section in new[] { "5. 무기 효과", "6. 방어구 효과" }) if (!ConfigText.Has(section)) missing.Add(section);
        Assert.Empty(missing);
    }

    [Fact]
    public void Switching_language_rewrites_only_the_f12_presentation()
    {
        var options = new DeveloperOptions();
        var general = options.Describe("1. 기본", "강화 메뉴 사용", "창고의 무기·방어구 우클릭에 장비 강화를 표시합니다.", false);
        var developer = options.Describe("2. 확률", "성공률 (%)", "+0에서 +1로 강화할 때 성공률. 이후 현재 단계 × 단계별 감소량만큼 줄어듭니다.");
        Assert.Equal("1. 기본", general.Category); Assert.Equal("강화 메뉴 사용", general.DispName);
        Assert.Null(general.Browsable); Assert.False(developer.Browsable);
        options.SetLanguage(true);
        Assert.Equal("1. General", general.Category);
        Assert.Equal("Enable enhance menu", general.DispName);
        Assert.StartsWith("Adds Enhance gear", general.Description);
        Assert.Equal("99. Developer options", developer.Category);
        Assert.Equal("2. Chance · Success chance (%)", developer.DispName);
        options.SetVisible(true);
        Assert.True(developer.Browsable); Assert.Null(general.Browsable);
        options.SetLanguage(false);
        Assert.Equal("99. 개발자 옵션", developer.Category);
        Assert.Equal("2. 확률 · 성공률 (%)", developer.DispName);
        Assert.True(developer.Browsable);
    }

    [Fact]
    public void Window_and_result_text_follow_the_language_and_cfg_keys_stay_korean()
    {
        Loc.English = true;
        Assert.Equal("Enhanced!", ResultText.Title(new() { Success = true }, false));
        Assert.Equal("3rd star evolved!", ResultText.Title(new() { Success = true, IsEvolution = true, StarLevel = 3 }, true));
        Assert.Equal("Recoil +3 reached", ResultText.Detail(new() { Success = true, Path = EnhancePath.Recoil, Level = 3 }));
        Assert.Contains("Destruction protection", ResultText.Detail(new() { DestructionPrevented = true }));
        Assert.Equal("Recoil reduction", EnhanceEffects.StatName(EnhancePath.Recoil));
        Assert.Equal("반동 감소", EnhanceEffects.KoreanStatName(EnhancePath.Recoil));
        Assert.Equal("2nd", Loc.Tier(2)); Assert.Equal("3rd", Loc.Tier(3)); Assert.Equal("11th", Loc.Tier(11));
        Assert.Equal("Handbook (no flea price)", Loc.PriceSource("북밸류 (플리 가격 없음)"));
        var error = Loc.Invalid("한국어", "English");
        Assert.Equal("English", error.Message); Assert.Equal("English", Loc.EnglishOf(error));
        Loc.English = false;
        Assert.Equal("2차", Loc.Tier(2));
        Assert.Equal("강화 성공!", ResultText.Title(new() { Success = true }, false));
        Assert.Equal("한국어", Loc.Invalid("한국어", "English").Message);
    }

    [Fact]
    public void Server_error_text_is_shown_in_the_selected_language()
    {
        var reply = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiReply<EnhanceSnapshot>>(
            "{\"Ok\":false,\"Error\":\"창고의 루블이 부족합니다.\",\"ErrorEnglish\":\"Not enough roubles in the stash.\"}")!;
        Assert.Equal("Not enough roubles in the stash.", reply.ErrorEnglish);
        // A 1.0.0 server sends no English text; the Korean text is used as is.
        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiReply<EnhanceSnapshot>>("{\"Ok\":false,\"Error\":\"오류\"}")!;
        Assert.Equal("", old.ErrorEnglish);
    }

    [Fact]
    public void Returned_gear_ids_ask_for_one_state_sync_per_new_id()
    {
        var source = Source("ItemMarkers.cs");
        Assert.Contains("if (unknown.Add(id)) Plugin.RequestSync();", source);
        Assert.Contains("if (unknown.Count >= 65536) unknown.Clear();", source);
        var plugin = Source("Plugin.cs");
        Assert.Contains("nextBackgroundSync = Clock + 5;", plugin);
        Assert.Contains("syncWanted && !visible && !busy && !backgroundSyncing", plugin);
    }
}
