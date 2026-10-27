using GearEnhance;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Items;
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

// 1.3.0 transcendence on the server: tracks, pity, drops, materials, and the armor
// 2nd track's real max durability (grant, revert, plate swaps, raid loss, prices).
[Collection("MailReturnPatch")]
public sealed class TranscendTests
{
    private static readonly MongoId Session = new("111111111111111111111111");
    private static readonly MongoId Stash = new("222222222222222222222222");
    private static readonly MongoId Vest = new("333333333333333333333333");
    private static readonly MongoId Plate = new("444444444444444444444444");
    private static readonly MongoId Spare = new("666666666666666666666666");
    private static readonly MongoId Gun = new("888888888888888888888888");
    private static readonly MongoId Other = new("555555555555555555555555");
    private static readonly MongoId Twin = new("aaaaaaaaaaaaaaaaaaaaaaab");
    private static readonly MongoId VestTpl = new("aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly MongoId CheapPlateTpl = new("bbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly MongoId DearPlateTpl = new("cccccccccccccccccccccccc");
    private static readonly MongoId GunTpl = new("eeeeeeeeeeeeeeeeeeeeeeee");
    private static readonly MongoId RubleTpl = new("5449016a4bdc2d6f028b456f");
    private static readonly JsonUtil Json = new([new SptJsonConverterRegistrator()]);
    private static readonly string[] MaterialTemplates =
    [
        TranscendRules.WeaponParts, TranscendRules.GunpowderEagle, TranscendRules.AramidFabric, TranscendRules.RipstopFabric,
        TranscendRules.MilitaryBoard, TranscendRules.Virtex, TranscendRules.GraphicsCard, TranscendRules.Ledx, SuccessBoost.BitcoinTemplate
    ];

    private sealed class Backend : SptEnhanceBackend
    {
        public SptProfile Saved = null!;
        public EnhanceService Service = null!;
        public double DrawValue;
        public bool AutomaticGun = true;
        public readonly List<List<Item>> Mailed = new();
        private readonly InventoryHelper native = new(null!, null!, null!, null!, null!, null!, null!, null!);
        public Backend() : base(null!, null!, null!, null!, null!, null!, null!) { }
        public override double Draw() => DrawValue;
        public override SptProfile Profile(MongoId session) => session == Session ? Saved : null!;
        public override IEnumerable<(MongoId Session, SptProfile Profile)> Profiles() => [(Session, Saved)];
        public override bool Eligible(MongoId template) => template == VestTpl || template == CheapPlateTpl || template == DearPlateTpl || template == GunTpl;
        public override bool IsWeapon(MongoId template) => template == GunTpl;
        public override bool Automatic(MongoId template) => template == GunTpl && AutomaticGun;
        public override bool IsArmorPart(MongoId template) => template == CheapPlateTpl || template == DearPlateTpl;
        public override double TemplateMaxDurability(MongoId template) =>
            template == VestTpl ? 50 : template == CheapPlateTpl ? 30 : template == DearPlateTpl ? 40 : template == GunTpl ? 100 : 0;
        public override HashSet<string> SlotNames(MongoId template) => template == VestTpl ? ["Front_plate", "Back_plate"] : [];
        public override (double, string) Price(MongoId template, PriceBasis basis) =>
            (template == VestTpl ? 100000 : template == CheapPlateTpl ? 10000 : template == DearPlateTpl ? 50000
                : template == GunTpl ? 200000 : 1000, "북밸류");
        public override void RemoveMoney(PmcData profile, MongoId id, int count, MongoId session, ItemEventRouterResponse output) =>
            native.RemoveItemByCount(profile, id, count, session, output);
        public override void RemoveMaterial(PmcData profile, MongoId id, int count, MongoId session, ItemEventRouterResponse output) =>
            native.RemoveItemByCount(profile, id, count, session, output);
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
        public ItemEventRouterResponse LastOutput = null!;
        public EnhanceState State => (EnhanceState)Backend.Saved.ExtensionData!["rfGearEnhance"]!;
        public EnhanceSettings Settings = new() { AlwaysSucceed = true };

        public Rig(int stars = 2, int materials = 20)
        {
            Add(Vest, VestTpl, Stash, "hideout", 50);
            Add(Plate, CheapPlateTpl, Vest, "Front_plate", 30);
            Add(Spare, DearPlateTpl, Stash, "hideout", 40);
            Add(Gun, GunTpl, Stash, "hideout", 100);
            Pmc.Inventory!.Items!.Add(new Item { Id = new("777777777777777777777777"), Template = RubleTpl, ParentId = Stash.ToString(),
                SlotId = "hideout", Upd = new() { StackObjectsCount = 90000000 } });
            var n = 0;
            foreach (var template in MaterialTemplates)
                Pmc.Inventory.Items.Add(new Item { Id = new($"9{n++:x}{new string('9', 22)}"), Template = new MongoId(template),
                    ParentId = Stash.ToString(), SlotId = "hideout", Upd = new() { StackObjectsCount = materials } });
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

        public void Add(MongoId id, MongoId template, MongoId parent, string slot, double durability) =>
            Pmc.Inventory!.Items!.Add(new Item { Id = id, Template = template, ParentId = parent.ToString(), SlotId = slot,
                Upd = new() { Repairable = new() { Durability = durability, MaxDurability = durability } } });

        public Item Item(MongoId id) => Pmc.Inventory!.Items!.Single(i => i.Id == id);
        public UpdRepairable Rep(MongoId id) => Item(id).Upd!.Repairable!;
        public long Stock(string template) => (long)Pmc.Inventory!.Items!.Where(i => i.Template.ToString() == template)
            .Sum(i => i.Upd?.StackObjectsCount ?? 1);
        public long Roubles => Stock(RubleTpl.ToString());

        public EnhanceQuote Quote(TranscendTrack track, MongoId? item = null, bool protect = false, bool boost = false) =>
            Service.Quote(Session, (item ?? Vest).ToString(), Settings, null, new EnhanceProtection { Downgrade = protect },
                EnhanceAction.Transcend, boost ? new QuoteOptions { Boost = [new BoostChoice { Kind = BoostMaterial.Bitcoin, Count = 1 }] } : null,
                InsuranceTier.None, track);
        public EnhanceQuote Manage(EnhanceAction action, InsuranceTier tier = InsuranceTier.None) =>
            Service.Quote(Session, Vest.ToString(), Settings, null, null, action, null, tier);
        public void Pay(EnhanceQuote quote)
        {
            LastOutput = new() { ProfileChanges = new() { [Session] = new() { Items = new() { NewItems = [], ChangedItems = [], DeletedItems = [] } } } };
            Service.Execute(Session, Pmc, quote.QuoteId, false, LastOutput);
        }
        public List<Item> Changed => LastOutput.ProfileChanges![Session]!.Items!.ChangedItems!;
        public void Transcend(TranscendTrack track, int times, MongoId? item = null)
        {
            for (var n = 0; n < times; n++) Pay(Quote(track, item));
        }
        // The item event the client sends after a plate goes into armor.
        public (int Registered, int Revoked) Sync(bool autoRegister = true)
        {
            LastOutput = new() { ProfileChanges = new() { [Session] = new() { Items = new() { NewItems = [], ChangedItems = [], DeletedItems = [] } } } };
            return Service.ReconcileInserts(Session, Pmc, Settings, autoRegister, LastOutput);
        }
        public void Move(MongoId id, MongoId parent, string slot)
        {
            var item = Item(id);
            item.ParentId = parent.ToString();
            item.SlotId = slot;
        }
        public List<Item> Lose()
        {
            var tree = Pmc.Inventory!.Items!.Where(i => i.Id == Vest || i.ParentId == Vest.ToString()).ToList();
            Pmc.Inventory!.Items!.RemoveAll(tree.Contains);
            return tree;
        }
    }

    [Fact]
    public void Successful_transcendence_lapses_insurance_and_keeps_the_paid_credit()
    {
        var rig = new Rig();
        var insure = rig.Manage(EnhanceAction.Insure, InsuranceTier.Advanced);
        rig.Pay(insure);
        rig.Pay(rig.Quote(TranscendTrack.Second));
        Assert.True(rig.State.Receipts[^1].InsuranceLapsed);
        Assert.False(rig.State.Coverage.ContainsKey(Vest.ToString()));
        var renewal = rig.Manage(EnhanceAction.Insure, InsuranceTier.Advanced);
        Assert.True(renewal.InsuranceLapsed);
        Assert.Equal(insure.Cost, renewal.PreviousInsurancePaid);
        Assert.Equal(Math.Max(0, EnhanceRules.InsuranceFee(renewal.InsuredValue, InsuranceTier.Advanced, rig.Settings) - insure.Cost), renewal.Cost);
    }

    [Fact]
    public void Rules_match_the_design_tables()
    {
        var tuning = new TranscendTuning();
        Assert.Equal([60, 50, 40, 30, 20], Enumerable.Range(0, 5).Select(l => TranscendRules.Success(TranscendTrack.Second, l, tuning)));
        Assert.Equal([40, 30, 20, 15, 10], Enumerable.Range(0, 5).Select(l => TranscendRules.Success(TranscendTrack.Third, l, tuning)));
        // max(minimum, price × percent × 1.5^level)
        Assert.Equal(1000000, TranscendRules.Cost(110000, TranscendTrack.Second, 0, tuning));
        Assert.Equal(3375000, TranscendRules.Cost(2000000, TranscendTrack.Second, 3, tuning));
        Assert.Equal(3000000, TranscendRules.Cost(200000, TranscendTrack.Third, 4, tuning));
        Assert.Equal(5062500, TranscendRules.Cost(1000000, TranscendTrack.Third, 4, tuning));
        Assert.Throws<InvalidOperationException>(() => TranscendRules.Cost(100000000, TranscendTrack.Third, 4, tuning));
        Assert.False(TranscendRules.DropsOnFailure(3));
        Assert.True(TranscendRules.DropsOnFailure(4));
        Assert.Equal([(TranscendRules.WeaponParts, 3), (TranscendRules.GraphicsCard, 1)], TranscendRules.Materials(true, TranscendTrack.Second, 5));
        Assert.Equal([(TranscendRules.AramidFabric, 2), (TranscendRules.RipstopFabric, 1)], TranscendRules.Materials(false, TranscendTrack.Second, 3));
        Assert.Equal([(TranscendRules.Virtex, 1), (TranscendRules.GraphicsCard, 1)], TranscendRules.Materials(true, TranscendTrack.Third, 4));
        Assert.Equal([(TranscendRules.GraphicsCard, 2), (TranscendRules.Ledx, 1)], TranscendRules.Materials(false, TranscendTrack.Third, 5));
        Assert.Empty(TranscendRules.Materials(false, TranscendTrack.Third, 6));
    }

    [Fact]
    public void Effects_follow_the_design_with_class_cap_substitution()
    {
        var tuning = new TranscendTuning();
        Assert.Equal(30, TranscendEffects.HorizontalRecoil(5, tuning));
        Assert.Equal(30, TranscendEffects.MaxDurability(9, tuning));
        // +min(50 per level, 6% of the base rate per level)
        Assert.Equal(36, TranscendEffects.FireRateBonus(600, 1, tuning), 6);
        Assert.Equal(50, TranscendEffects.FireRateBonus(1200, 1, tuning), 6);
        Assert.Equal(250, TranscendEffects.FireRateBonus(1200, 5, tuning), 6);
        Assert.Equal(50, TranscendEffects.Malfunction(5, tuning));
        Assert.Equal(40, TranscendEffects.Overheat(5, tuning));
        Assert.Equal(4, TranscendEffects.ArmorClass(2, 4));
        Assert.Equal(5, TranscendEffects.ArmorClass(3, 4));
        Assert.Equal(6, TranscendEffects.ArmorClass(5, 4));
        Assert.Equal(6, TranscendEffects.ArmorClass(5, 5));
        Assert.Equal(7, TranscendEffects.ArmorClass(5, 7));
        Assert.Equal(25, TranscendEffects.Blunt(5, 4, tuning));
        // Class 5 takes +1 of the +2, class 6 takes none: -10% per class not taken.
        Assert.Equal(35, TranscendEffects.Blunt(5, 5, tuning));
        Assert.Equal(45, TranscendEffects.Blunt(5, 6, tuning));
        Assert.Equal(25, TranscendEffects.Blunt(3, 6, tuning));
        Assert.Equal(TranscendKind.FireRate, TranscendEffects.Kind(true, TranscendTrack.Third, true));
        Assert.Equal(TranscendKind.Reliability, TranscendEffects.Kind(true, TranscendTrack.Third, false));
        Assert.Equal(TranscendKind.MaxDurability, TranscendEffects.Kind(false, TranscendTrack.Second, false));
    }

    [Fact]
    public void Tuning_out_of_range_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new EnhanceSettings { Transcend = new() { SecondSuccess = [60, 50, 40, 30] } }.Validate());
        Assert.Throws<ArgumentException>(() => new EnhanceSettings { Transcend = new() { HorizontalRecoilPerLevel = 17 } }.Validate());
        Assert.Throws<ArgumentException>(() => new EnhanceSettings { Transcend = null! }.Validate());
        new EnhanceSettings().Validate();
        Assert.Equal(13, EnhanceProtocol.Version);
    }

