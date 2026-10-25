using System.Reflection;
using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

// 1.4.0 window pieces that run without Unity: motion timing, status panel numbers, the mythic
// records the window and stash read, and the stash name line.
public sealed class WindowTests : IDisposable
{
    public WindowTests() { StatPatches.Registry.Clear(); StatPatches.Registry.Configure(new()); Loc.English = false; }
    public void Dispose() { StatPatches.Registry.Clear(); Loc.English = false; }

    [Fact]
    public void Motion_starts_hidden_overshoots_once_and_ends_still()
    {
        Assert.Equal((-22f, 0f), UiMotion.Panel(0, false));
        Assert.Equal((0f, 1f), UiMotion.Panel(UiMotion.PanelTime, false));
        // Row 2 waits 120 ms, dips 4 px past its line at 65 %, then settles.
        Assert.Equal((-30f, 0f), UiMotion.Row(0.1, 2, false));
        var dip = UiMotion.Row(2 * UiMotion.RowGap + UiMotion.RowTime * 0.65, 2, false);
        Assert.Equal(4f, dip.Offset, 3); Assert.Equal(1f, dip.Alpha);
        Assert.Equal((0f, 1f), UiMotion.Row(5, 2, false));
        Assert.Equal(0f, UiMotion.Fill(0.1, 0.24, false));
        Assert.Equal(1f, UiMotion.Fill(0.24 + UiMotion.FillTime, 0.24, false));
        Assert.Equal(0f, UiMotion.Shake(0.02, false));
        Assert.NotEqual(0f, UiMotion.Shake(0.1, false));
        Assert.Equal(0f, UiMotion.Shake(0.6, false));
        Assert.Equal(1f, UiMotion.Glow(1.6, 1.6, 0.5, false));
        Assert.Equal((0f, 0f), UiMotion.Ring(0.1, 0.23, 1.5, 5.5f));
        Assert.True(UiMotion.Spark(0.5, 0, 1.2, 460).Distance > 20);
    }

    [Fact]
    public void Reduced_motion_shows_the_end_state_at_once()
    {
        Assert.Equal((0f, 1f), UiMotion.Panel(0, true));
        Assert.Equal((0f, 1f), UiMotion.Row(0, 5, true));
        Assert.Equal(1f, UiMotion.Fill(0, 1, true));
        Assert.Equal(1f, UiMotion.PopScale(0, true));
        Assert.Equal(0f, UiMotion.Shake(0.1, true));
        Assert.Equal(0f, UiMotion.Glow(1.6, 1.6, 0.5, true));
    }

    [Fact]
    public void Weapon_sheet_uses_the_hook_formulas_and_marks_only_what_this_attempt_changes()
    {
        var raw = new WeaponStats { RecoilUp = 100, RecoilBack = 250, RecoilDelta = -0.1f, Ergonomics = 50, CenterOfImpact = 0.09f,
            FireRate = 750, Automatic = true, Durability = 95, MaxDurability = 100 };
        var tuning = new TranscendTuning();
        var now = new GearLevels { Recoil = 10, Mythic = -1 };
        var next = now; next.Recoil = 15;
        var rows = StatSheet.Weapon(raw, now, next, tuning, new MythicTuning());
        Assert.Equal(8, rows.Count);
        Assert.Equal("90", rows[0].Base);
        Assert.Equal(((1 + EnhanceEffects.RecoilDelta(-0.1f, 10)) * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), rows[0].Now);
        Assert.NotNull(rows[0].Next);
        Assert.Null(rows[2].Next); // ergonomics unchanged
        Assert.Equal("95/100", rows[5].Now);
        Assert.Equal("100%", rows[6].Now);
        // 3-star fire rate +1 level and mythic +1 level: only those rows change.
        var mythic = new GearLevels { Third = 0, Mythic = 0 };
        var after = mythic; after.Third = 1; after.Mythic = 1;
        rows = StatSheet.Weapon(raw, mythic, after, tuning, new MythicTuning());
        Assert.Equal("795", rows[4].Next);
        Assert.Equal("102%", rows[6].Next); Assert.Equal("102%", rows[7].Next);
        Assert.Null(rows[0].Next);
    }

    [Fact]
    public void Armor_sheet_counts_zones_class_and_damage_taken()
    {
        var raw = new ArmorStats { ArmorClass = 4, PlateClass = 5, Zones = 7, TemplateMax = 64, Speed = -8, Turn = -7, Ergonomics = -10 };
        var now = new GearLevels { ArmorDurability = 10.4, Mobility = 3.16, Second = 2, Third = 2, Mythic = -1 };
        var mystery = now; mystery.Mythic = 0; mystery.Zones = 2;
        var rows = StatSheet.Armor(raw, now, mystery, new TranscendTuning(), new MythicTuning());
        Assert.Equal("4 · 판 5", rows[0].Base);
        Assert.Equal("7곳", rows[1].Now); Assert.Equal("9곳", rows[1].Next);
        Assert.Equal("89.6%", rows[3].Now);
        Assert.Equal("100%", rows[5].Now); Assert.Null(rows[5].Next);
        var up = mystery; up.Mythic = 1;
        rows = StatSheet.Armor(raw, mystery, up, new TranscendTuning(), new MythicTuning());
        Assert.Equal("98%", rows[5].Next);
        // 3-star level 3 adds one class to the body and the plate.
        var third = now; third.Third = 3;
        rows = StatSheet.Armor(raw, now, third, new TranscendTuning(), new MythicTuning());
        Assert.Equal("5 · 판 6", rows[0].Next);
    }

