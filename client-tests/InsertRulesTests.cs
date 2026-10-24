using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

// 1.3.0 plate auto-register: which inserts reach the server and what the notice says.
public sealed class InsertRulesTests : IDisposable
{
    public InsertRulesTests() => StatPatches.Registry.Clear();
    public void Dispose() { StatPatches.Registry.Clear(); Loc.English = false; }

    private static void Load() => StatPatches.Registry.Replace(new EnhanceSnapshot
    {
        ProfileId = "pmc",
        Items = [new EnhanceRecord { ItemId = "vest", Path = EnhancePath.ArmorDurability, Level = 5 }],
        Inserts = [new InsertLink { ArmorId = "vest", PartIds = ["paid"] }],
        Durability = [new DurabilityLink { ItemId = "granted", Amount = 3 }],
        Transcend = [new TranscendLink { ItemId = "tough", Second = 1 }]
    }, "pmc");

    [Fact]
    public void Only_unregistered_plates_in_enhanced_armor_or_granted_plates_are_sent()
    {
        var registry = StatPatches.Registry;
        Assert.False(InsertRules.Relevant(registry, "vest", "new"));
        Load();
        Assert.True(InsertRules.Relevant(registry, "vest", "new"));
        Assert.False(InsertRules.Relevant(registry, "vest", "paid"));
        // Plain armor: only a plate carrying added max durability matters (it must lose it).
        Assert.False(InsertRules.Relevant(registry, "plain", "new"));
        Assert.True(InsertRules.Relevant(registry, "plain", "granted"));
        // A registered plate back in 2-star transcended armor gets its max durability again.
        Assert.True(InsertRules.Relevant(registry, "tough", "paid"));
        Assert.False(InsertRules.Relevant(registry, "", "new"));
        Assert.False(InsertRules.Relevant(registry, "vest", null!));
    }

    [Fact]
    public void The_notice_counts_plates_still_waiting_once()
    {
        Load();
        var seen = new[] { ("vest", "new"), ("vest", "new"), ("vest", "paid"), ("plain", "granted") };
        Assert.Equal(1, InsertRules.Waiting(StatPatches.Registry, seen));
        Assert.Contains("차액", InsertRules.Notice(1, true));
        Assert.DoesNotContain("차액", InsertRules.Notice(2, false));
        Loc.English = true;
        Assert.StartsWith("1 pricier plate", InsertRules.Notice(1, true));
    }
}