    [Fact]
    public void Tracks_need_their_star_level()
    {
        var one = new Rig(stars: 1);
        Assert.Throws<InvalidOperationException>(() => one.Quote(TranscendTrack.Second));
        var two = new Rig(stars: 2);
        Assert.Equal(0, two.Quote(TranscendTrack.Second).CurrentLevel);
        Assert.Throws<InvalidOperationException>(() => two.Quote(TranscendTrack.Third));
        Assert.Throws<ArgumentException>(() => two.Quote(TranscendTrack.None));
        var three = new Rig(stars: 3);
        Assert.Equal(TranscendKind.ArmorClass, three.Quote(TranscendTrack.Third).TranscendKind);
    }

    [Fact]
    public void Armor_second_success_raises_max_durability_of_the_armor_and_registered_plate()
    {
        var rig = new Rig();
        var quote = rig.Quote(TranscendTrack.Second);
        Assert.Equal(TranscendKind.MaxDurability, quote.TranscendKind);
        Assert.Equal(1000000, quote.Cost);
        Assert.Equal(100, quote.SuccessPercent);
        Assert.Equal(6, quote.NextEffect);
        var material = Assert.Single(quote.Materials);
        Assert.Equal((TranscendRules.AramidFabric, 1, 20L), (material.Template, material.Count, material.Owned));
        Assert.True(quote.CanPay);
        var roubles = rig.Roubles;
        rig.Pay(quote);

        Assert.Equal(1, rig.State.Transcend[Vest.ToString()].Second);
        Assert.Equal(roubles - 1000000, rig.Roubles);
        Assert.Equal(19, rig.Stock(TranscendRules.AramidFabric));
        // 6% of the template maximum; full items stay full.
        Assert.Equal((53d, 53d), (rig.Rep(Vest).MaxDurability!.Value, rig.Rep(Vest).Durability!.Value));
        Assert.Equal((31.8d, 31.8d), (rig.Rep(Plate).MaxDurability!.Value, rig.Rep(Plate).Durability!.Value));
        Assert.Contains(rig.Item(Vest), rig.Changed);
        Assert.Contains(rig.Item(Plate), rig.Changed);
        Assert.Equal(Vest.ToString(), rig.State.Grants[Plate.ToString()].ArmorId);
        Assert.True(GrantIndex.TryGet(Plate.ToString(), out var indexed) && Math.Abs(indexed - 1.8) < 1e-9);

        var snapshot = rig.Service.Snapshot(Session);
        var link = Assert.Single(snapshot.Transcend);
        Assert.Equal((Vest.ToString(), 1, 0), (link.ItemId, link.Second, link.Third));
        Assert.Equal(2, snapshot.Durability.Length);
        var receipt = rig.Service.Receipt(Session, quote.QuoteId)!;
        Assert.Equal((EnhanceAction.Transcend, TranscendTrack.Second, 1, true), (receipt.Action, receipt.Transcend, receipt.Level, receipt.Success));
    }

