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

// 1.2.0: armor priced and registered with the plates in its slots, enhancement
// insurance tiers, and what happens to recorded gear lost in a raid.
[Collection("MailReturnPatch")]
public sealed class InsuranceTests
{
    private static readonly MongoId Session = new("111111111111111111111111");
    private static readonly MongoId Stash = new("222222222222222222222222");
    private static readonly MongoId Vest = new("333333333333333333333333");
    private static readonly MongoId Plate = new("444444444444444444444444");
    private static readonly MongoId Pocket = new("555555555555555555555555");
    private static readonly MongoId Spare = new("666666666666666666666666");
    private static readonly MongoId VestTpl = new("aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly MongoId CheapPlateTpl = new("bbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly MongoId DearPlateTpl = new("cccccccccccccccccccccccc");
    private static readonly MongoId LootTpl = new("dddddddddddddddddddddddd");
    private static readonly MongoId RubleTpl = new("5449016a4bdc2d6f028b456f");
    private static readonly JsonUtil Json = new([new SptJsonConverterRegistrator()]);

    private sealed class Backend : SptEnhanceBackend
    {
        public SptProfile Saved = null!;
        public EnhanceService Service = null!;
        public double DrawValue = 0.5;
        public readonly List<List<Item>> Mailed = new();
        private readonly InventoryHelper native = new(null!, null!, null!, null!, null!, null!, null!, null!);
        public Backend() : base(null!, null!, null!, null!, null!, null!) { }
        public override double Draw() => DrawValue;
        public override SptProfile Profile(MongoId session) => session == Session ? Saved : null!;
        public override bool Eligible(MongoId template) => template != LootTpl && template != RubleTpl;
        public override bool IsWeapon(MongoId template) => false;
        public override bool IsArmorPart(MongoId template) => template == CheapPlateTpl || template == DearPlateTpl;
        public override HashSet<string> SlotNames(MongoId template) => template == VestTpl ? ["Front_plate", "Back_plate"] : [];
        public override (double, string) Price(MongoId template, PriceBasis basis) =>
            (template == VestTpl ? 100000 : template == CheapPlateTpl ? 10000 : template == DearPlateTpl ? 50000 : 1000, "북밸류");
        public override void RemoveMoney(PmcData profile, MongoId id, int count, MongoId session, ItemEventRouterResponse output) =>
            native.RemoveItemByCount(profile, id, count, session, output);
        public override void Destroy(PmcData profile, MongoId id, MongoId session, ItemEventRouterResponse output) =>
            native.RemoveItem(profile, id, session, output);
        // MailSendService.SendMessageToPlayer as the real patch sees it: clone + ReplaceIDs.
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
        public ItemEventRouterResponse Output() => new()
        {
            ProfileChanges = new() { [Session] = new() { Items = new() { NewItems = [], ChangedItems = [], DeletedItems = [] } } }
        };
        public Rig()
        {
            Pmc.Inventory!.Items!.Add(new Item { Id = Vest, Template = VestTpl, ParentId = Stash.ToString(), SlotId = "hideout",
                Upd = new() { Repairable = new() { Durability = 50, MaxDurability = 50 } } });
            Pmc.Inventory.Items.Add(new Item { Id = Plate, Template = CheapPlateTpl, ParentId = Vest.ToString(), SlotId = "Front_plate",
                Upd = new() { Repairable = new() { Durability = 30, MaxDurability = 30 } } });
            Pmc.Inventory.Items.Add(new Item { Id = Spare, Template = DearPlateTpl, ParentId = Stash.ToString(), SlotId = "hideout",
                Upd = new() { Repairable = new() { Durability = 40, MaxDurability = 40 } } });
            Pmc.Inventory.Items.Add(new Item { Id = new("777777777777777777777777"), Template = RubleTpl, ParentId = Stash.ToString(),
                SlotId = "hideout", Upd = new() { StackObjectsCount = 50000000 } });
            Backend.Saved = new SptProfile { CharacterData = new() { PmcData = Pmc }, InsuranceList = [], DialogueRecords = new() };
            Service = new(Backend, Json);
            Backend.Service = Service;
        }
        public EnhanceState State => (EnhanceState)Backend.Saved.ExtensionData!["rfGearEnhance"]!;
        public static EnhanceSettings Sure => new() { AlwaysSucceed = true };
        public EnhanceQuote Quote(EnhanceAction action = EnhanceAction.Upgrade, InsuranceTier tier = InsuranceTier.None,
            EnhancePath path = EnhancePath.ArmorDurability) =>
            Service.Quote(Session, Vest.ToString(), Sure, path, null, action, false, tier);
        public void Pay(EnhanceQuote quote) => Service.Execute(Session, Pmc, quote.QuoteId, false, Output());
        public void Upgrade(int times, EnhancePath path = EnhancePath.ArmorDurability)
        {
            for (var n = 0; n < times; n++) Pay(Quote(path: path));
        }
        // Swap the inserted plate for the expensive spare.
        public void SwapPlate()
        {
            var cheap = Pmc.Inventory!.Items!.Single(i => i.Id == Plate);
            var dear = Pmc.Inventory!.Items!.Single(i => i.Id == Spare);
            cheap.ParentId = Stash.ToString(); cheap.SlotId = "hideout";
            dear.ParentId = Vest.ToString(); dear.SlotId = "Front_plate";
        }
        public List<Item> Lose()
        {
            var tree = Pmc.Inventory!.Items!.Where(i => i.Id == Vest || i.ParentId == Vest.ToString()).ToList();
            Pmc.Inventory!.Items!.RemoveAll(tree.Contains);
            return tree;
        }
    }

    [Fact]
    public void Armor_price_includes_the_inserted_plate_and_registers_it()
    {
        var rig = new Rig();
        var quote = rig.Quote();
        Assert.Equal(10000, quote.PartsPrice);
        Assert.Equal(1, quote.InsertedParts);
        Assert.Equal(1, quote.UnregisteredParts);
        Assert.Equal(EnhanceRules.Cost(110000, 0, Rig.Sure), quote.Cost);
        rig.Pay(quote);
        Assert.Contains(Plate.ToString(), rig.State.Inserts[Vest.ToString()].Ids);
        var snapshot = rig.Service.Snapshot(Session);
        Assert.Contains(snapshot.Inserts, link => link.ArmorId == Vest.ToString() && link.PartIds.Contains(Plate.ToString()));
    }

    [Fact]
    public void Applied_plates_never_owe_a_difference_after_prices_move()
    {
        // In game 2026-10-24: every plate applied, yet the paid set priced lower than the
        // current one (market move), so insurance asked for a difference no button could pay.
        var rig = new Rig();
        rig.Upgrade(3);
        rig.State.Inserts[Vest.ToString()].PaidTemplates = [];
        var insure = rig.Quote(EnhanceAction.Insure, InsuranceTier.Basic);
        Assert.Equal(0, insure.UnregisteredParts);
        Assert.Equal(0, insure.RegistrationFee);
        Assert.True(insure.CanBuyInsurance);
        Assert.Equal(0, rig.Quote().InsertSurcharge);
    }

    [Fact]
    public void Pricier_plate_swapped_in_later_pays_the_difference_for_every_level_held()
    {
        var rig = new Rig();
        rig.Upgrade(3);
        rig.SwapPlate();
        var quote = rig.Quote();
        Assert.Equal(1, quote.UnregisteredParts);
        var catchUp = EnhanceRules.InsertSurcharge(40000, new Dictionary<EnhancePath, int> { [EnhancePath.ArmorDurability] = 3 }, 1, Rig.Sure);
        Assert.True(catchUp > 0);
        Assert.Equal(catchUp, quote.InsertSurcharge);
        Assert.Equal(EnhanceRules.Cost(150000, 3, Rig.Sure) + catchUp, quote.Cost);
        Assert.Equal(0, quote.ProtectionCost);

        // A maxed or unwanted upgrade is not required: registration charges the catch-up alone.
        var insure = rig.Quote(EnhanceAction.Insure, InsuranceTier.Premium);
        Assert.Equal(catchUp, insure.RegistrationFee);
        Assert.False(insure.CanBuyInsurance);
        // The insurance · plates window loads this quote and enables its plate button from it.
        Assert.True(insure.CanRegister);
        var register = rig.Quote(EnhanceAction.RegisterInserts);
        Assert.True(register.CanRegister);
        Assert.Equal(catchUp, register.Cost);
        rig.Pay(register);
        Assert.Contains(Spare.ToString(), rig.State.Inserts[Vest.ToString()].Ids);
        Assert.Equal([DearPlateTpl.ToString()], rig.State.Inserts[Vest.ToString()].PaidTemplates);
        // Paid up: the cheap plate going back in costs nothing extra and is still registered.
        var again = rig.Quote(EnhanceAction.Insure, InsuranceTier.Premium);
        Assert.Equal(0, again.RegistrationFee);
        Assert.Equal(0, again.UnregisteredParts);
        Assert.Equal(3, rig.State.Tracks[Vest.ToString()][EnhancePath.ArmorDurability]);
    }

    [Fact]
    public void Armor_upgrade_never_raises_a_separately_enhanced_plate()
    {
        var rig = new Rig();
        rig.Upgrade(1);
        rig.State.Tracks[Plate.ToString()] = new() { [EnhancePath.ArmorDurability] = 4 };
        rig.Upgrade(1);
        Assert.Equal(2, rig.State.Tracks[Vest.ToString()][EnhancePath.ArmorDurability]);
        Assert.Equal(4, rig.State.Tracks[Plate.ToString()][EnhancePath.ArmorDurability]);
        // 1.2.0: the plate's own (pre-1.2.0) record is kept but gives no effect, so the
        // client only sees the armor's level on it, and it cannot be enhanced alone.
        var snapshot = rig.Service.Snapshot(Session);
        Assert.DoesNotContain(snapshot.Items, record => record.ItemId == Plate.ToString());
        Assert.Contains(snapshot.Items, record => record.ItemId == Vest.ToString() && record.Level == 2);
        Assert.Contains(snapshot.Inserts, link => link.ArmorId == Vest.ToString() && link.PartIds.Contains(Plate.ToString()));
    }

    [Fact]
    public void Plates_and_inserts_cannot_be_enhanced_or_insured_alone()
    {
        var rig = new Rig();
        foreach (var action in new[] { EnhanceAction.Upgrade, EnhanceAction.Insure, EnhanceAction.RegisterInserts })
        {
            var error = Assert.ThrowsAny<Exception>(() => rig.Service.Quote(Session, Spare.ToString(), Rig.Sure,
                EnhancePath.ArmorDurability, null, action, false, InsuranceTier.Basic));
            Assert.Contains("따로 강화할 수 없습니다", error.Message);
        }
        // The vest itself is unaffected.
        Assert.True(rig.Quote().Cost > 0);
    }

    [Fact]
    public void Armor_enhanced_before_1_2_0_keeps_its_plates_free()
    {
        var rig = new Rig();
        rig.Backend.Saved.ExtensionData = new()
        {
            ["rfGearEnhance"] = new EnhanceState { Tracks = new() { [Vest.ToString()] = new() { [EnhancePath.ArmorDurability] = 5 } } }
        };
        var snapshot = rig.Service.Snapshot(Session);
        Assert.Contains(snapshot.Inserts, link => link.ArmorId == Vest.ToString() && link.PartIds.Contains(Plate.ToString()));
        Assert.Equal(0, rig.Quote().InsertSurcharge);
    }

    [Fact]
    public void Insurance_fee_follows_enhancement_value_and_tier()
    {
        var rig = new Rig();
        Assert.Throws<InvalidOperationException>(() => rig.Quote(EnhanceAction.Insure, InsuranceTier.Basic));
        rig.Upgrade(2);
        var low = rig.Quote(EnhanceAction.Insure, InsuranceTier.Advanced);
        Assert.Equal(EnhanceRules.Value(110000, new Dictionary<EnhancePath, int> { [EnhancePath.ArmorDurability] = 2 }, 1, Rig.Sure), low.InsuredValue);
        Assert.True(low.InsuranceFees[1] < low.InsuranceFees[2] && low.InsuranceFees[2] < low.InsuranceFees[3]);
        Assert.Equal([0, 20, 55, 90], low.InsuranceChances);
        // Two armor tracks at different levels (the quote and the payment check must agree).
        rig.Upgrade(4, EnhancePath.Mobility);
        var high = rig.Quote(EnhanceAction.Insure, InsuranceTier.Advanced);
        Assert.True(high.InsuredValue > low.InsuredValue);
        Assert.True(high.InsuranceFees[2] > low.InsuranceFees[2]);
        Assert.True(high.CanBuyInsurance);
        rig.Pay(high);
        Assert.Equal(InsuranceTier.Advanced, rig.State.Coverage[Vest.ToString()].Tier);
        Assert.Equal(55, rig.State.Coverage[Vest.ToString()].ReturnPercent);
        // The stash label reads the tier from the snapshot.
        Assert.Contains(rig.Service.Snapshot(Session).Coverage, link => link.ItemId == Vest.ToString() && link.Tier == InsuranceTier.Advanced);
        // Same or lower tier cannot be bought again; a higher one can.
        Assert.False(rig.Quote(EnhanceAction.Insure, InsuranceTier.Basic).CanBuyInsurance);
        Assert.True(rig.Quote(EnhanceAction.Insure, InsuranceTier.Premium).CanBuyInsurance);
        // The receipt never feeds the 1.0.0 receipt recovery as a track level.
        Assert.Equal(EnhanceAction.Insure, rig.State.Receipts[^1].Action);
        // A successful upgrade raises the value, so the cover ends and must be bought again.
        rig.Upgrade(1);
        Assert.False(rig.State.Coverage.ContainsKey(Vest.ToString()));
    }

    [Fact]
    public void Insured_armor_lost_in_raid_comes_back_enhanced_with_its_plate_and_no_trader_copy()
    {
        var rig = new Rig();
        rig.Upgrade(3);
        rig.Pay(rig.Quote(EnhanceAction.Insure, InsuranceTier.Premium));
        // Loot in a pocket of the vest is not a slot part and is never returned.
        rig.Pmc.Inventory!.Items!.Add(new Item { Id = Pocket, Template = LootTpl, ParentId = Vest.ToString(), SlotId = "1" });
        var capture = rig.Service.BeginRaidEnd(Session);
        Assert.NotNull(capture);
        var lost = rig.Lose();
        rig.Backend.Saved.InsuranceList!.Add(new Insurance { TraderId = new MongoId(), Items = lost.Select(i => i).ToList() });

        rig.Backend.DrawValue = 0.85;
        Assert.Equal((1, 0), rig.Service.CompleteRaidEnd(Session, capture!, null));
        var mailed = Assert.Single(rig.Backend.Mailed);
        Assert.Equal(2, mailed.Count);
        Assert.DoesNotContain(mailed, i => i.Template == LootTpl);
        var newVest = mailed.Single(i => i.Template == VestTpl).Id.ToString();
        var newPlate = mailed.Single(i => i.Template == CheapPlateTpl).Id.ToString();
        Assert.Equal(3, rig.State.Tracks[newVest][EnhancePath.ArmorDurability]);
        Assert.Contains(newPlate, rig.State.Inserts[newVest].Ids);
        Assert.Empty(rig.State.Coverage);
        // Only the pocket loot is left for the trader; no second vest or plate.
        var package = Assert.Single(rig.Backend.Saved.InsuranceList);
        Assert.Equal([Pocket], package.Items!.Select(i => i.Id).ToList());
    }

    [Fact]
    public void Failed_roll_forfeits_the_enhancement_and_the_trader_returns_it_as_plus_zero()
    {
        var rig = new Rig();
        rig.Upgrade(3);
        rig.Pay(rig.Quote(EnhanceAction.Insure, InsuranceTier.Basic));
        var capture = rig.Service.BeginRaidEnd(Session);
        var lost = rig.Lose();
        rig.Backend.Saved.InsuranceList!.Add(new Insurance { TraderId = new MongoId(), Items = lost });

        rig.Backend.DrawValue = 0.5;
        Assert.Equal((0, 1), rig.Service.CompleteRaidEnd(Session, capture!, null));
        Assert.Empty(rig.Backend.Mailed);
        Assert.False(rig.State.Tracks.ContainsKey(Vest.ToString()));
        Assert.Contains(Vest.ToString(), rig.State.Forfeited);
        Assert.Equal(2, rig.Backend.Saved.InsuranceList.Single().Items!.Count);
        // The trader's return mail gets new ids; neither records nor receipts move to them.
        rig.Backend.SendReturn(Session, rig.Backend.Saved.InsuranceList.Single().Items!, "trader");
        var returned = rig.Backend.Mailed.Single();
        Assert.All(returned, i => Assert.False(rig.State.Tracks.ContainsKey(i.Id.ToString())));
    }

    [Fact]
    public void Uninsured_loss_forfeits_and_gear_kept_after_the_raid_is_untouched()
    {
        var rig = new Rig();
        rig.Upgrade(2);
        var capture = rig.Service.BeginRaidEnd(Session);
        Assert.Equal((0, 0), rig.Service.CompleteRaidEnd(Session, capture!, null));
        Assert.Equal(2, rig.State.Tracks[Vest.ToString()][EnhancePath.ArmorDurability]);
        capture = rig.Service.BeginRaidEnd(Session);
        rig.Lose();
        Assert.Equal((0, 1), rig.Service.CompleteRaidEnd(Session, capture!, null));
        Assert.Empty(rig.Backend.Mailed);
        Assert.Empty(rig.State.Tracks);
    }

    [Fact]
    public void Raid_end_patch_targets_the_real_spt_post_raid_method()
    {
        var target = typeof(SPTarkov.Server.Core.Services.InRaid.LocationLifecycleService).GetMethod("HandlePostRaidPmc",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(target);
        var names = target!.GetParameters().Select(p => p.Name).ToArray();
        Assert.Contains("sessionId", names);
        Assert.Contains("request", names);
        // The insurance packages are queued inside it, so the postfix can strip them.
        using var core = Mono.Cecil.AssemblyDefinition.ReadAssembly(target.DeclaringType!.Assembly.Location);
        var method = core.MainModule.GetType(target.DeclaringType.FullName).Methods.Single(m => m.Name == "HandlePostRaidPmc");
        Assert.Contains(method.Body.Instructions, i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "HandleInsuredItemLostEvent");
        Assert.Contains(method.Body.Instructions, i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "DeleteInventory");
    }
}
