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

    [Fact]
    public void Window_text_has_an_english_pair_and_the_quote_names_the_track()
    {
        var view = Source("Plugin.TranscendView.cs");
        // Every Korean literal in the window goes through Loc.L with an English partner.
        foreach (Match line in Regex.Matches(view, "^.*[가-힣].*$", RegexOptions.Multiline))
        {
            var text = line.Value.Trim();
            // Log lines stay Korean like the rest of the mod's logs.
            if (text.StartsWith("//") || text.Contains("Logger.Log")) continue;
            Assert.True(text.Contains("Loc.L(") || text.StartsWith("$\""), text);
        }
        var plugin = Source("Plugin.cs");
        Assert.Contains("Track = transcendMode ? transcendTrack : TranscendTrack.None", plugin);
        Assert.Contains("Action = transcendMode ? EnhanceAction.Transcend", plugin);
        // The plate re-registration follows the server's CanRegister (it includes DurabilityPending).
        var insurance = Source("Plugin.Insurance.cs");
        Assert.Contains("!shown.CanRegister", insurance);
        Assert.Contains("view?.CanRegister == true", insurance);
        var markers = Source("ItemMarkers.cs");
        Assert.Contains("TranscendTotal(owner) > 0", markers);
    }
}