    [Fact]
    public void Failures_build_pity_and_only_level_four_and_five_attempts_drop()
    {
        var rig = new Rig();
        rig.Settings = new EnhanceSettings();
        rig.Backend.DrawValue = 0.99;
        rig.Pay(rig.Quote(TranscendTrack.Second));
        Assert.False(rig.State.Transcend[Vest.ToString()].Second > 0);
        Assert.Equal(5, rig.State.Transcend[Vest.ToString()].SecondPity);
        var next = rig.Quote(TranscendTrack.Second);
        Assert.Equal((65d, 5d), (next.SuccessPercent, next.PityPercent));
        Assert.False(next.DowngradeProtectionAvailable);

        rig.State.Transcend[Vest.ToString()] = new TranscendRecord { Second = 3, SecondPity = 28 };
        var risky = rig.Quote(TranscendTrack.Second);
        Assert.Equal(FailureMode.Downgrade, risky.FailureMode);
        Assert.Equal(58, risky.SuccessPercent);
        Assert.True(risky.DowngradeProtectionAvailable);
        rig.Pay(risky);
        Assert.Equal((2, 30d), (rig.State.Transcend[Vest.ToString()].Second, rig.State.Transcend[Vest.ToString()].SecondPity));

        rig.State.Transcend[Vest.ToString()].Second = 3;
        var safe = rig.Quote(TranscendTrack.Second, protect: true);
        Assert.True(safe.Protection.Downgrade);
        Assert.Equal(safe.BaseCost + safe.DowngradeProtectionFee, safe.Cost);
        // 1.3.0 decision A: a flat 100% of the base cost, not the 1.2.0 level² fee (5.6× at →4).
        Assert.Equal(safe.BaseCost, safe.DowngradeProtectionFee);
        rig.Pay(safe);
        Assert.Equal(3, rig.State.Transcend[Vest.ToString()].Second);
        Assert.True(rig.Service.Receipt(Session, safe.QuoteId)!.DowngradePrevented);

        rig.Backend.DrawValue = 0;
        rig.Pay(rig.Quote(TranscendTrack.Second));
        Assert.Equal((4, 0d), (rig.State.Transcend[Vest.ToString()].Second, rig.State.Transcend[Vest.ToString()].SecondPity));
    }

