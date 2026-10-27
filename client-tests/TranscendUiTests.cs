using System.Reflection;
using System.Text.RegularExpressions;
using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

public sealed class TranscendUiTests : IDisposable
{
    public void Dispose() => Loc.English = false;

    private static string Source(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GearEnhance.sln"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Client", file));
    }

    [Fact]
    public void Every_material_the_server_can_ask_for_has_a_name_in_both_languages()
    {
        var ids = typeof(TranscendRules).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToArray();
        Assert.Equal(8, ids.Length);
        foreach (var id in ids)
        {
            Loc.English = false;
            var korean = TranscendText.MaterialName(id);
            Loc.English = true;
            var english = TranscendText.MaterialName(id);
            Assert.NotEqual(id, korean); Assert.NotEqual(id, english);
        }
        Loc.English = false;
        Assert.Equal("그래픽 카드", TranscendText.MaterialName(TranscendRules.GraphicsCard));
    }

    [Fact]
    public void Missing_materials_name_only_the_short_ones()
    {
        var quote = new EnhanceQuote
        {
            Materials = new[]
            {
                new TranscendMaterial { Template = TranscendRules.WeaponParts, Count = 2, Owned = 4 },
                new TranscendMaterial { Template = TranscendRules.GunpowderEagle, Count = 2, Owned = 1 }
            }
        };
        Assert.Equal("화약 \"Eagle\" 1개", TranscendText.Missing(quote));
        Assert.False(quote.CanPay);
        quote.Materials[1].Owned = 2;
        Assert.Equal("", TranscendText.Missing(quote));
        Assert.Equal("", TranscendText.Missing(null!));
    }

    [Fact]
    public void Transcend_results_never_mention_destruction_and_show_the_pity()
    {
        var success = new EnhanceReceipt { Success = true, Transcend = TranscendTrack.Second, Level = 3 };
        Assert.Equal("초월 성공!", ResultText.Title(success, false));
        Assert.Equal("2성 초월 3단계 달성", ResultText.Detail(success));
        var kept = new EnhanceReceipt { Transcend = TranscendTrack.Third, Level = 2, Pity = 6, FailureMode = FailureMode.KeepItem };
        Assert.Equal("초월 실패", ResultText.Title(kept, false));
        Assert.Equal("3성 초월 2단계 유지 · 재료 소모 · 천장 +6%p", ResultText.Detail(kept));
        var dropped = new EnhanceReceipt { Transcend = TranscendTrack.Third, Level = 2, Pity = 9, FailureMode = FailureMode.Downgrade };
        Assert.Equal("초월 단계 하락", ResultText.Title(dropped, false));
        Assert.Contains("2단계로 하락 · 장비 파괴 없음", ResultText.Detail(dropped));
        var saved = new EnhanceReceipt { Transcend = TranscendTrack.Third, Level = 3, Pity = 9, FailureMode = FailureMode.Downgrade, DowngradePrevented = true };
        Assert.Equal("초월 실패 · 하락 방지", ResultText.Title(saved, false));
        Assert.StartsWith("단계 하락 방지 발동 · 3단계 유지", ResultText.Detail(saved));
        Assert.Equal(FeedbackCue.Downgrade, GameFeedback.Result(dropped, false));
        Loc.English = true;
        Assert.Equal("Transcended!", ResultText.Title(success, false));
        Assert.Equal("3-star transcendence dropped to level 2 · nothing destroyed · pity +9%p", ResultText.Detail(dropped));
    }

    [Theory]
    [InlineData("Plugin.View.cs")]
    [InlineData("Plugin.Flow.cs")]
    [InlineData("Plugin.TabUpgrade.cs")]
    [InlineData("Plugin.TabInfo.cs")]
    [InlineData("Plugin.Popups.cs")]
    [InlineData("Plugin.Insurance.cs")]
    [InlineData("Plugin.Status.cs")]
    [InlineData("StatSheet.cs")]
    public void Window_text_has_an_english_pair(string file)
    {
        var view = Source(file);
        // Every Korean literal in the 1.4.0 window goes through Loc.L with an English partner
        // (1.4.1: Loc.F for interpolated text, so Russian and Chinese rows find it).
        foreach (Match line in Regex.Matches(view, "^.*[가-힣].*$", RegexOptions.Multiline))
        {
            var text = line.Value.Trim();
            // Log lines stay Korean like the rest of the mod's logs; the language button names itself.
            // Font names are system names, not window text.
            if (text.StartsWith("//") || text.Contains("Logger.Log") || text.Contains("LogDetailed(") || text.Contains("\"한국어\"")
                || text.Contains("\"Malgun Gothic\"")) continue;
            Assert.True(text.Contains("Loc.L(") || text.Contains("Loc.F(") || text.Contains("Loc.Invalid(") || text.Contains("Loc.Argument(")
                || text.Contains("Loc.InvalidF(") || text.Contains("Loc.ArgumentF(") || text.StartsWith("$\""), text);
        }
    }

    [Fact]
    public void The_quote_names_the_track_and_the_plates_follow_the_server()
    {
        var flow = Source("Plugin.Flow.cs");
        Assert.Contains("Track = action == EnhanceAction.Transcend ? transcendTrack : TranscendTrack.None", flow);
        Assert.Contains("UiTab.Transcend => EnhanceAction.Transcend", flow);
        // ??? never sends chance materials; the server refuses them anyway.
        Assert.Contains("Boost = action == EnhanceAction.Mystery ? Array.Empty<BoostChoice>() : BoostRequest()", flow);
        // The plate re-registration follows the server's CanRegister (it includes DurabilityPending).
        Assert.Contains("!shown.CanRegister", Source("Plugin.Insurance.cs"));
        Assert.Contains("view?.CanRegister == true", Source("Plugin.TabInfo.cs"));
        var markers = Source("ItemMarkers.cs");
        Assert.Contains("TranscendTotal(owner) > 0", markers);
    }
}
