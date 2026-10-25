using GearEnhance;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Xunit;

namespace GearEnhanceTests;

// 1.4.0 on the server: ??? (50/50), the mythic track, armor zones and rerolls, evolution
// destruction tickets, and delayed enhancement insurance returns.
[Collection("MailReturnPatch")]
public sealed class MythicTests
{
    private static readonly MongoId Session = new("111111111111111111111111");
    private static readonly MongoId Stash = new("222222222222222222222222");
    private static readonly MongoId Vest = new("333333333333333333333333");
    private static readonly MongoId Plate = new("444444444444444444444444");
    private static readonly MongoId Gun = new("888888888888888888888888");
    private static readonly MongoId VestTpl = new("aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly MongoId PlateTpl = new("bbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly MongoId GunTpl = new("eeeeeeeeeeeeeeeeeeeeeeee");
    private static readonly MongoId RubleTpl = new("5449016a4bdc2d6f028b456f");
    private static readonly JsonUtil Json = new([new SptJsonConverterRegistrator()]);

    private sealed class Backend : SptEnhanceBackend
    {
        public SptProfile Saved = null!;
        public EnhanceService Service = null!;
        public readonly Queue<double> Draws = new();
        public double DrawValue;
        public readonly List<List<Item>> Mailed = new();
        private readonly InventoryHelper native = new(null!, null!, null!, null!, null!, null!, null!, null!);
        public Backend() : base(null!, null!, null!, null!, null!, null!) { }
        public override double Draw() => Draws.Count > 0 ? Draws.Dequeue() : DrawValue;
        public override SptProfile Profile(MongoId session) => session == Session ? Saved : null!;
        public override bool Eligible(MongoId template) => template == VestTpl || template == PlateTpl || template == GunTpl;
        public override bool IsWeapon(MongoId template) => template == GunTpl;
        public override bool IsArmorPart(MongoId template) => template == PlateTpl;
        public override HashSet<string> SlotNames(MongoId template) => template == VestTpl ? ["Front_plate"] : [];
        public override double TemplateMaxDurability(MongoId template) => template == VestTpl ? 50 : template == PlateTpl ? 30 : 100;
        // The vest protects the chest and the front of the neck; its plate adds the back of the neck.
        public override IEnumerable<string> ArmorZones(MongoId template) =>
            template == VestTpl ? ["RibcageUp", "RibcageLow", "SpineTop", "NeckFront"] : template == PlateTpl ? ["NeckBack"] : [];
        public override bool IsHeadArmor(MongoId template) => false;
        public override (double, string) Price(MongoId template, PriceBasis basis) =>
            (template == VestTpl ? 100000 : template == PlateTpl ? 10000 : template == GunTpl ? 200000
                : template.ToString() == MythicRules.Ledx ? 1000000 : 1000, "북밸류");
        public override void RemoveMoney(PmcData profile, MongoId id, int count, MongoId session, ItemEventRouterResponse output) =>
            native.RemoveItemByCount(profile, id, count, session, output);
        public override void RemoveMaterial(PmcData profile, MongoId id, int count, MongoId session, ItemEventRouterResponse output) =>
            native.RemoveItemByCount(profile, id, count, session, output);
        public override void Destroy(PmcData profile, MongoId id, MongoId session, ItemEventRouterResponse output) =>
            native.RemoveItem(profile, id, session, output);
        public override void SendReturn(MongoId session, List<Item> tree, string text)
        {
            MailReturnPatch.Service = Service;
            var details = new SendMessageDetails { RecipientId = session, Items = tree };
            MailReturnPatch.Prefix(details);
            details.Items = Json.Deserialize<List<Item>>(Json.Serialize(tree)!)!.ReplaceIDs().ToList();
            MailReturnPatch.Postfix(details);
            Mailed.Add(details.Items);
        }
    }

    private sealed class Rig
    {
        public readonly Backend Backend = new();
        public readonly EnhanceService Service;
        public readonly PmcData Pmc = new() { Id = Session, Inventory = new() { Stash = Stash, Items = [] }, InsuredItems = [] };
        public EnhanceState State => (EnhanceState)Backend.Saved.ExtensionData!["rfGearEnhance"]!;
        public EnhanceSettings Settings = new();

        public Rig(int stars = 3, int ledx = 10, int cards = 10)
        {
            Add(Vest, VestTpl, Stash, "hideout");
            Add(Plate, PlateTpl, Vest, "Front_plate");
            Add(Gun, GunTpl, Stash, "hideout");
            Stack(RubleTpl.ToString(), 900000000);
            Stack(MythicRules.Ledx, ledx);
            Stack(SuccessBoost.GraphicsCardTemplate, cards);
            Stack(SuccessBoost.BitcoinTemplate, 10);
            var state = new EnhanceState();
            foreach (var id in new[] { Vest, Gun })
            {
                state.Tracks[id.ToString()] = id == Gun
                    ? new() { [EnhancePath.Recoil] = 10, [EnhancePath.Ergonomics] = 10, [EnhancePath.Precision] = 10 }
                    : new() { [EnhancePath.ArmorDurability] = 10, [EnhancePath.Mobility] = 10 };
                if (stars >= 2) state.EvolvedItems.Add(id.ToString());
                if (stars >= 3) state.ThirdStarItems.Add(id.ToString());
            }
            Backend.Saved = new SptProfile { CharacterData = new() { PmcData = Pmc }, InsuranceList = [], DialogueRecords = new(),
                ExtensionData = new() { ["rfGearEnhance"] = state } };
            Service = new(Backend, Json);
            Backend.Service = Service;
        }

        private int serial;
        public void Add(MongoId id, MongoId template, MongoId parent, string slot) =>
            Pmc.Inventory!.Items!.Add(new Item { Id = id, Template = template, ParentId = parent.ToString(), SlotId = slot,
                Upd = new() { Repairable = new() { Durability = 50, MaxDurability = 50 } } });
        public void Stack(string template, long count) => Pmc.Inventory!.Items!.Add(new Item { Id = new($"9{serial++:x}{new string('9', 22)}"),
            Template = new MongoId(template), ParentId = Stash.ToString(), SlotId = "hideout", Upd = new() { StackObjectsCount = count } });
        public long Stock(string template) => (long)Pmc.Inventory!.Items!.Where(i => i.Template.ToString() == template).Sum(i => i.Upd?.StackObjectsCount ?? 1);
        public long Roubles => Stock(RubleTpl.ToString());
        public bool Has(MongoId id) => Pmc.Inventory!.Items!.Any(i => i.Id == id);

        public EnhanceQuote Quote(EnhanceAction action, MongoId? item = null, QuoteOptions? options = null, bool protect = false) =>
            Service.Quote(Session, (item ?? Vest).ToString(), Settings, null, new EnhanceProtection { Downgrade = protect }, action, options);
        public EnhanceReceipt Pay(EnhanceQuote quote, bool confirm = true)
        {
            var output = new ItemEventRouterResponse { ProfileChanges = new() { [Session] = new() { Items = new() { NewItems = [], ChangedItems = [], DeletedItems = [] } } } };
            Service.Execute(Session, Pmc, quote.QuoteId, confirm, output);
            return Service.Receipt(Session, quote.QuoteId)!;
        }
        // Becomes mythic: the first draw passes the 50/50, the next ones pick the zones.
        public EnhanceReceipt Mystery(MongoId? item = null, params double[] zoneDraws)
        {
            Backend.Draws.Enqueue(0.1);
            foreach (var draw in zoneDraws) Backend.Draws.Enqueue(draw);
            return Pay(Quote(EnhanceAction.Mystery, item));
        }
    }

    [Fact]
    public void Mystery_needs_three_stars_costs_eight_million_and_a_ledx_and_takes_no_extras()
    {
        Assert.Throws<InvalidOperationException>(() => new Rig(stars: 2).Quote(EnhanceAction.Mystery));
        var rig = new Rig();
        var quote = rig.Quote(EnhanceAction.Mystery);
        Assert.Equal(50, quote.SuccessPercent);
        Assert.Equal(8000000, quote.Cost);
        Assert.Equal(MythicRules.Ledx, Assert.Single(quote.Materials).Template);
        Assert.True(quote.CanDestroy); Assert.False(quote.BoostAvailable); Assert.False(quote.InstantAvailable);
        Assert.False(quote.DestructionProtectionAvailable); Assert.False(quote.DowngradeProtectionAvailable);
        Assert.Equal(-1, quote.MythicLevel);
        // Zones the vest and its plate do not protect yet.
        Assert.Equal(["LeftUpperArm", "RightUpperArm", "LeftForearm", "RightForearm", "Pelvis", "PelvisBack", "LeftThigh", "RightThigh"], quote.ZoneOptions);
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.Mystery, options: new() { Instant = true }));
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.Mystery,
            options: new() { Boost = [new BoostChoice { Kind = BoostMaterial.Bitcoin, Count = 1 }] }));
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.Mystery, options: new() { UseTicket = true }));
        // Destruction must be confirmed, like a star evolution.
        Assert.Throws<InvalidOperationException>(() => rig.Pay(rig.Quote(EnhanceAction.Mystery), confirm: false));
    }

    [Fact]
    public void Mystery_success_makes_armor_mythic_with_two_drawn_zones()
    {
        var rig = new Rig();
        var roubles = rig.Roubles;
        // Options are LeftUpperArm..RightThigh (8): 0.5 → index 4 (Pelvis), then 0 → LeftUpperArm.
        var receipt = rig.Mystery(null, 0.5, 0);
        Assert.True(receipt.Success);
        Assert.Equal(0, receipt.MythicLevel);
        Assert.Equal(["Pelvis", "LeftUpperArm"], receipt.Zones);
        Assert.Equal(roubles - 8000000, rig.Roubles);
        Assert.Equal(9, rig.Stock(MythicRules.Ledx));
        var record = rig.State.Mythic[Vest.ToString()];
        Assert.Equal(8, record.Candidates.Count);
        var link = Assert.Single(rig.Service.Snapshot(Session).Mythic);
        Assert.Equal((Vest.ToString(), 0), (link.ItemId, link.Level));
        Assert.Equal(["Pelvis", "LeftUpperArm"], link.Zones);
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.Mystery));
        var weapon = rig.Mystery(Gun);
        Assert.True(weapon.Success); Assert.Empty(weapon.Zones);
    }

    [Fact]
    public void Mystery_failure_destroys_the_gear_and_its_records()
    {
        var rig = new Rig();
        rig.State.Tickets[Vest.ToString()] = new TicketRecord { ThirdBought = 1 };
        rig.Backend.DrawValue = 0.9;
        var receipt = rig.Pay(rig.Quote(EnhanceAction.Mystery));
        Assert.False(receipt.Success);
        Assert.Equal(FailureMode.Destroy, receipt.FailureMode);
        Assert.False(rig.Has(Vest)); Assert.False(rig.Has(Plate));
        Assert.False(rig.State.Tracks.ContainsKey(Vest.ToString()));
        Assert.False(rig.State.Mythic.ContainsKey(Vest.ToString()));
        Assert.False(rig.State.Tickets.ContainsKey(Vest.ToString()));
    }

    [Fact]
    public void Mythic_track_chances_pity_cost_growth_ledx_and_drops()
    {
        var rig = new Rig(ledx: 40);
        rig.Mystery(Gun);
        var first = rig.Quote(EnhanceAction.Mythic, Gun);
        Assert.Equal((0, 1, 40d), (first.CurrentLevel, first.NextLevel, first.SuccessPercent));
        Assert.Equal(5000000, first.Cost);
        Assert.Equal(FailureMode.KeepItem, first.FailureMode);
        Assert.Equal((0d, 2d, 10d), (first.CurrentEffect, first.NextEffect, first.EffectCap));
        Assert.True(first.BoostAvailable); Assert.True(first.InstantAvailable);
        // Two failures add 3%p each.
        rig.Backend.DrawValue = 0.99;
        rig.Pay(first);
        rig.Pay(rig.Quote(EnhanceAction.Mythic, Gun));
        var third = rig.Quote(EnhanceAction.Mythic, Gun);
        Assert.Equal((6d, 46d), (third.PityPercent, third.SuccessPercent));
        rig.Backend.DrawValue = 0;
        var success = rig.Pay(third);
        Assert.Equal(1, success.MythicLevel); Assert.Equal(0, success.Pity);
        var costs = new List<int> { 5000000 };
        for (var level = 1; level < 3; level++)
        {
            var quote = rig.Quote(EnhanceAction.Mythic, Gun);
            costs.Add(quote.Cost);
            rig.Pay(quote);
        }
        var drop = rig.Quote(EnhanceAction.Mythic, Gun);
        costs.Add(drop.Cost);
        Assert.Equal([5000000, 5500000, 6050000, 6655000], costs);
        Assert.Equal((3, 4, 15d), (drop.CurrentLevel, drop.NextLevel, drop.SuccessPercent));
        Assert.Equal(FailureMode.Downgrade, drop.FailureMode);
        Assert.True(drop.DowngradeProtectionAvailable);
        Assert.Equal(6655000, drop.DowngradeProtectionFee);
        rig.Backend.DrawValue = 0.99;
        var dropped = rig.Pay(drop);
        Assert.Equal(2, dropped.MythicLevel);
        Assert.Equal(FailureMode.Downgrade, dropped.FailureMode);
        // The enhancement tracks are untouched by a mythic drop.
        Assert.Equal(10, rig.State.Tracks[Gun.ToString()][EnhancePath.Recoil]);
        Assert.Equal(40 - 1 - 6, rig.Stock(MythicRules.Ledx));
    }

    [Fact]
    public void Mythic_last_level_costs_seven_million_three_hundred_twenty_thousand_five_hundred()
    {
        Assert.Equal([5000000, 5500000, 6050000, 6655000, 7320500], Enumerable.Range(0, 5).Select(l => MythicRules.Cost(l, new MythicTuning())));
        Assert.Equal(10, MythicEffects.WeaponPercent(5, new MythicTuning()));
        Assert.Equal(10, MythicEffects.ArmorPercent(9, new MythicTuning()));
    }

    [Fact]
    public void Mythic_drop_protection_and_instant_and_materials_add_up()
    {
        var rig = new Rig();
        rig.Mystery(Gun);
        rig.State.Mythic[Gun.ToString()].Level = 4;
        var quote = rig.Quote(EnhanceAction.Mythic, Gun, new QuoteOptions { Instant = true,
            Boost = [new BoostChoice { Kind = BoostMaterial.Bitcoin, Count = 2 }] }, protect: true);
        Assert.Equal(20, quote.SuccessPercent);
        Assert.True(quote.Protection.Downgrade);
        Assert.Equal(7320500 * 2 + 600000 + 50000, quote.Cost);
        rig.Backend.DrawValue = 0.99;
        var receipt = rig.Pay(quote);
        Assert.True(receipt.DowngradePrevented); Assert.True(receipt.Instant);
        Assert.Equal(4, receipt.MythicLevel);
        Assert.Throws<InvalidOperationException>(() => { rig.State.Mythic[Gun.ToString()].Level = 5; rig.Quote(EnhanceAction.Mythic, Gun); });
    }

    [Fact]
    public void Reroll_replaces_one_zone_from_the_options_fixed_at_the_mystery_and_grows_by_half()
    {
        var rig = new Rig();
        rig.Mystery(null, 0.5, 0);
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.Reroll, Gun));
        Assert.Throws<ArgumentException>(() => rig.Quote(EnhanceAction.Reroll, options: new() { Slot = 2 }));
        // Taking the plate out later must not offer the back of the neck: options were fixed then.
        rig.Pmc.Inventory!.Items!.Single(i => i.Id == Plate).ParentId = Stash.ToString();
        var quote = rig.Quote(EnhanceAction.Reroll, options: new() { Slot = 1 });
        Assert.Equal(2000000, quote.Cost);
        Assert.Equal(SuccessBoost.GraphicsCardTemplate, Assert.Single(quote.Materials).Template);
        Assert.DoesNotContain("NeckBack", quote.ZoneOptions);
        Assert.DoesNotContain("Pelvis", quote.ZoneOptions); Assert.DoesNotContain("LeftUpperArm", quote.ZoneOptions);
        Assert.Equal(6, quote.ZoneOptions.Length);
        // Options without the held two: RightUpperArm, LeftForearm, RightForearm, PelvisBack, LeftThigh, RightThigh.
        // First draw: the (certain) roll; second: the zone, 0.99 → the last option.
        rig.Backend.Draws.Enqueue(0.0); rig.Backend.Draws.Enqueue(0.99);
        var receipt = rig.Pay(quote);
        Assert.Equal(("LeftUpperArm", "RightThigh"), (receipt.OldZone, receipt.NewZone));
        Assert.Equal(["Pelvis", "RightThigh"], rig.State.Mythic[Vest.ToString()].Zones);
        Assert.Equal(9, rig.Stock(SuccessBoost.GraphicsCardTemplate));
        Assert.Equal(3000000, rig.Quote(EnhanceAction.Reroll, options: new() { Slot = 0 }).Cost);
        rig.State.Mythic[Vest.ToString()].Rerolls = 3;
        Assert.Equal(6750000, rig.Quote(EnhanceAction.Reroll).Cost);
    }

    [Fact]
    public void Insurance_value_counts_the_mystery_and_the_mythic_levels()
    {
        var rig = new Rig();
        var before = rig.Quote(EnhanceAction.Insure).InsuredValue;
        rig.Mystery(Gun);
        rig.State.Mythic[Gun.ToString()].Level = 2;
        var after = rig.Quote(EnhanceAction.Insure, Gun).InsuredValue;
        var gunBefore = new Rig().Quote(EnhanceAction.Insure, Gun).InsuredValue;
        // ??? 8M + levels 5M + 5.5M + three LEDX at 1M each.
        Assert.Equal(gunBefore + 8000000 + 5000000 + 5500000 + 3000000, after);
        Assert.True(before > 0);
    }

    [Fact]
    public void Tickets_have_lifetime_limits_and_belong_to_the_next_evolution()
    {
        var rig = new Rig(stars: 1);
        var quote = rig.Quote(EnhanceAction.Evolve, Gun);
        Assert.Equal((2, 0, 0, 3, 3000000), (quote.TicketStars, quote.TicketsHeld, quote.TicketsBought, quote.TicketMax, quote.TicketPrice));
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.BuyTicket, Gun, new() { TicketStars = 3 }));
        for (var n = 0; n < 3; n++) Assert.Equal(3000000, rig.Pay(rig.Quote(EnhanceAction.BuyTicket, Gun, new() { TicketStars = 2 })).Paid);
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.BuyTicket, Gun, new() { TicketStars = 2 }));
        var evolve = rig.Quote(EnhanceAction.Evolve, Gun, new() { UseTicket = true });
        Assert.True(evolve.UseTicket); Assert.False(evolve.CanDestroy);
        Assert.Equal((3, 3), (evolve.TicketsHeld, evolve.TicketsBought));
        // A failed attempt with a ticket keeps the gear and spends one ticket.
        rig.Backend.DrawValue = 0.99;
        var failed = rig.Pay(evolve, confirm: false);
        Assert.False(failed.Success); Assert.True(failed.TicketUsed); Assert.Equal(2, failed.TicketsLeft);
        Assert.Equal(FailureMode.KeepItem, failed.FailureMode);
        Assert.True(rig.Has(Gun));
        // A success spends one too, and the ★★ tickets left over vanish.
        rig.Backend.DrawValue = 0;
        var evolved = rig.Pay(rig.Quote(EnhanceAction.Evolve, Gun, new() { UseTicket = true }));
        Assert.True(evolved.Success); Assert.True(evolved.TicketUsed);
        Assert.Equal((1, 0), (evolved.TicketsVoided, evolved.TicketsLeft));
        var next = rig.Quote(EnhanceAction.BuyTicket, Gun, new() { TicketStars = 3 });
        Assert.Equal(8000000, next.Cost);
        Assert.Equal((3, 0, 0, 2), (next.TicketStars, next.TicketsHeld, next.TicketsBought, next.TicketMax));
        rig.Pay(next);
        rig.Pay(rig.Quote(EnhanceAction.BuyTicket, Gun, new() { TicketStars = 3 }));
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.BuyTicket, Gun, new() { TicketStars = 3 }));
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.BuyTicket, Gun, new() { TicketStars = 2 }));
        Assert.Equal(2, rig.Quote(EnhanceAction.EvolveThird, Gun).TicketsHeld);
    }

    [Fact]
    public void Evolution_without_a_ticket_still_destroys_and_a_missing_ticket_is_refused()
    {
        var rig = new Rig(stars: 1);
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.Evolve, Gun, new() { UseTicket = true }));
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.Upgrade, Gun, new() { UseTicket = true }));
        var plain = rig.Quote(EnhanceAction.Evolve, Gun);
        Assert.True(plain.CanDestroy);
        rig.Backend.DrawValue = 0.99;
        Assert.Equal(FailureMode.Destroy, rig.Pay(plain).FailureMode);
        Assert.False(rig.Has(Gun));
    }

    [Fact]
    public void Remap_moves_mythic_records_and_tickets_to_the_returned_copy()
    {
        var rig = new Rig();
        rig.Mystery(null, 0.5, 0);
        rig.State.Tickets[Vest.ToString()] = new TicketRecord { ThirdBought = 2 };
        var tree = rig.Pmc.Inventory!.Items!.Where(i => i.Id == Vest || i.Id == Plate).ToList();
        rig.Pmc.Inventory.Items.RemoveAll(tree.Contains);
        rig.Backend.SendReturn(Session, tree, "");
        var copy = rig.Backend.Mailed.Single().Single(i => i.Template == VestTpl).Id.ToString();
        Assert.Equal(["Pelvis", "LeftUpperArm"], rig.State.Mythic[copy].Zones);
        Assert.Equal(2, rig.State.Tickets[copy].ThirdBought);
        Assert.False(rig.State.Mythic.ContainsKey(Vest.ToString()));
    }

    [Theory]
    [InlineData(InsuranceTier.Basic, 24)]
    [InlineData(InsuranceTier.Advanced, 12)]
    public void A_delayed_return_waits_keeps_its_records_and_is_mailed_once_due(InsuranceTier tier, double hours)
    {
        var rig = new Rig();
        var insure = rig.Service.Quote(Session, Vest.ToString(), rig.Settings, null, null, EnhanceAction.Insure, null, tier);
        Assert.Equal(hours, insure.InsuranceHours[(int)tier]);
        Assert.Equal(0, insure.InsuranceHours[(int)InsuranceTier.Premium]);
        Assert.Equal(99, insure.InsuranceChances[(int)InsuranceTier.Premium]);
        rig.Pay(insure);
        Assert.Equal(hours, rig.State.Coverage[Vest.ToString()].ReturnHours);
        var capture = rig.Service.BeginRaidEnd(Session)!;
        rig.Pmc.Inventory!.Items!.RemoveAll(i => i.Id == Vest || i.Id == Plate);
        rig.Backend.DrawValue = 0;
        Assert.Equal((1, 0), rig.Service.CompleteRaidEnd(Session, capture, null));
        Assert.Empty(rig.Backend.Mailed);
        var waiting = Assert.Single(rig.State.Returns);
        Assert.Equal(waiting.StartUnixSeconds + (long)(hours * 3600), waiting.DueUnixSeconds);
        var link = Assert.Single(rig.Service.Snapshot(Session).Returns);
        Assert.Equal((Vest.ToString(), VestTpl.ToString(), tier, 3), (link.ItemId, link.Template, link.Tier, link.Stars));
        // Opening the window for other gear must not prune the waiting gear's records.
        rig.Quote(EnhanceAction.Insure, Gun);
        Assert.True(rig.State.Tracks.ContainsKey(Vest.ToString()));
        Assert.Empty(rig.Backend.Mailed);
        waiting.DueUnixSeconds = waiting.StartUnixSeconds - 1;
        Assert.Empty(rig.Service.Snapshot(Session).Returns);
        var mailed = Assert.Single(rig.Backend.Mailed);
        var copy = mailed.Single(i => i.Template == VestTpl).Id.ToString();
        Assert.Equal(10, rig.State.Tracks[copy][EnhancePath.ArmorDurability]);
        Assert.Empty(rig.State.Returns);
    }

    [Fact]
    public void Premium_and_coverage_bought_before_1_4_0_return_at_once()
    {
        var rig = new Rig();
        rig.State.Coverage[Vest.ToString()] = new InsuranceCoverage { Tier = InsuranceTier.Basic, ReturnPercent = 20 };
        rig.State.Coverage[Gun.ToString()] = new InsuranceCoverage { Tier = InsuranceTier.Premium, ReturnPercent = 99 };
        var capture = rig.Service.BeginRaidEnd(Session)!;
        rig.Pmc.Inventory!.Items!.RemoveAll(i => i.Id == Vest || i.Id == Plate || i.Id == Gun);
        rig.Backend.DrawValue = 0;
        Assert.Equal((2, 0), rig.Service.CompleteRaidEnd(Session, capture, null));
        Assert.Equal(2, rig.Backend.Mailed.Count);
        Assert.Empty(rig.State.Returns);
    }

    [Fact]
    public void Mythic_tuning_out_of_range_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new EnhanceSettings { Mythic = new() { Success = [40, 30] } }.Validate());
        Assert.Throws<ArgumentException>(() => new EnhanceSettings { Mythic = new() { CostGrowth = 0.5 } }.Validate());
        Assert.Throws<ArgumentException>(() => new EnhanceSettings { Boost = new() { MaxKinds = 4 } }.Validate());
        Assert.Throws<ArgumentException>(() => new EnhanceSettings { BasicInsuranceReturnHours = -1 }.Validate());
        Assert.Throws<ArgumentException>(() => new EnhanceSettings { Mythic = null! }.Validate());
        Assert.Equal((50, 10, 4), (SuccessBoost.MaxCount(BoostMaterial.Dogtag, new()), SuccessBoost.MaxCount(BoostMaterial.GraphicsCard, new()),
            SuccessBoost.MaxCount(BoostMaterial.Bitcoin, new())));
        Assert.Equal(5, SuccessBoost.Points(BoostMaterial.Dogtag, 50, new()));
        new EnhanceSettings().Validate();
    }
}