    [Fact]
    public void Downgrade_protection_fee_follows_its_own_percent_and_the_window_numbers_match_the_quote()
    {
        Assert.Equal(1500000, TranscendRules.ProtectionFee(3000000, new TranscendTuning { DowngradeProtectionPercent = 50 }));
        Assert.Equal(0, TranscendRules.ProtectionFee(3000000, new TranscendTuning { DowngradeProtectionPercent = 0 }));
        Assert.Throws<ArgumentException>(() => new TranscendTuning { DowngradeProtectionPercent = 1001 }.Validate());
        var rig = new Rig();
        rig.Settings = new EnhanceSettings { Transcend = new TranscendTuning { DowngradeProtectionPercent = 40 } };
        rig.State.Transcend[Vest.ToString()] = new TranscendRecord { Second = 3 };
        var quote = rig.Quote(TranscendTrack.Second);
        Assert.Equal((long)Math.Ceiling(quote.BaseCost * 0.4), quote.DowngradeProtectionFee);
        // The client draws effects with the same shared function the quote uses.
        var tuning = rig.Settings.Transcend;
        Assert.Equal(TranscendEffects.Display(quote.TranscendKind, 3, tuning).Main, quote.CurrentEffect);
        Assert.Equal(TranscendEffects.Display(quote.TranscendKind, 4, tuning).Main, quote.NextEffect);
        // User 2026-10-26: →5 drops on failure but cannot be protected.
        rig.State.Transcend[Vest.ToString()] = new TranscendRecord { Second = 4 };
        Assert.Equal(0, rig.Quote(TranscendTrack.Second).DowngradeProtectionFee);
        Assert.Equal((25d, 0d, 2), TranscendEffects.Display(TranscendKind.ArmorClass, 5, tuning));
        Assert.Equal((30d, 24d), (TranscendEffects.Display(TranscendKind.Reliability, 3, tuning).Main, TranscendEffects.Display(TranscendKind.Reliability, 3, tuning).Secondary));
    }

