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

// 1.4.0 prestige insurance: registered gear comes back by mail only when the character advances
// to the next prestige stage, never at the last stage, and the registration ends with the return.
[Collection("MailReturnPatch")]
public sealed class PrestigeTests
{
    private static readonly MongoId Session = new("111111111111111111111111");
    private static readonly MongoId Stash = new("222222222222222222222222");
    private static readonly MongoId Vest = new("333333333333333333333333");
    private static readonly MongoId Plate = new("444444444444444444444444");
    private static readonly MongoId NewStash = new("888888888888888888888888");
    private static readonly MongoId VestTpl = new("aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly MongoId PlateTpl = new("bbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly MongoId RubleTpl = new("5449016a4bdc2d6f028b456f");
    private static readonly JsonUtil Json = new([new SptJsonConverterRegistrator()]);

    private sealed class Backend : SptEnhanceBackend
    {
        public SptProfile Saved = null!;
        public EnhanceService Service = null!;
        public int Stages = 6;
        public double VestPrice = 100000;
        public readonly List<(List<Item> Items, string Text)> Mailed = new();
        private readonly InventoryHelper native = new(null!, null!, null!, null!, null!, null!, null!, null!);
        public Backend() : base(null!, null!, null!, null!, null!, null!, null!) { }
        public override double Draw() => 0.5;
        public override SptProfile Profile(MongoId session) => session == Session ? Saved : null!;
        public override bool Eligible(MongoId template) => template != RubleTpl;
        public override bool IsWeapon(MongoId template) => false;
        public override bool IsArmorPart(MongoId template) => template == PlateTpl;
        public override HashSet<string> SlotNames(MongoId template) => template == VestTpl ? ["Front_plate"] : [];
        public override (double, string) Price(MongoId template, PriceBasis basis) =>
            (template == VestTpl ? VestPrice : template == PlateTpl ? 10000 : 1000, "북밸류");
        public override int PrestigeMax() => Stages;
        public override void RemoveMoney(PmcData profile, MongoId id, int count, MongoId session, ItemEventRouterResponse output) =>
            native.RemoveItemByCount(profile, id, count, session, output);
        public override void SendPrestigeReturn(MongoId session, List<Item> tree, string text) => Mail(session, tree, text);

        // MailSendService.SendMessageToPlayer as the real patch sees it: clone + ReplaceIDs.
        public List<Item> Mail(MongoId session, List<Item> tree, string text)
        {
            MailReturnPatch.Service = Service;
            var details = new SendMessageDetails { RecipientId = session, Items = tree };
            MailReturnPatch.Prefix(details);
            details.Items = Json.Deserialize<List<Item>>(Json.Serialize(tree)!)!.ReplaceIDs().ToList();
            MailReturnPatch.Postfix(details);
            Mailed.Add((details.Items, text));
            // Uncollected attachments stay tracked through the profile's dialogues.
            Saved.DialogueRecords![new MongoId()] = new Dialogue { Messages = [new() { Items = new() { Data = details.Items } }] };
            return details.Items;
        }
    }

    private sealed class Rig
    {
        public readonly Backend Backend = new();
        public readonly EnhanceService Service;
        public readonly PmcData Pmc = new() { Id = Session, Info = new() { PrestigeLevel = 1 },
            Inventory = new() { Stash = Stash, Items = [] }, InsuredItems = [] };
        public SptProfile Old = null!;
        public ItemEventRouterResponse Output() => new()
        {
            ProfileChanges = new() { [Session] = new() { Items = new() { NewItems = [], ChangedItems = [], DeletedItems = [] } } }
        };
        public Rig()
        {
            Pmc.Inventory!.Items!.Add(new Item { Id = Vest, Template = VestTpl, ParentId = Stash.ToString(), SlotId = "hideout",
                Upd = new() { Repairable = new() { Durability = 50, MaxDurability = 50 } } });
            Pmc.Inventory.Items.Add(new Item { Id = Plate, Template = PlateTpl, ParentId = Vest.ToString(), SlotId = "Front_plate",
                Upd = new() { Repairable = new() { Durability = 30, MaxDurability = 30 } } });
            Pmc.Inventory.Items.Add(new Item { Id = new("777777777777777777777777"), Template = RubleTpl, ParentId = Stash.ToString(),
                SlotId = "hideout", Upd = new() { StackObjectsCount = 50000000 } });
            Backend.Saved = new SptProfile { ProfileInfo = new() { ProfileId = Session }, CharacterData = new() { PmcData = Pmc },
                InsuranceList = [], DialogueRecords = new() };
            Service = new(Backend, Json);
            Backend.Service = Service;
        }
        public EnhanceState State => (EnhanceState)Backend.Saved.ExtensionData!["rfGearEnhance"]!;
        public static EnhanceSettings Sure => new() { AlwaysSucceed = true };
        public EnhanceQuote Quote(EnhanceAction action = EnhanceAction.Upgrade) =>
            Service.Quote(Session, Vest.ToString(), Sure, EnhancePath.ArmorDurability, null, action);
        public void Pay(EnhanceQuote quote) => Service.Execute(Session, Pmc, quote.QuoteId, false, Output());
        public void Upgrade(int times)
        {
            for (var n = 0; n < times; n++) Pay(Quote());
        }

        // CreateProfile: the old character is replaced by a new one before ProcessPendingPrestige.
        public PmcData Advance(int to, params string[] transfers) => Advance(to, true, transfers);

        // `obtained`: SPT keeps the prestige the player obtained pending on the old character.
        public PmcData Advance(int to, bool obtained, params string[] transfers)
        {
            Old = Backend.Saved;
            if (obtained) Old.SptData = new Spt { PendingPrestige = new PendingPrestige { PrestigeLevel = to } };
            var fresh = new PmcData { Id = Session, Info = new() { PrestigeLevel = to }, Inventory = new() { Stash = NewStash, Items = [] },
                InsuredItems = [] };
            Backend.Saved = new SptProfile { ProfileInfo = new() { ProfileId = Session }, CharacterData = new() { PmcData = fresh },
                InsuranceList = [], DialogueRecords = new() };
            var carry = Service.BeginPrestige(Old, Backend.Saved, to, transfers);
            // SPT's own transfer mail of the chosen items and their children.
            var moved = Pmc.Inventory!.Items!.Where(i => transfers.Contains(i.Id.ToString()) || transfers.Contains(i.ParentId)).ToList();
            if (moved.Count > 0) Backend.Mail(Session, moved, "");
            if (carry != null) Service.CompletePrestige(carry);
            return fresh;
        }
    }

    [Fact]
    public void Registration_charges_the_fee_then_only_the_difference()
    {
        var rig = new Rig();
        rig.Upgrade(2);
        var first = rig.Quote(EnhanceAction.PrestigeInsure);
        Assert.True(first.CanBuyPrestige);
        Assert.False(first.PrestigeRegistered);
        Assert.Equal(EnhanceRules.PrestigeFee(first.InsuredValue, Rig.Sure), first.Cost);
        Assert.Equal((int)Math.Ceiling(first.InsuredValue * 0.10), first.Cost);
        rig.Pay(first);
        Assert.Equal(first.Cost, rig.State.Prestige[Vest.ToString()].Paid);
        // Paid in full: nothing to buy until the value rises.
        Assert.False(rig.Quote(EnhanceAction.PrestigeInsure).CanBuyPrestige);
        rig.Upgrade(1);
        // Still registered after the upgrade; only the rise is charged.
        var renew = rig.Quote(EnhanceAction.PrestigeInsure);
        Assert.True(renew.PrestigeRegistered);
        Assert.True(renew.CanBuyPrestige);
        Assert.Equal(renew.PrestigeFee - first.Cost, renew.Cost);
        rig.Pay(renew);
        Assert.Equal(renew.PrestigeFee, rig.State.Prestige[Vest.ToString()].Paid);
        // A lower value asks nothing and refunds nothing.
        rig.Backend.VestPrice = 50000;
        var lower = rig.Quote(EnhanceAction.PrestigeInsure);
        Assert.False(lower.CanBuyPrestige);
        Assert.Equal(0, lower.Cost);
        Assert.Equal(renew.PrestigeFee, lower.PrestigePaid);
        // Prestige registration never lapses the enhancement insurance or registers plates.
        Assert.DoesNotContain(rig.State.Receipts, r => r.Action == EnhanceAction.PrestigeInsure && r.InsuranceLapsed);
    }

    [Fact]
    public void The_last_prestige_stage_cannot_register()
    {
        var rig = new Rig();
        rig.Upgrade(1);
        rig.Pmc.Info!.PrestigeLevel = 6;
        var quote = rig.Quote(EnhanceAction.PrestigeInsure);
        Assert.False(quote.CanBuyPrestige);
        Assert.Equal(6, quote.PrestigeLevel);
        Assert.Equal(6, quote.PrestigeMax);
        Assert.Throws<InvalidOperationException>(() => rig.Pay(quote));
        Assert.Empty(rig.State.Prestige);
    }

    [Fact]
    public void Advancing_mails_the_registered_gear_back_enhanced_and_ends_the_registration()
    {
        var rig = new Rig();
        rig.Upgrade(3);
        rig.Pay(rig.Quote(EnhanceAction.PrestigeInsure));
        var fresh = rig.Advance(2);
        var mail = Assert.Single(rig.Backend.Mailed);
        Assert.Equal(2, mail.Items.Count);
        Assert.Contains("프레스티지 2", mail.Text);
        var vest = mail.Items.Single(i => i.Template == VestTpl).Id.ToString();
        var plate = mail.Items.Single(i => i.Template == PlateTpl).Id.ToString();
        var state = rig.State;
        Assert.Equal(3, state.Tracks[vest][EnhancePath.ArmorDurability]);
        Assert.Contains(plate, state.Inserts[vest].Ids);
        Assert.Empty(state.Prestige);
        var snapshot = rig.Service.Snapshot(Session);
        var waiting = Assert.Single(snapshot.PrestigeReturns);
        Assert.Equal(vest, waiting.ItemId);
        Assert.Equal(2, waiting.Level);
        Assert.Equal(2, snapshot.PrestigeLevel);
        // Collected: the return leaves the list and the enhancement shows on the stash copy.
        foreach (var item in mail.Items) fresh.Inventory!.Items!.Add(item);
        mail.Items.Single(i => i.Id.ToString() == vest).ParentId = NewStash.ToString();
        var after = rig.Service.Snapshot(Session);
        Assert.Empty(after.PrestigeReturns);
        Assert.Contains(after.Items, r => r.ItemId == vest && r.Level == 3);
    }

    [Fact]
    public void An_unpaid_rise_returns_the_gear_as_it_was_when_last_paid_for()
    {
        var rig = new Rig();
        rig.Upgrade(2);
        rig.Pay(rig.Quote(EnhanceAction.PrestigeInsure));
        rig.Upgrade(2);
        rig.Advance(2);
        var vest = rig.Backend.Mailed.Single().Items.Single(i => i.Template == VestTpl).Id.ToString();
        Assert.Equal(2, rig.State.Tracks[vest][EnhancePath.ArmorDurability]);
    }

    [Fact]
    public void Unregistered_gear_and_a_stage_that_does_not_advance_return_nothing()
    {
        var rig = new Rig();
        rig.Upgrade(2);
        rig.Advance(2);
        Assert.Empty(rig.Backend.Mailed);

        var same = new Rig();
        same.Upgrade(2);
        same.Pay(same.Quote(EnhanceAction.PrestigeInsure));
        same.Advance(1);
        Assert.Empty(same.Backend.Mailed);
    }

    [Fact]
    public void A_profile_created_at_a_forced_level_returns_nothing()
    {
        var rig = new Rig();
        rig.Upgrade(2);
        rig.Pay(rig.Quote(EnhanceAction.PrestigeInsure));
        rig.Advance(2, false);
        Assert.Empty(rig.Backend.Mailed);
        var skip = new Rig();
        skip.Upgrade(2);
        skip.Pay(skip.Quote(EnhanceAction.PrestigeInsure));
        skip.Advance(3);
        Assert.Empty(skip.Backend.Mailed);
    }

    [Fact]
    public void An_unpaid_transcendence_rise_takes_its_added_max_durability_off_the_returned_armor()
    {
        var rig = new Rig();
        rig.Upgrade(2);
        rig.Pay(rig.Quote(EnhanceAction.PrestigeInsure));
        var vest = rig.Pmc.Inventory!.Items!.Single(i => i.Id == Vest);
        rig.State.Transcend[Vest.ToString()] = new TranscendRecord { Second = 2 };
        rig.State.Grants[Vest.ToString()] = new DurabilityGrant { Amount = 10, ArmorId = Vest.ToString() };
        vest.Upd!.Repairable!.MaxDurability = 60;
        vest.Upd.Repairable.Durability = 60;
        rig.Advance(2);
        var mailed = rig.Backend.Mailed.Single().Items.Single(i => i.Template == VestTpl);
        Assert.Equal(50, mailed.Upd!.Repairable!.MaxDurability);
        Assert.False(rig.State.Grants.ContainsKey(mailed.Id.ToString()));
        Assert.False(rig.State.Transcend.ContainsKey(mailed.Id.ToString()));
    }

    [Fact]
    public void Gear_SPT_transfers_itself_is_not_mailed_twice_but_keeps_its_enhancement()
    {
        var rig = new Rig();
        rig.Upgrade(2);
        rig.Pay(rig.Quote(EnhanceAction.PrestigeInsure));
        rig.Advance(2, Vest.ToString());
        var mail = Assert.Single(rig.Backend.Mailed);
        var vest = mail.Items.Single(i => i.Template == VestTpl).Id.ToString();
        Assert.Equal(2, rig.State.Tracks[vest][EnhancePath.ArmorDurability]);
        Assert.Equal(vest, Assert.Single(rig.Service.Snapshot(Session).PrestigeReturns).ItemId);
    }

    [Fact]
    public void A_registration_made_with_the_developer_options_on_does_not_carry_over()
    {
        var rig = new Rig();
        rig.Upgrade(2);
        var before = Json.Serialize<EnhanceRecords>(rig.State)!;
        rig.Pay(rig.Quote(EnhanceAction.PrestigeInsure));
        rig.State.Sandbox = new DeveloperSandbox { LaunchId = "test", Records = before };
        rig.Advance(2);
        Assert.Empty(rig.Backend.Mailed);
    }

    [Fact]
    public void Snapshot_reports_registered_gear_with_its_difference()
    {
        var rig = new Rig();
        rig.Upgrade(1);
        var paid = rig.Quote(EnhanceAction.PrestigeInsure);
        rig.Pay(paid);
        rig.Upgrade(1);
        var link = Assert.Single(rig.Service.Snapshot(Session).Prestige);
        Assert.Equal(Vest.ToString(), link.ItemId);
        Assert.Equal(paid.Cost, link.Paid);
        Assert.Equal(paid.InsuredValue, link.Value);
        Assert.True(link.Fee > link.Paid);
    }
}