    [Fact]
    public void Mythic_receipts_change_only_the_mythic_record_and_the_stash_shows_it()
    {
        StatPatches.Registry.Replace(new EnhanceSnapshot { ProfileId = "pmc", Items = new[]
        {
            new EnhanceRecord { ItemId = "vest", Path = EnhancePath.ArmorDurability, Level = 10, Evolved = true, StarLevel = 3 },
            new EnhanceRecord { ItemId = "vest", Path = EnhancePath.Mobility, Level = 10, Evolved = true, StarLevel = 3 }
        }, Transcend = new[] { new TranscendLink { ItemId = "vest", Second = 4, Third = 2 } } }, "pmc");
        Assert.Equal("★★★", StatPatches.Registry.Badge("vest"));
        Assert.Equal(-1, StatPatches.Registry.MythicLevel("vest"));
        // A ticket purchase and a reroll never touch the enhancement tracks.
        StatPatches.Registry.ApplyReceipt(new() { Action = EnhanceAction.BuyTicket, Success = true, ItemId = "vest", Path = EnhancePath.Mobility, Level = 0 }, "pmc");
        Assert.Equal(10, StatPatches.Registry.Find("vest", EnhancePath.Mobility)!.Level);
        StatPatches.Registry.ApplyReceipt(new() { Action = EnhanceAction.Mystery, Success = true, ItemId = "vest", Path = EnhancePath.Mobility,
            Level = 0, MythicLevel = 0, Zones = new[] { "Pelvis", "LeftThigh" } }, "pmc");
        Assert.Equal(0, StatPatches.Registry.MythicLevel("vest"));
        Assert.Equal(new[] { "Pelvis", "LeftThigh" }, StatPatches.Registry.MythicZones("vest"));
        Assert.Equal(10, StatPatches.Registry.Find("vest", EnhancePath.Mobility)!.Level);
        StatPatches.Registry.ApplyReceipt(new() { Action = EnhanceAction.Reroll, Success = true, ItemId = "vest", Path = EnhancePath.Mobility,
            Level = 0, MythicLevel = 0, Zones = new[] { "Pelvis", "RightThigh" } }, "pmc");
        StatPatches.Registry.ApplyReceipt(new() { Action = EnhanceAction.Mythic, Success = true, ItemId = "vest", Path = EnhancePath.Mobility,
            Level = 3, MythicLevel = 3, Zones = new[] { "Pelvis", "RightThigh" } }, "pmc");
        Assert.Equal(3, StatPatches.Registry.MythicLevel("vest"));
        Assert.Equal(10, StatPatches.Registry.Find("vest", EnhancePath.Mobility)!.Level);
        Assert.Equal("✦ 신화", StatPatches.Registry.Badge("vest"));
        var args = new object[] { new HooksTests.MarkerView(new TestItem("vest")), "Gen4" };
        typeof(ItemMarkers).GetMethod("Name", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        Assert.Equal("Gen4 <color=#FF6AD5>✦ 신화</color>", args[1]);
        Assert.Equal("<color=#FF6AD5>신화 3/5</color> · <color=#40D1D9>초월 4/5·2/5</color>", StashLine.Text(3, 4, 2));
        Assert.Equal("", StashLine.Text(-1, 0, 0));
        // A failed ??? destroys the gear: both records go.
        StatPatches.Registry.ApplyReceipt(new() { Action = EnhanceAction.Mystery, ItemId = "vest", FailureMode = FailureMode.Destroy, MythicLevel = -1 }, "pmc");
        Assert.Equal(-1, StatPatches.Registry.MythicLevel("vest"));
        Assert.Null(StatPatches.Registry.Find("vest"));
    }

    [Fact]
    public void Transcended_gear_shows_its_level_under_the_stars()
    {
        StatPatches.Registry.Replace(new EnhanceSnapshot { ProfileId = "pmc",
            Items = new[] { new EnhanceRecord { ItemId = "gun", Path = EnhancePath.Recoil, Level = 10, Evolved = true, StarLevel = 2 } },
            Transcend = new[] { new TranscendLink { ItemId = "gun", Second = 6 > 5 ? 5 : 6, Third = 0 } },
            Returns = new[] { new ReturnLink { ItemId = "lost", Tier = InsuranceTier.Basic } } }, "pmc");
        var args = new object[] { new HooksTests.MarkerView(new TestItem("gun")), "K2" };
        typeof(ItemMarkers).GetMethod("Name", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        // The stage line is its own text under the name now (StashLine), as N/5 per track.
        Assert.Equal("K2 <color=#B989F5>★★</color>", args[1]);
        Assert.Equal("<color=#40D1D9>초월 5/5·0/5</color>", StashLine.Text(-1, 5, 0));
        Assert.Single(StatPatches.Registry.Returns);
        Loc.English = true;
        Assert.Equal("✦ Mythic", Loc.L("✦ 신화", "✦ Mythic"));
        Assert.Equal("Groin", MythicText.ZoneName("Pelvis"));
        Assert.Equal("Jaw", MythicText.ZoneName("Jaw"));
        Loc.English = false;
        // Every zone the server can draw has a name of its own.
        foreach (var zone in MythicRules.BodyZones.Concat(MythicRules.HeadZones)) Assert.NotEqual(zone, MythicText.ZoneName(zone));
    }

    [Fact]
    public void Caliber_names_read_like_the_mockup()
    {
        Assert.Equal("5.56x45 NATO", CaliberText.Name("Caliber556x45NATO"));
        Assert.Equal("7.62x39", CaliberText.Name("Caliber762x39"));
        Assert.Equal("9x19 PARA", CaliberText.Name("Caliber9x19PARA"));
        Assert.Equal("12.7x55", CaliberText.Name("Caliber127x55"));
        Assert.Equal("12g", CaliberText.Name("Caliber12g"));
        Assert.Null(CaliberText.Name(""));
    }
}