    [Fact]
    public void A_drop_lowers_the_granted_durability_again()
    {
        var rig = new Rig();
        rig.Transcend(TranscendTrack.Second, 4);
        Assert.Equal(62d, rig.Rep(Vest).MaxDurability!.Value, 6);
        rig.Rep(Vest).Durability = 61;
        rig.Settings = new EnhanceSettings();
        rig.Backend.DrawValue = 0.99;
        rig.Pay(rig.Quote(TranscendTrack.Second));
        Assert.Equal(3, rig.State.Transcend[Vest.ToString()].Second);
        Assert.Equal(59d, rig.Rep(Vest).MaxDurability!.Value, 6);
        Assert.Equal(59d, rig.Rep(Vest).Durability!.Value, 6);
    }

    [Fact]
    public void Swapping_plates_moves_the_grant_and_never_repairs_for_free()
    {
        var rig = new Rig();
        rig.Add(Other, VestTpl, Stash, "hideout", 50);
        rig.Transcend(TranscendTrack.Second, 1);
        rig.Rep(Plate).Durability = 20;
        rig.Move(Plate, Stash, "hideout");
        rig.Move(Spare, Vest, "Front_plate");

        var register = rig.Manage(EnhanceAction.RegisterInserts);
        Assert.True(register.CanRegister);
        Assert.True(register.RegistrationFee > 0);
        rig.Pay(register);
        Assert.Equal((42.4d, 42.4d), (rig.Rep(Spare).MaxDurability!.Value, rig.Rep(Spare).Durability!.Value));
        // Taken out and left loose, the old plate keeps its grant: nothing is trimmed on a quick swap.
        Assert.Equal((31.8d, 20d), (rig.Rep(Plate).MaxDurability!.Value, rig.Rep(Plate).Durability!.Value));
        Assert.True(rig.State.Grants.ContainsKey(Plate.ToString()));

        // In other armor it loses the grant; the damage stays.
        rig.Move(Plate, Other, "Front_plate");
        Assert.Equal((0, 1), rig.Sync());
        Assert.Equal((30d, 20d), (rig.Rep(Plate).MaxDurability!.Value, rig.Rep(Plate).Durability!.Value));
        Assert.False(rig.State.Grants.ContainsKey(Plate.ToString()));
        Assert.Contains(rig.Changed, i => i.Id == Plate);

        // Back in the vest (already registered): only the maximum comes back, never a free repair.
        rig.Move(Spare, Stash, "hideout");
        rig.Move(Plate, Vest, "Front_plate");
        rig.Sync();
        Assert.Equal((31.8d, 20d), (rig.Rep(Plate).MaxDurability!.Value, rig.Rep(Plate).Durability!.Value));
        Assert.Equal((42.4d, 42.4d), (rig.Rep(Spare).MaxDurability!.Value, rig.Rep(Spare).Durability!.Value));
        Assert.Equal(0, rig.Manage(EnhanceAction.RegisterInserts).DurabilityPending);
    }

