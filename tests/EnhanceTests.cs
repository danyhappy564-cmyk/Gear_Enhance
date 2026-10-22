using System.Text.Json;
using GearEnhance;
using Microsoft.Extensions.DependencyInjection;
using SPTarkov.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Xunit;

namespace GearEnhanceTests;

public sealed class EnhanceTests
{
    private static readonly MongoId Session = new("111111111111111111111111");
    private static readonly MongoId Stash = new("222222222222222222222222");
    private static readonly MongoId Target = new("333333333333333333333333");
    private static readonly MongoId Part = new("444444444444444444444444");
    private static readonly MongoId RubleTpl = new("5449016a4bdc2d6f028b456f");
    private static readonly JsonUtil Json = new([new SptJsonConverterRegistrator()]);

    private sealed class MemoryBackend : SptEnhanceBackend
    {
        public SptProfile Saved { get; set; }
        public bool IsEligible { get; set; } = true;
        public bool WeaponCategory { get; set; } = true;
        // These are SPT's real removal methods, not a copied payment/deletion algorithm.
        private readonly InventoryHelper native = new(null!, null!, null!, null!, null!, null!, null!, null!);
        public MemoryBackend(SptProfile saved) : base(null!, null!, null!, null!, null!) => Saved = saved;
        public override SptProfile Profile(MongoId session) => session == Session ? Saved : null!;
        public override bool Eligible(MongoId template) => IsEligible;
        public override bool IsWeapon(MongoId template) => WeaponCategory;
        public override (double, string) Price(MongoId template, PriceBasis basis) => (100000, "북밸류");
        public override void RemoveMoney(PmcData profile, MongoId id, int count, MongoId session,
            ItemEventRouterResponse output) => native.RemoveItemByCount(profile, id, count, session, output);
        public override void Destroy(PmcData profile, MongoId id, MongoId session,
            ItemEventRouterResponse output) => native.RemoveItem(profile, id, session, output);
    }

    private sealed class Rig
    {
        public readonly PmcData Pmc = new()
        {
            Id = Session, Inventory = new() { Stash = Stash, Items = [] }, InsuredItems = []
        };
        public readonly MemoryBackend Backend;
        public readonly EnhanceService Service;
        public readonly ItemEventRouterResponse Output = new()
        {
            ProfileChanges = new() { [Session] = new() { Items = new()
                { NewItems = [], ChangedItems = [], DeletedItems = [] } } }
        };
        public Rig(long money = 100000)
        {
            Pmc.Inventory!.Items!.Add(new Item
            {
                Id = Target, Template = new("555555555555555555555555"), ParentId = Stash,
                SlotId = "hideout", Upd = new() { Repairable = new() { Durability = 80, MaxDurability = 100 } }
            });
            Pmc.Inventory.Items.Add(new Item
            {
                Id = new("666666666666666666666666"), Template = RubleTpl,
                ParentId = Stash, SlotId = "hideout", Upd = new() { StackObjectsCount = money }
            });
            Backend = new(new SptProfile { CharacterData = new() { PmcData = Pmc } });
            Service = new(Backend, Json);
        }
        public EnhanceQuote Quote(double chance = 100, FailureMode failure = FailureMode.KeepItem) =>
            Service.Quote(Session, Target.ToString(), new() { SuccessPercent = chance, FailureMode = failure });
        public void Execute(EnhanceQuote quote, bool confirm = false) =>
            Service.Execute(Session, Pmc, quote.QuoteId, confirm, Output);
        public long Money => (long)Pmc.Inventory!.Items!.Where(i => i.Template == RubleTpl)
            .Sum(i => i.Upd?.StackObjectsCount ?? 1);
        public Item Item => Pmc.Inventory!.Items!.Single(i => i.Id == Target);
        public void AddPart(string slot = "mod_scope") => Pmc.Inventory!.Items!.Add(new()
        {
            Id = Part, Template = new("777777777777777777777777"), ParentId = Target,
            SlotId = slot, Upd = new() { Repairable = new() { Durability = 50, MaxDurability = 75 } }
        });
    }