    [Fact]
    public void New_plate_without_a_durability_record_still_gets_the_max_durability_grant()
    {
        // In-game 2026-10-24: new side plates from a trader carry no Upd.Repairable (the game
        // reads them as full), so the 2-star grant skipped them while they showed registered.
        var rig = new Rig();
        rig.Add(Twin, CheapPlateTpl, Stash, "hideout", 30);
        rig.Item(Twin).Upd = null;
        rig.Transcend(TranscendTrack.Second, 1);
        rig.Move(Plate, Stash, "hideout");
        rig.Move(Twin, Vest, "Front_plate");
        Assert.Equal((1, 0), rig.Sync());
        Assert.Contains(Twin.ToString(), rig.State.Inserts[Vest.ToString()].Ids);
        Assert.Equal((31.8d, 31.8d), (rig.Rep(Twin).MaxDurability!.Value, rig.Rep(Twin).Durability!.Value));
        Assert.True(rig.State.Grants.ContainsKey(Twin.ToString()));
        Assert.Contains(rig.Changed, i => i.Id == Twin);
        Assert.Equal(0, rig.Manage(EnhanceAction.RegisterInserts).DurabilityPending);
    }

    [Fact]
    public void Free_plates_register_on_insert_and_pricier_ones_wait_for_payment()
    {
        var rig = new Rig();
        rig.Add(Twin, CheapPlateTpl, Stash, "hideout", 30);
        rig.Sync();
        Assert.Contains(Plate.ToString(), rig.State.Inserts[Vest.ToString()].Ids);

        rig.Move(Plate, Stash, "hideout");
        rig.Move(Spare, Vest, "Front_plate");
        Assert.Equal((0, 0), rig.Sync());
        Assert.DoesNotContain(Spare.ToString(), rig.State.Inserts[Vest.ToString()].Ids);
        Assert.True(rig.Manage(EnhanceAction.RegisterInserts).RegistrationFee > 0);

        // A plate like the paid one costs nothing: it registers by itself, unless switched off.
        rig.Move(Spare, Stash, "hideout");
        rig.Move(Twin, Vest, "Front_plate");
        Assert.Equal((0, 0), rig.Sync(autoRegister: false));
        Assert.DoesNotContain(Twin.ToString(), rig.State.Inserts[Vest.ToString()].Ids);
        Assert.False(rig.State.AutoRegisterInserts);
        Assert.Equal((1, 0), rig.Sync());
        Assert.Contains(Twin.ToString(), rig.State.Inserts[Vest.ToString()].Ids);
        Assert.Equal([CheapPlateTpl.ToString()], rig.State.Inserts[Vest.ToString()].PaidTemplates);
    }

    [Fact]
    public void Raid_end_takes_the_grant_from_a_plate_moved_into_other_armor()
    {
        var rig = new Rig();
        rig.Add(Other, VestTpl, Stash, "hideout", 50);
        rig.Transcend(TranscendTrack.Second, 2);
        Assert.Equal(33.6d, rig.Rep(Plate).MaxDurability!.Value);
        var capture = rig.Service.BeginRaidEnd(Session);
        rig.Move(Plate, Other, "Front_plate");
        Assert.Equal((0, 0), rig.Service.CompleteRaidEnd(Session, capture!, null));
        Assert.Equal((30d, 30d), (rig.Rep(Plate).MaxDurability!.Value, rig.Rep(Plate).Durability!.Value));
        Assert.False(rig.State.Grants.ContainsKey(Plate.ToString()));
        Assert.Equal(56d, rig.Rep(Vest).MaxDurability!.Value);
    }

    [Fact]
    public void Pricier_plates_pay_the_average_attempts_of_every_held_level()
    {
        var settings = new EnhanceSettings();
        var tracks = new Dictionary<EnhancePath, int> { [EnhancePath.ArmorDurability] = 10 };
        // +10: 25,000 × Σ 1.25^k ÷ (75% - 5%k) = 2,039,594; ★★: 750,000 ÷ 30%; 2-star 2 levels with pity.
        Assert.Equal(5087507, EnhanceRules.InsertSurcharge(250000, tracks, 2, settings, 2, 0));
        settings.InsertSurchargePercent = 50;
        Assert.Equal(2543754, EnhanceRules.InsertSurcharge(250000, tracks, 2, settings, 2, 0));
        settings.InsertSurchargePercent = 100;
        settings.AlwaysSucceed = true;
        // Every attempt succeeds: one attempt per level, the 1.2.0 amount.
        Assert.Equal(Math.Ceiling(Enumerable.Range(0, 10).Sum(k => 25000 * Math.Pow(1.25, k))), EnhanceRules.InsertSurcharge(250000, tracks, 1, settings));
        Assert.Equal(0, EnhanceRules.InsertSurcharge(-5, tracks, 2, settings, 2, 0));
        Assert.Equal(1, TranscendRules.ExpectedAttempts(100, 0, 0));
        // Below 1% counts as 1%; the sum stops at 1000 attempts.
        Assert.InRange(TranscendRules.ExpectedAttempts(0, 0, 0), 99.9, 100);
        // 60%, then 65%, 70%... after each failure: 1 + 0.4 + 0.4 × 0.35 + ...
        Assert.Equal(1.5949, TranscendRules.ExpectedAttempts(60, 5, 30), 3);
    }

    [Fact]
    public void Weapons_never_get_durability_and_the_third_track_follows_fire_modes()
    {
        var rig = new Rig(stars: 3);
        var recoil = rig.Quote(TranscendTrack.Second, Gun);
        Assert.Equal(TranscendKind.HorizontalRecoil, recoil.TranscendKind);
        Assert.Equal(TranscendRules.WeaponParts, Assert.Single(recoil.Materials).Template);
        rig.Pay(recoil);
        Assert.Equal(100d, rig.Rep(Gun).MaxDurability!.Value);
        Assert.Empty(rig.State.Grants);

        var rate = rig.Quote(TranscendTrack.Third, Gun);
        Assert.Equal((TranscendKind.FireRate, true, 6d, 50d), (rate.TranscendKind, rate.Automatic, rate.NextEffect, rate.NextSecondary));
        Assert.Equal(3000000, rate.Cost);
        rig.Backend.AutomaticGun = false;
        var reliability = rig.Quote(TranscendTrack.Third, Gun);
        Assert.Equal((TranscendKind.Reliability, 10d, 8d), (reliability.TranscendKind, reliability.NextEffect, reliability.NextSecondary));
    }

    [Fact]
    public void Missing_materials_block_payment_and_can_be_turned_off()
    {
        var rig = new Rig(materials: 0);
        var quote = rig.Quote(TranscendTrack.Second);
        Assert.False(quote.CanPay);
        var roubles = rig.Roubles;
        Assert.Throws<InvalidOperationException>(() => rig.Pay(quote));
        Assert.Equal(roubles, rig.Roubles);
        Assert.False(rig.State.Transcend.ContainsKey(Vest.ToString()));

        rig.Settings = new EnhanceSettings { AlwaysSucceed = true, Transcend = new() { RequireMaterials = false } };
        var free = rig.Quote(TranscendTrack.Second);
        Assert.Empty(free.Materials);
        rig.Pay(free);
        Assert.Equal(1, rig.State.Transcend[Vest.ToString()].Second);
    }

    [Fact]
    public void Chance_materials_apply_to_transcendence()
    {
        var rig = new Rig();
        rig.Settings = new EnhanceSettings();
        var quote = rig.Quote(TranscendTrack.Second, boost: true);
        Assert.Equal((300000, 65d), (quote.BoostCost, quote.SuccessPercent));
        rig.Backend.DrawValue = 0.62;
        rig.Pay(quote);
        Assert.Equal(1, rig.State.Transcend[Vest.ToString()].Second);
        Assert.Equal(19, rig.Stock(SuccessBoost.BitcoinTemplate));
    }

    [Fact]
    public void A_graphics_card_needed_as_a_material_is_not_offered_again_as_a_chance_boost()
    {
        // The armor 2nd track to level 5 needs one graphics card; the boost sees the other 19.
        var rig = new Rig();
        rig.Settings = new EnhanceSettings();
        rig.State.Transcend[Vest.ToString()] = new TranscendRecord { Second = 4 };
        var quote = rig.Service.Quote(Session, Vest.ToString(), rig.Settings, null, null, EnhanceAction.Transcend,
            new QuoteOptions { Boost = [new BoostChoice { Kind = BoostMaterial.GraphicsCard, Count = 3 }] }, InsuranceTier.None, TranscendTrack.Second);
        Assert.Equal(19, quote.Boost[0].Owned);
    }

    [Fact]
    public void A_changed_transcend_record_invalidates_the_quote()
    {
        var rig = new Rig();
        var quote = rig.Quote(TranscendTrack.Second);
        rig.State.Transcend[Vest.ToString()] = new TranscendRecord { SecondPity = 5 };
        Assert.Throws<InvalidOperationException>(() => rig.Pay(quote));
    }

    [Fact]
    public void Insurance_value_and_plate_surcharge_count_transcendence()
    {
        var rig = new Rig();
        var before = rig.Manage(EnhanceAction.Insure, InsuranceTier.Basic).InsuredValue;
        rig.Transcend(TranscendTrack.Second, 2);
        var after = rig.Manage(EnhanceAction.Insure, InsuranceTier.Basic);
        // Two attempts at the 1,000,000 minimum plus one and two aramid fabrics (1,000 each).
        Assert.Equal(before + 2000000 + 3000, after.InsuredValue);
        Assert.Equal(2, after.TranscendSecond);

        var tracks = rig.State.Tracks[Vest.ToString()];
        var plain = EnhanceRules.InsertSurcharge(40000, tracks, 2, rig.Settings);
        var with = EnhanceRules.InsertSurcharge(40000, tracks, 2, rig.Settings, 2, 0);
        Assert.InRange(with - plain - 50000, -1, 1);
    }