    [Fact]
    public async Task Actual_spt_di_keeps_quote_available_to_a_separately_resolved_event_router()
    {
        var rig = new Rig();
        var services = new ServiceCollection();
        services.AddSingleton<SptEnhanceBackend>(rig.Backend);
        services.AddSingleton(Json);
        services.AddSingleton(new HttpResponseUtil(Json, null!));
        var injector = new DependencyInjectionHandler(services);
        injector.AddInjectableTypesFromTypeList([typeof(EnhanceService), typeof(EnhanceQueries), typeof(EnhanceEvents)]);
        injector.InjectAll();
        using var provider = services.BuildServiceProvider();
        var queries = provider.GetRequiredService<EnhanceQueries>();
        var request = Json.Serialize(new QuoteRequest
            { ProtocolVersion = EnhanceProtocol.Version, ItemId = Target.ToString(), Settings = new() { AlwaysSucceed = true } })!;
        var response = await queries.HandleStaticAsync("/rf/gear-enhance/quote", request, Session, "");
        var quote = Json.Deserialize<ApiReply<EnhanceQuote>>((string)response)!.Data!;
        // Resolve the event router independently, exactly as SPT's two router graphs do.
        var events = provider.GetRequiredService<EnhanceEvents>();
        var operation = Json.Deserialize<EnhanceOperation>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new { Action = "GearEnhance", quoteId = quote.QuoteId }))!;
        await events.HandleItemEvent("GearEnhance", rig.Pmc, operation, Session, rig.Output);
        Assert.Equal(90000, rig.Money);
        var receiptResponse = await provider.GetRequiredService<EnhanceQueries>().HandleStaticAsync(
            "/rf/gear-enhance/receipt", Json.Serialize(new ReceiptRequest { ProtocolVersion = EnhanceProtocol.Version, QuoteId = quote.QuoteId })!, Session, "");
        var receipt = Json.Deserialize<ApiReply<EnhanceReceipt>>((string)receiptResponse)!;
        Assert.True(receipt.Ok, receipt.Error);
        Assert.Equal(1, receipt.Data!.Level);
        Assert.Same(provider.GetRequiredService<EnhanceService>(), provider.GetRequiredService<EnhanceService>());
    }

    [Fact]
    public async Task Successful_native_item_event_charges_once_and_records_level()
    {
        var rig = new Rig();
        var quote = rig.Quote();
        var router = new EnhanceEvents(rig.Service, new HttpResponseUtil(Json, null!));
        var data = Json.Deserialize<EnhanceOperation>("{\"Action\":\"GearEnhance\",\"quoteId\":\"" + quote.QuoteId + "\"}")!;
        Assert.Equal(typeof(EnhanceOperation), router.GetBodyType("GearEnhance"));
        await router.HandleItemEvent("GearEnhance", rig.Pmc, data, Session, rig.Output);
        Assert.Equal(90000, rig.Money);
        Assert.Equal(1, rig.Service.Receipt(Session, quote.QuoteId)!.Level);
        Assert.Null(rig.Item.Upd!.Buff); // Enhancement never overwrites native repair buffs.
        Assert.Contains(rig.Output.ProfileChanges![Session].Items!.ChangedItems!, i => i.Template == RubleTpl);
        Assert.Throws<InvalidOperationException>(() => rig.Execute(quote));
        Assert.Equal(90000, rig.Money);
    }

    [Fact]
    public void Partial_and_complete_currency_stacks_are_reported_to_client()
    {
        var rig = new Rig(3000);
        rig.Pmc.Inventory!.Items!.Add(new()
        {
            Id = new("888888888888888888888888"), Template = RubleTpl, ParentId = Stash,
            Upd = new() { StackObjectsCount = 9000 }
        });
        rig.Execute(rig.Quote());
        Assert.Equal(2000, rig.Money);
        Assert.Single(rig.Output.ProfileChanges![Session].Items!.DeletedItems!);
        Assert.Single(rig.Output.ProfileChanges[Session].Items!.ChangedItems!);
    }

    [Theory]
    [InlineData(FailureMode.KeepItem, 80)]
    [InlineData(FailureMode.ZeroDurability, 0)]
    public void Failure_keeps_level_and_max_durability(FailureMode mode, double durability)
    {
        var rig = new Rig();
        rig.AddPart();
        var quote = rig.Quote(0, mode);
        rig.Execute(quote);
        Assert.Equal(90000, rig.Money);
        Assert.Equal(durability, rig.Item.Upd!.Repairable!.Durability);
        Assert.Equal(100, rig.Item.Upd.Repairable.MaxDurability);
        Assert.Equal(0, rig.Service.Receipt(Session, quote.QuoteId)!.Level);
        if (mode == FailureMode.ZeroDurability)
        {
            Assert.Equal(0, rig.Pmc.Inventory!.Items!.Single(i => i.Id == Part).Upd!.Repairable!.Durability);
            Assert.Equal(2, rig.Output.ProfileChanges![Session].Items!.ChangedItems!.Count(i => i.Template != RubleTpl));
        }
    }

    [Fact]
    public void Destruction_requires_confirmation_and_removes_attached_tree_and_insurance()
    {
        var rig = new Rig();
        rig.AddPart();
        rig.Pmc.InsuredItems!.Add(new() { ItemId = Part });
        var quote = rig.Quote(0, FailureMode.Destroy);
        Assert.Throws<InvalidOperationException>(() => rig.Execute(quote));
        Assert.Equal(100000, rig.Money);
        rig.Execute(quote, true);
        Assert.DoesNotContain(rig.Pmc.Inventory!.Items!, i => i.Id == Target || i.Id == Part);
        Assert.Empty(rig.Pmc.InsuredItems!);
        Assert.Contains(rig.Output.ProfileChanges![Session].Items!.DeletedItems!, i => i.Id == Target);
    }

    [Fact]
    public void Insufficient_funds_makes_no_changes()
    {
        var rig = new Rig(9999);
        Assert.Throws<InvalidOperationException>(() => rig.Execute(rig.Quote()));
        Assert.Equal(9999, rig.Money);
        Assert.Equal(80, rig.Item.Upd!.Repairable!.Durability);
        Assert.Empty(rig.Output.ProfileChanges![Session].Items!.ChangedItems!);
    }

    [Fact]
    public void Money_outside_stash_cannot_pay()
    {
        var rig = new Rig();
        rig.Pmc.Inventory!.Items!.Single(i => i.Template == RubleTpl).ParentId = new("999999999999999999999999");
        var quote = rig.Quote();
        Assert.Equal(0, quote.AvailableRoubles);
        Assert.Throws<InvalidOperationException>(() => rig.Execute(quote));
    }

    [Fact]
    public void Changed_item_or_quote_cannot_be_executed()
    {
        var rig = new Rig();
        var first = rig.Quote();
        var second = rig.Quote();
        Assert.Throws<InvalidOperationException>(() => rig.Execute(first));
        rig.Item.Upd!.Repairable!.Durability = 79;
        Assert.Throws<InvalidOperationException>(() => rig.Execute(second));
        Assert.Equal(100000, rig.Money);
    }

    [Fact]
    public void Equipped_gear_unrelated_items_and_filled_grids_are_rejected()
    {
        var rig = new Rig();
        rig.Item.ParentId = new("999999999999999999999999");
        Assert.Throws<InvalidOperationException>(() => rig.Quote());
        rig.Item.ParentId = Stash;
        rig.Backend.IsEligible = false;
        Assert.Throws<InvalidOperationException>(() => rig.Quote());
        rig.Backend.IsEligible = true;
        rig.AddPart("main");
        Assert.Throws<InvalidOperationException>(() => rig.Quote());
    }

    [Fact]
    public void Profile_serialization_keeps_levels_and_replay_guard_after_restart()
    {
        var rig = new Rig();
        var quote = rig.Quote();
        rig.Execute(quote);
        rig.Backend.Saved = Json.Deserialize<SptProfile>(Json.Serialize(rig.Backend.Saved))!;
        var restarted = new EnhanceService(rig.Backend, Json);
        Assert.Equal(1, restarted.Receipt(Session, quote.QuoteId)!.Level);
        Assert.Throws<InvalidOperationException>(() => restarted.Execute(Session,
            rig.Backend.Saved.CharacterData!.PmcData!, quote.QuoteId, false, rig.Output));
        var next = restarted.Quote(Session, Target.ToString(), new());
        Assert.Equal(1, next.CurrentLevel);
        Assert.Equal(12500, next.Cost);
    }

    [Fact]
    public async Task Concurrent_replay_charges_only_once()
    {
        var rig = new Rig();
        var quote = rig.Quote();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            try { rig.Execute(quote); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.Single(results, ok => ok);
        Assert.Equal(90000, rig.Money);
    }

    [Fact]
    public async Task Quote_route_deserializes_actual_client_body()
    {
        var rig = new Rig();
        var router = new EnhanceQueries(Json, rig.Service);
        var request = "{\"ProtocolVersion\":2,\"ItemId\":\"" + Target + "\",\"Settings\":{\"AlwaysSucceed\":true,\"SuccessPercent\":0}}";
        var result = await router.HandleStaticAsync("/rf/gear-enhance/quote", request, Session, "");
        var reply = Json.Deserialize<ApiReply<EnhanceQuote>>((string)result)!;
        Assert.True(reply.Ok, reply.Error);
        Assert.Equal(100, reply.Data!.SuccessPercent);
        Assert.Equal(10000, reply.Data.Cost);
    }

    [Fact]
    public async Task Client_JsonNet_and_server_enum_converters_agree()
    {
        var rig = new Rig();
        var router = new EnhanceQueries(Json, rig.Service);
        var request = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            ProtocolVersion = EnhanceProtocol.Version, ItemId = Target.ToString(), Settings = new EnhanceSettings
            { FailureMode = FailureMode.Destroy, PriceBasis = PriceBasis.Flea, AlwaysSucceed = true }
        });
        var result = await router.HandleStaticAsync("/rf/gear-enhance/quote", request, Session, "");
        var client = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiReply<EnhanceQuote>>((string)result)!;
        Assert.True(client.Ok, client.Error);
        Assert.Equal(FailureMode.Destroy, client.Data!.FailureMode);
        Assert.Equal(100, client.Data.SuccessPercent);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(100, 0.999999, true)]
    [InlineData(75, 0.749999, true)]
    [InlineData(75, 0.75, false)]
    public void Probability_boundaries_are_exact(double chance, double draw, bool expected) =>
        Assert.Equal(expected, EnhanceRules.Succeeds(chance, draw));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Invalid_settings_are_rejected(double chance) =>
        Assert.Throws<ArgumentException>(() => new EnhanceSettings { SuccessPercent = chance }.Validate());

    [Fact]
    public void Maximum_level_and_cost_overflow_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => EnhanceRules.Cost(100000, 10, new()));
        Assert.Throws<InvalidOperationException>(() => EnhanceRules.Cost(1e20, 0, new()));
        Assert.Throws<InvalidOperationException>(() => EnhanceRules.Cost(0, 0, new()));
        Assert.Equal(1000, EnhanceRules.Cost(1, 0, new()));
        Assert.Equal(0, EnhanceRules.Cost(1, 0, new() { MinimumCost = 0, CostPercent = 0 }));
    }

    [Fact]
    public void Parent_cycles_terminate_without_qualifying_as_stash()
    {
        var cycle = new Dictionary<string, string> { ["a"] = "b", ["b"] = "a", ["c"] = "c" };
        Assert.False(EnhanceRules.IsBelow("a", "stash", cycle));
        Assert.False(EnhanceRules.IsBelow("c", "stash", cycle));
    }

    [Fact]
    public void Three_weapon_tracks_upgrade_independently_and_keep_all_paid_levels()
    {
        var rig = new Rig();
        rig.Execute(rig.Quote());
        var quote = rig.Service.Quote(Session, Target.ToString(), new() { AlwaysSucceed = true }, EnhancePath.Ergonomics);
        Assert.Equal(0, quote.CurrentLevel);
        Assert.Equal(1, quote.NextLevel);
        Assert.Equal(10000, quote.Cost);
        Assert.Equal(0, quote.CurrentEffect);
        Assert.Equal(1, quote.NextEffect);
        rig.Execute(quote);
        rig.Execute(rig.Service.Quote(Session, Target.ToString(), new() { AlwaysSucceed = true }, EnhancePath.Precision));
        rig.Execute(rig.Quote());
        var tracks = rig.Service.Snapshot(Session).Items.ToDictionary(i => i.Path, i => i.Level);
        Assert.Equal(2, tracks[EnhancePath.Recoil]);
        Assert.Equal(1, tracks[EnhancePath.Ergonomics]);
        Assert.Equal(1, tracks[EnhancePath.Precision]);
        Assert.Equal(57500, rig.Money);
    }

    [Fact]
    public void Existing_prototype_levels_get_a_free_choice_even_at_maximum_level()
    {
        var rig = new Rig();
        rig.Backend.Saved.ExtensionData = new() { ["rfGearEnhance"] = new EnhanceState
            { Levels = new() { [Target.ToString()] = 10 } } };
        var quote = rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Precision);
        Assert.True(quote.BindLegacy);
        Assert.Equal(0, quote.Cost);
        Assert.Equal(10, quote.NextLevel);
        rig.Execute(quote);
        Assert.Equal(100000, rig.Money);
        Assert.Equal(EnhancePath.Precision, rig.Service.Snapshot(Session).Items.Single().Path);
        Assert.Throws<InvalidOperationException>(() => rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Precision));
        var other = rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Recoil);
        Assert.Equal(0, other.CurrentLevel);
        Assert.Equal(10000, other.Cost);
    }

    [Fact]
    public void Weapon_and_armor_paths_cannot_be_mixed()
    {
        var rig = new Rig();
        Assert.Throws<ArgumentException>(() => rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Mobility));
        rig.Backend.WeaponCategory = false;
        Assert.Throws<ArgumentException>(() => rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Recoil));
        var quote = rig.Service.Quote(Session, Target.ToString(),
            new() { AlwaysSucceed = true }, EnhancePath.ArmorDurability);
        Assert.Equal(3, quote.NextEffect);
        rig.Execute(quote);
        Assert.Equal(EnhancePath.ArmorDurability, rig.Service.Snapshot(Session).Items.Single().Path);
    }

    [Fact]
    public void Snapshot_and_specialization_survive_the_real_spt_profile_serializer()
    {
        var rig = new Rig();
        rig.Execute(rig.Quote());
        rig.Backend.Saved = Json.Deserialize<SptProfile>(Json.Serialize(rig.Backend.Saved))!;
        var restored = new EnhanceService(rig.Backend, Json).Snapshot(Session);
        Assert.Equal(Session.ToString(), restored.ProfileId);
        Assert.Equal(EnhancePath.Recoil, restored.Items.Single().Path);
        Assert.Equal(1, restored.Items.Single().Level);
    }

    [Fact]
    public void Failed_upgrade_preserves_specialization_and_recorded_effect_level()
    {
        var rig = new Rig();
        rig.Execute(rig.Quote());
        rig.Execute(rig.Quote(0));
        var entry = rig.Service.Snapshot(Session).Items.Single();
        Assert.Equal(EnhancePath.Recoil, entry.Path);
        Assert.Equal(1, entry.Level);
    }

    [Fact]
    public async Task State_endpoint_returns_only_owned_enhanced_items_using_client_json()
    {
        var rig = new Rig();
        rig.Execute(rig.Quote());
        var router = new EnhanceQueries(Json, rig.Service);
        var result = await router.HandleStaticAsync("/rf/gear-enhance/state", "{\"ProtocolVersion\":2}", Session, "");
        var client = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiReply<EnhanceSnapshot>>((string)result)!;
        Assert.True(client.Ok, client.Error);
        Assert.Equal(EnhancePath.Recoil, client.Data!.Items.Single().Path);
        rig.Pmc.Inventory!.Items!.Remove(rig.Item);
        Assert.Empty(rig.Service.Snapshot(Session).Items);
    }

    [Fact]
    public void Legacy_specialization_migrates_once_without_cost_or_other_track_levels()
    {
        var rig = new Rig();
        rig.Backend.Saved.ExtensionData = new() { ["rfGearEnhance"] = new EnhanceState
            { Levels = new() { [Target.ToString()] = 10 }, Paths = new() { [Target.ToString()] = EnhancePath.Precision } } };
        rig.Backend.Saved = Json.Deserialize<SptProfile>(Json.Serialize(rig.Backend.Saved))!;
        var state = rig.Service.Snapshot(Session);
        Assert.Equal(10, state.Items.Single().Level);
        Assert.Equal(EnhancePath.Precision, state.Items.Single().Path);
        Assert.Equal(100000, rig.Money);
        var other = rig.Service.Quote(Session, Target.ToString(), new() { AlwaysSucceed = true }, EnhancePath.Recoil);
        Assert.Equal(0, other.CurrentLevel);
        Assert.Equal(10000, other.Cost);
        // The loaded profile was deserialized; native execution must use that PMC.
        rig.Service.Execute(Session, rig.Backend.Saved.CharacterData!.PmcData!, other.QuoteId, false, rig.Output);
        rig.Backend.Saved = Json.Deserialize<SptProfile>(Json.Serialize(rig.Backend.Saved))!;
        var restored = rig.Service.Snapshot(Session).Items.ToDictionary(i => i.Path, i => i.Level);
        Assert.Equal(10, restored[EnhancePath.Precision]);
        Assert.Equal(1, restored[EnhancePath.Recoil]);
        Assert.Equal(2, restored.Count);
    }

    [Fact]
    public void Independent_tracks_still_use_failure_chance_and_destruction_clears_all_tracks()
    {
        var rig = new Rig();
        rig.Execute(rig.Quote());
        rig.Execute(rig.Service.Quote(Session, Target.ToString(), new() { AlwaysSucceed = true }, EnhancePath.Ergonomics));
        var failed = rig.Service.Quote(Session, Target.ToString(), new() { SuccessPercent = 0 }, EnhancePath.Precision);
        Assert.Equal(0, failed.SuccessPercent);
        rig.Execute(failed);
        Assert.Equal(2, rig.Service.Snapshot(Session).Items.Length);
        var destroyed = rig.Service.Quote(Session, Target.ToString(),
            new() { SuccessPercent = 0, FailureMode = FailureMode.Destroy }, EnhancePath.Precision);
        rig.Execute(destroyed, true);
        Assert.Empty(rig.Service.Snapshot(Session).Items);
        Assert.DoesNotContain(rig.Pmc.Inventory!.Items!, i => i.Id == Target);
    }

    [Fact]
    public void Both_armor_tracks_keep_separate_progress_after_profile_roundtrip()
    {
        var rig = new Rig(); rig.Backend.WeaponCategory = false;
        foreach (var path in new[] { EnhancePath.ArmorDurability, EnhancePath.Mobility, EnhancePath.ArmorDurability })
            rig.Execute(rig.Service.Quote(Session, Target.ToString(), new() { AlwaysSucceed = true }, path));
        rig.Backend.Saved = Json.Deserialize<SptProfile>(Json.Serialize(rig.Backend.Saved))!;
        var tracks = rig.Service.Snapshot(Session).Items.ToDictionary(i => i.Path, i => i.Level);
        Assert.Equal(2, tracks[EnhancePath.ArmorDurability]);
        Assert.Equal(1, tracks[EnhancePath.Mobility]);
    }

    [Fact]
    public async Task Previous_client_protocol_is_rejected_before_issuing_a_paid_quote()
    {
        var rig = new Rig();
        var router = new EnhanceQueries(Json, rig.Service);
        var response = await router.HandleStaticAsync("/rf/gear-enhance/quote",
            Json.Serialize(new QuoteRequest { ItemId = Target.ToString() })!, Session, "");
        var reply = Json.Deserialize<ApiReply<EnhanceQuote>>((string)response)!;
        Assert.False(reply.Ok);
        Assert.Contains("업데이트", reply.Error);
        Assert.Equal(100000, rig.Money);
    }
}