    [Fact]
    public void Lost_without_enhancement_insurance_the_trader_copy_loses_the_grant()
    {
        var rig = new Rig();
        rig.Transcend(TranscendTrack.Second, 2);
        var capture = rig.Service.BeginRaidEnd(Session);
        var lost = rig.Lose();
        rig.Backend.Saved.InsuranceList!.Add(new Insurance { TraderId = new MongoId(), Items = lost });
        Assert.Equal((0, 1), rig.Service.CompleteRaidEnd(Session, capture!, null));
        var package = rig.Backend.Saved.InsuranceList.Single().Items!;
        Assert.Equal(50d, package.Single(i => i.Id == Vest).Upd!.Repairable!.MaxDurability!.Value, 6);
        Assert.Equal(30d, package.Single(i => i.Id == Plate).Upd!.Repairable!.MaxDurability!.Value, 6);
        Assert.Empty(rig.State.Grants);
        Assert.Empty(rig.State.Transcend);
        Assert.False(GrantIndex.TryGet(Plate.ToString(), out _));
    }

    [Fact]
    public void Enhancement_insurance_return_keeps_transcendence_and_grants_on_the_new_ids()
    {
        var rig = new Rig();
        rig.Transcend(TranscendTrack.Second, 1);
        rig.Pay(rig.Manage(EnhanceAction.Insure, InsuranceTier.Premium));
        var capture = rig.Service.BeginRaidEnd(Session);
        rig.Lose();
        rig.Backend.DrawValue = 0;
        Assert.Equal((1, 0), rig.Service.CompleteRaidEnd(Session, capture!, null));
        var mailed = Assert.Single(rig.Backend.Mailed);
        var vest = mailed.Single(i => i.Template == VestTpl);
        var plate = mailed.Single(i => i.Template == CheapPlateTpl);
        Assert.Equal(53d, vest.Upd!.Repairable!.MaxDurability!.Value, 6);
        Assert.Equal(1, rig.State.Transcend[vest.Id.ToString()].Second);
        Assert.Equal(vest.Id.ToString(), rig.State.Grants[plate.Id.ToString()].ArmorId);
        Assert.False(rig.State.Grants.ContainsKey(Plate.ToString()));
        Assert.True(GrantIndex.TryGet(plate.Id.ToString(), out _));
    }

    [Fact]
    public void Receipts_rebuild_transcendence_with_the_tracks()
    {
        var state = new EnhanceState();
        state.Receipts.Add(new EnhanceReceipt { ItemId = "x", Path = EnhancePath.Recoil, Level = 10, Evolved = true, StarLevel = 2, Success = true });
        state.Receipts.Add(new EnhanceReceipt { ItemId = "x", Action = EnhanceAction.Transcend, Transcend = TranscendTrack.Second, Level = 2, Pity = 0 });
        state.Receipts.Add(new EnhanceReceipt { ItemId = "x", Action = EnhanceAction.Transcend, Transcend = TranscendTrack.Second, Level = 2, Pity = 5 });
        Assert.True(EnhanceService.RecoverFromReceipts(state, "x"));
        Assert.Equal(10, state.Tracks["x"][EnhancePath.Recoil]);
        Assert.Equal((2, 5d), (state.Transcend["x"].Second, state.Transcend["x"].SecondPity));
    }

    [Fact]
    public void Flea_and_fence_rate_granted_items_without_the_grant()
    {
        Assert.Equal(1, DurabilityPricePatch.Quality(65, 65, 15, 50, 1.14), 6);
        Assert.Equal(Math.Sqrt(0.5), DurabilityPricePatch.Quality(32.5, 65, 15, 50, 0.8), 6);
        Assert.Equal(0.7, DurabilityPricePatch.Quality(10, 65, 0, 50, 0.7), 6);
        var target = typeof(ItemHelper).GetMethod("GetRepairableItemQualityValue",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(target);
        Assert.Equal(["itemDetails", "repairable", "item"], target!.GetParameters().Select(p => p.Name!));
        Assert.Equal(typeof(double), target.ReturnType);
    }

    [Fact]
    public void Sold_or_discarded_granted_items_drop_their_record()
    {
        var rig = new Rig();
        rig.Transcend(TranscendTrack.Second, 1);
        rig.Pmc.Inventory!.Items!.RemoveAll(i => i.Id == Plate);
        rig.Quote(TranscendTrack.Second);
        Assert.False(rig.State.Grants.ContainsKey(Plate.ToString()));
        Assert.True(rig.State.Grants.ContainsKey(Vest.ToString()));
    }

    [Fact]
    public void Server_start_indexes_saved_grants_for_pricing()
    {
        var rig = new Rig();
        rig.Transcend(TranscendTrack.Second, 1);
        var fresh = new EnhanceService(rig.Backend, Json);
        Assert.Equal(2, fresh.IndexGrants());
        Assert.True(GrantIndex.TryGet(Vest.ToString(), out var amount) && Math.Abs(amount - 3) < 1e-9);
    }
}
