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
            Service.Quote(Session, Target.ToString(), new() { SuccessPercent = chance, AlwaysSucceed = chance == 100, FailureMode = failure });
        public void Execute(EnhanceQuote quote, bool confirm = false) =>
            Service.Execute(Session, Pmc, quote.QuoteId, confirm, Output);
        public long Money => (long)Pmc.Inventory!.Items!.Where(i => i.Template == RubleTpl)
            .Sum(i => i.Upd?.StackObjectsCount ?? 1);
        public Item Item => Pmc.Inventory!.Items!.Single(i => i.Id == Target);
        public void Seed(params (EnhancePath Path, int Level)[] tracks) => Backend.Saved.ExtensionData = new()
        {
            ["rfGearEnhance"] = new EnhanceState { Tracks = new()
                { [Target.ToString()] = tracks.ToDictionary(t => t.Path, t => t.Level) } }
        };
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
        var request = "{\"ProtocolVersion\":5,\"ItemId\":\"" + Target + "\",\"Settings\":{\"AlwaysSucceed\":true,\"SuccessPercent\":0}}";
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
        Assert.Equal(10.0 / 19, quote.NextEffect, 10);
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
        Assert.Equal(30.0 / 19, quote.NextEffect, 10);
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
        var result = await router.HandleStaticAsync("/rf/gear-enhance/state", "{\"ProtocolVersion\":5}", Session, "");
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

    [Theory]
    [InlineData(0, 75)]
    [InlineData(1, 70)]
    [InlineData(4, 55)]
    [InlineData(9, 30)]
    public void Quotes_reduce_success_chance_by_the_selected_track_level(int level, double expected)
    {
        var rig = new Rig();
        rig.Seed((EnhancePath.Recoil, level));
        var quote = rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Recoil);
        Assert.Equal(expected, quote.SuccessPercent);
        var other = rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Precision);
        Assert.Equal(75, other.SuccessPercent);
        var certain = rig.Service.Quote(Session, Target.ToString(), new() { AlwaysSucceed = true }, EnhancePath.Recoil);
        Assert.Equal(100, certain.SuccessPercent);
        var constant = rig.Service.Quote(Session, Target.ToString(), new() { SuccessLossPerLevel = 0 }, EnhancePath.Recoil);
        Assert.Equal(75, constant.SuccessPercent);
        var zero = rig.Service.Quote(Session, Target.ToString(), new() { SuccessPercent = 0, SuccessLossPerLevel = 100 }, EnhancePath.Recoil);
        Assert.Equal(0, zero.SuccessPercent);
    }

    [Fact]
    public void Per_level_loss_reaching_zero_is_refused_but_an_explicit_zero_base_is_kept()
    {
        var rig = new Rig();
        rig.Seed((EnhancePath.Recoil, 15));
        var money = rig.Money;
        Assert.Throws<InvalidOperationException>(() => rig.Service.Quote(Session, Target.ToString(),
            new() { MaxLevel = 20 }, EnhancePath.Recoil));
        Assert.Equal(money, rig.Money);
        Assert.Equal(5, rig.Service.Quote(Session, Target.ToString(), new() { MaxLevel = 20, SuccessPercent = 80 }, EnhancePath.Recoil).SuccessPercent);
        Assert.Equal(100, rig.Service.Quote(Session, Target.ToString(), new() { MaxLevel = 20, AlwaysSucceed = true }, EnhancePath.Recoil).SuccessPercent);
        Assert.Equal(0, rig.Service.Quote(Session, Target.ToString(), new() { MaxLevel = 20, SuccessPercent = 0 }, EnhancePath.Recoil).SuccessPercent);
    }

    [Fact]
    public void Upgrades_past_the_maximum_are_refused_while_evolution_stays_available()
    {
        var rig = new Rig();
        rig.Seed((EnhancePath.Recoil, 10), (EnhancePath.Ergonomics, 10), (EnhancePath.Precision, 10));
        Assert.Throws<InvalidOperationException>(() => rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Recoil));
        Assert.Equal(11, rig.Service.Quote(Session, Target.ToString(), new() { MaxLevel = 11 }, EnhancePath.Recoil).NextLevel);
        Assert.Equal(50, rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Recoil, action: EnhanceAction.Evolve).SuccessPercent);
    }

    [Theory]
    [InlineData(10, 5, 5, 4, false)]
    [InlineData(10, 5, 5, 5, true)]
    [InlineData(9, 5, 5, 4, false)]
    [InlineData(9, 5, 5, 5, true)]
    [InlineData(11, 6, 6, 5, false)]
    [InlineData(11, 6, 6, 6, true)]
    public void High_risk_requires_every_weapon_track_at_half_rounded_up(int maximum, int recoil, int ergonomics, int precision, bool expected)
    {
        var rig = new Rig();
        rig.Seed((EnhancePath.Recoil, recoil), (EnhancePath.Ergonomics, ergonomics), (EnhancePath.Precision, precision));
        var quote = rig.Service.Quote(Session, Target.ToString(), new() { MaxLevel = maximum }, EnhancePath.Recoil);
        Assert.Equal(expected, quote.HighRisk);
        Assert.Equal(expected ? FailureMode.Downgrade : FailureMode.KeepItem, quote.FailureMode);
        Assert.Equal(expected ? 25 : 0, quote.DestructionPercent);
        Assert.Equal(expected, quote.CanDestroy);
    }

    [Fact]
    public void High_risk_downgrade_charges_once_changes_only_selected_track_and_exits_the_risk_zone()
    {
        var rig = new Rig(); rig.AddPart();
        rig.Seed((EnhancePath.Recoil, 5), (EnhancePath.Ergonomics, 5), (EnhancePath.Precision, 5));
        var quote = rig.Service.Quote(Session, Target.ToString(),
            new() { SuccessPercent = 0, HighRiskDestructionPercent = 0 }, EnhancePath.Recoil);
        rig.Execute(quote);
        Assert.Equal(100000 - quote.Cost, rig.Money);
        Assert.Equal(80, rig.Item.Upd!.Repairable!.Durability);
        Assert.Contains(rig.Pmc.Inventory!.Items!, i => i.Id == Part);
        var receipt = rig.Service.Receipt(Session, quote.QuoteId)!;
        Assert.False(receipt.Success);
        Assert.Equal(FailureMode.Downgrade, receipt.FailureMode);
        Assert.Equal(4, receipt.Level);
        Assert.Throws<InvalidOperationException>(() => rig.Execute(quote));
        rig.Backend.Saved = Json.Deserialize<SptProfile>(Json.Serialize(rig.Backend.Saved))!;
        var levels = rig.Service.Snapshot(Session).Items.ToDictionary(i => i.Path, i => i.Level);
        Assert.Equal(4, levels[EnhancePath.Recoil]);
        Assert.Equal(5, levels[EnhancePath.Ergonomics]);
        Assert.Equal(5, levels[EnhancePath.Precision]);
        Assert.False(rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Recoil).HighRisk);
    }

    [Fact]
    public void High_risk_destruction_requires_confirmation_and_removes_the_native_tree_and_every_track()
    {
        var rig = new Rig(); rig.AddPart();
        rig.Pmc.InsuredItems!.Add(new() { ItemId = Part });
        rig.Seed((EnhancePath.Recoil, 5), (EnhancePath.Ergonomics, 5), (EnhancePath.Precision, 5));
        var quote = rig.Service.Quote(Session, Target.ToString(),
            new() { SuccessPercent = 0, HighRiskDestructionPercent = 100 }, EnhancePath.Recoil);
        Assert.True(quote.CanDestroy);
        Assert.Throws<InvalidOperationException>(() => rig.Execute(quote));
        Assert.Equal(100000, rig.Money);
        rig.Execute(quote, true);
        Assert.Equal(100000 - quote.Cost, rig.Money);
        Assert.Equal(FailureMode.Destroy, rig.Service.Receipt(Session, quote.QuoteId)!.FailureMode);
        Assert.Empty(rig.Service.Snapshot(Session).Items);
        Assert.DoesNotContain(rig.Pmc.Inventory!.Items!, i => i.Id == Target || i.Id == Part);
        Assert.Empty(rig.Pmc.InsuredItems!);
        // SPT reports the deleted root; its children are removed with that tree.
        Assert.Contains(rig.Output.ProfileChanges![Session].Items!.DeletedItems!, i => i.Id == Target);
    }

    [Fact]
    public void Guaranteed_success_overrides_high_risk_and_completion_quote_matches_restored_client_effects()
    {
        var rig = new Rig();
        rig.Seed((EnhancePath.Recoil, 10), (EnhancePath.Ergonomics, 10), (EnhancePath.Precision, 9));
        var quote = rig.Service.Quote(Session, Target.ToString(), new() { AlwaysSucceed = true }, EnhancePath.Precision);
        Assert.True(quote.HighRisk);
        Assert.False(quote.CanDestroy);
        Assert.False(quote.Complete);
        Assert.True(quote.CompletesOnSuccess);
        Assert.Equal(4, quote.NextGainMultiplier);
        Assert.Equal(10, quote.NextEffect);
        rig.Execute(quote);
        rig.Backend.Saved = Json.Deserialize<SptProfile>(Json.Serialize(rig.Backend.Saved))!;
        var registry = new EffectRegistry();
        registry.Replace(rig.Service.Snapshot(Session), Session.ToString());
        Assert.True(registry.IsComplete(Target.ToString()));
        Assert.Equal(quote.NextEffect, registry.Amount(Target.ToString(), EnhancePath.Precision));
        Assert.False(registry.IsEvolved(Target.ToString()));
        Assert.Equal(15, registry.Amount(Target.ToString(), EnhancePath.Recoil));
        Assert.Equal(10, registry.Amount(Target.ToString(), EnhancePath.Ergonomics));
    }

    [Fact]
    public void Armor_completion_has_a_bonus_and_uses_the_normal_failure_setting()
    {
        var rig = new Rig(); rig.Backend.WeaponCategory = false;
        rig.Seed((EnhancePath.ArmorDurability, 10), (EnhancePath.Mobility, 9));
        var quote = rig.Service.Quote(Session, Target.ToString(), new() { AlwaysSucceed = true }, EnhancePath.Mobility);
        Assert.False(quote.HighRisk);
        Assert.False(quote.CanDestroy);
        Assert.True(quote.CompletesOnSuccess);
        Assert.Equal(20, quote.NextEffect);
        rig.Execute(quote);
        var registry = new EffectRegistry();
        registry.Replace(rig.Service.Snapshot(Session), Session.ToString());
        Assert.Equal(30, registry.Amount(Target.ToString(), EnhancePath.ArmorDurability));
        Assert.Equal(20, registry.Amount(Target.ToString(), EnhancePath.Mobility));
    }

    [Fact]
    public void Disabling_high_risk_preserves_the_selected_normal_failure_option()
    {
        var rig = new Rig();
        rig.Seed((EnhancePath.Recoil, 5), (EnhancePath.Ergonomics, 5), (EnhancePath.Precision, 5));
        var quote = rig.Service.Quote(Session, Target.ToString(),
            new() { SuccessPercent = 0, HighRiskEnabled = false, FailureMode = FailureMode.ZeroDurability }, EnhancePath.Recoil);
        Assert.False(quote.HighRisk);
        Assert.False(quote.CanDestroy);
        rig.Execute(quote);
        Assert.Equal(0, rig.Item.Upd!.Repairable!.Durability);
        Assert.All(rig.Service.Snapshot(Session).Items, i => Assert.Equal(5, i.Level));
    }

    [Fact]
    public void Changes_to_any_other_track_invalidate_the_saved_risk_and_completion_quote_without_payment()
    {
        var rig = new Rig();
        rig.Seed((EnhancePath.Recoil, 5), (EnhancePath.Ergonomics, 5), (EnhancePath.Precision, 5));
        var quote = rig.Service.Quote(Session, Target.ToString(), new(), EnhancePath.Recoil);
        var state = Assert.IsType<EnhanceState>(rig.Backend.Saved.ExtensionData!["rfGearEnhance"]);
        state.Tracks[Target.ToString()][EnhancePath.Precision] = 4;
        Assert.Throws<InvalidOperationException>(() => rig.Execute(quote, true));
        Assert.Equal(100000, rig.Money);
    }

    [Theory]
    [InlineData(0.249999, FailureMode.Destroy)]
    [InlineData(0.25, FailureMode.Downgrade)]
    [InlineData(0.999999, FailureMode.Downgrade)]
    public void Conditional_destruction_draw_is_independent_of_the_upgrade_success_roll(double draw, FailureMode expected) =>
        Assert.Equal(expected, EnhanceRules.Failure(new() { DestructionPercent = 25, FailureMode = FailureMode.Downgrade }, draw));

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Previous_protocol_clients_are_rejected_before_a_destructive_quote_can_be_issued(int protocol)
    {
        var rig = new Rig();
        var router = new EnhanceQueries(Json, rig.Service);
        var result = await router.HandleStaticAsync("/rf/gear-enhance/quote",
            Json.Serialize(new QuoteRequest { ProtocolVersion = protocol, ItemId = Target.ToString() })!, Session, "");
        Assert.False(Json.Deserialize<ApiReply<EnhanceQuote>>((string)result)!.Ok);
        Assert.Equal(100000, rig.Money);
    }

    [Fact]
    public async Task A_downgrade_to_zero_is_not_deletion_and_the_client_reads_the_actual_server_receipt_enum()
    {
        var rig = new Rig();
        rig.Seed((EnhancePath.Recoil, 1));
        var quote = rig.Service.Quote(Session, Target.ToString(), new()
            { SuccessPercent = 0, FailureMode = FailureMode.Downgrade }, EnhancePath.Recoil);
        var registry = new EffectRegistry();
        registry.Replace(rig.Service.Snapshot(Session), Session.ToString());
        rig.Execute(quote);
        var router = new EnhanceQueries(Json, rig.Service);
        var response = await router.HandleStaticAsync("/rf/gear-enhance/receipt",
            Json.Serialize(new ReceiptRequest { ProtocolVersion = EnhanceProtocol.Version, QuoteId = quote.QuoteId })!, Session, "");
        var client = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiReply<EnhanceReceipt>>((string)response)!;
        Assert.True(client.Ok, client.Error);
        Assert.Equal(FailureMode.Downgrade, client.Data!.FailureMode);
        Assert.Equal(0, client.Data.Level);
        registry.ApplyReceipt(client.Data, Session.ToString());
        Assert.Null(registry.Find(Target.ToString()));
        Assert.Empty(rig.Service.Snapshot(Session).Items);
        Assert.Equal(80, rig.Item.Upd!.Repairable!.Durability);
        Assert.Empty(rig.Output.ProfileChanges![Session].Items!.DeletedItems!);
    }

    [Theory]
    [InlineData(100, true, false, FailureMode.KeepItem, true, false)]
    [InlineData(100, false, true, FailureMode.Destroy, false, false)]
    [InlineData(0, true, false, FailureMode.Downgrade, false, false)]
    [InlineData(0, false, true, FailureMode.KeepItem, false, true)]
    public void Protection_blocks_only_its_selected_loss_and_the_server_charges_the_saved_total(
        double destructionPercent, bool protectDestruction, bool protectDowngrade, FailureMode expected, bool savedDestruction, bool savedDowngrade)
    {
        var rig = new Rig(10000000); rig.AddPart();
        rig.Pmc.InsuredItems!.Add(new() { ItemId = Part });
        rig.Seed((EnhancePath.Recoil, 5), (EnhancePath.Ergonomics, 5), (EnhancePath.Precision, 5));
        var quote = rig.Service.Quote(Session, Target.ToString(),
            new() { SuccessPercent = 0, HighRiskDestructionPercent = destructionPercent }, EnhancePath.Recoil,
            new() { Destruction = protectDestruction, Downgrade = protectDowngrade });
        rig.Execute(quote, quote.CanDestroy);
        Assert.Equal(10000000 - quote.Cost, rig.Money);
        var receipt = rig.Service.Receipt(Session, quote.QuoteId)!;
        Assert.False(receipt.Success);
        Assert.Equal(expected, receipt.FailureMode);
        Assert.Equal(savedDestruction, receipt.DestructionPrevented);
        Assert.Equal(savedDowngrade, receipt.DowngradePrevented);
        if (expected == FailureMode.Destroy)
        {
            Assert.DoesNotContain(rig.Pmc.Inventory!.Items!, i => i.Id == Target || i.Id == Part);
            Assert.Empty(rig.Pmc.InsuredItems!);
        }
        else
        {
            Assert.Equal(80, rig.Item.Upd!.Repairable!.Durability);
            Assert.Contains(rig.Pmc.Inventory!.Items!, i => i.Id == Part);
            Assert.Single(rig.Pmc.InsuredItems!);
            Assert.Equal(expected == FailureMode.Downgrade ? 4 : 5, receipt.Level);
            Assert.Equal(5, rig.Service.Snapshot(Session).Items.Single(i => i.Path == EnhancePath.Ergonomics).Level);
        }
    }

    [Fact]
    public void Both_protections_guarantee_item_and_level_retention_without_refunding_a_failure()
    {
        var rig = new Rig(10000000); rig.AddPart();
        rig.Seed((EnhancePath.Recoil, 5), (EnhancePath.Ergonomics, 5), (EnhancePath.Precision, 5));
        var quote = rig.Service.Quote(Session, Target.ToString(), new() { SuccessPercent = 0 }, EnhancePath.Recoil,
            new() { Destruction = true, Downgrade = true });
        Assert.True(quote.Protection.Destruction);
        Assert.True(quote.Protection.Downgrade);
        Assert.False(quote.CanDestroy);
        Assert.Equal(30518, quote.BaseCost);
        Assert.Equal(549324, quote.DestructionProtectionFee);
        Assert.Equal(384527, quote.DowngradeProtectionFee);
        Assert.Equal(964369, quote.Cost);
        rig.Execute(quote);
        Assert.Equal(10000000 - quote.Cost, rig.Money);
        Assert.All(rig.Service.Snapshot(Session).Items, i => Assert.Equal(5, i.Level));
        var receipt = rig.Service.Receipt(Session, quote.QuoteId)!;
        Assert.False(receipt.Success);
        Assert.Equal(FailureMode.KeepItem, receipt.FailureMode);
        Assert.True(receipt.DestructionPrevented ^ receipt.DowngradePrevented);
        Assert.Throws<InvalidOperationException>(() => rig.Execute(quote));
        Assert.Equal(10000000 - quote.Cost, rig.Money);
        rig.Backend.Saved = Json.Deserialize<SptProfile>(Json.Serialize(rig.Backend.Saved))!;
        var restored = new EnhanceService(rig.Backend, Json);
        Assert.All(restored.Snapshot(Session).Items, i => Assert.Equal(5, i.Level));
        Assert.Equal(receipt.DestructionPrevented, restored.Receipt(Session, quote.QuoteId)!.DestructionPrevented);
        Assert.Equal(receipt.DowngradePrevented, restored.Receipt(Session, quote.QuoteId)!.DowngradePrevented);
    }

    [Theory]
    [InlineData(FailureMode.KeepItem, false)]
    [InlineData(FailureMode.ZeroDurability, false)]
    [InlineData(FailureMode.Downgrade, false)]
    [InlineData(FailureMode.Destroy, true)]
    public void Inapplicable_protection_is_removed_without_fees_including_guaranteed_success(FailureMode mode, bool available)
    {
        var rig = new Rig(10000000);
        var quote = rig.Service.Quote(Session, Target.ToString(), new() { SuccessPercent = 0, FailureMode = mode }, EnhancePath.Recoil,
            new() { Destruction = true, Downgrade = true });
        Assert.Equal(available, quote.Protection.Destruction);
        Assert.False(quote.Protection.Downgrade);
        Assert.Equal(available ? 5000 : 0, quote.ProtectionCost);
        var certain = rig.Service.Quote(Session, Target.ToString(), new() { AlwaysSucceed = true, FailureMode = mode }, EnhancePath.Recoil,
            new() { Destruction = true, Downgrade = true });
        Assert.False(certain.Protection.Destruction);
        Assert.False(certain.Protection.Downgrade);
        Assert.Equal(0, certain.ProtectionCost);
        Assert.Equal(certain.BaseCost, certain.Cost);
    }

    [Fact]
    public async Task Client_protection_checkboxes_are_part_of_the_real_server_quote_and_cannot_modify_an_old_quote()
    {
        var rig = new Rig(10000000);
        var router = new EnhanceQueries(Json, rig.Service);
        async Task<EnhanceQuote> Query(bool protection)
        {
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(new { ProtocolVersion = EnhanceProtocol.Version,
                ItemId = Target.ToString(), Path = EnhancePath.Recoil,
                Settings = new EnhanceSettings { SuccessPercent = 0, FailureMode = FailureMode.Destroy },
                Protection = new EnhanceProtection { Destruction = protection } });
            var response = await router.HandleStaticAsync("/rf/gear-enhance/quote", body, Session, "");
            var client = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiReply<EnhanceQuote>>((string)response)!;
            Assert.True(client.Ok, client.Error);
            return client.Data!;
        }
        var unprotected = await Query(false);
        var protectedQuote = await Query(true);
        Assert.True(unprotected.CanDestroy);
        Assert.False(protectedQuote.CanDestroy);
        Assert.Equal(15000, protectedQuote.Cost);
        Assert.Throws<InvalidOperationException>(() => rig.Execute(unprotected, true));
        Assert.Equal(10000000, rig.Money);
        rig.Execute(protectedQuote);
        Assert.Equal(9985000, rig.Money);
        Assert.NotNull(rig.Item);
    }

    [Fact]
    public void An_over_limit_protection_fee_does_not_reject_the_unprotected_quote_or_charge_money()
    {
        var rig = new Rig(10000000);
        rig.Seed((EnhancePath.Recoil, 9));
        var settings = new EnhanceSettings { SuccessPercent = 0, CostPercent = 1000, MinimumCost = 10000000,
            CostPerLevel = 1, FailureMode = FailureMode.Destroy };
        var quote = rig.Service.Quote(Session, Target.ToString(), settings, EnhancePath.Recoil);
        Assert.Equal(10000000, quote.Cost);
        Assert.Equal(500000000, quote.DestructionProtectionFee);
        Assert.Throws<InvalidOperationException>(() => rig.Service.Quote(Session, Target.ToString(), settings,
            EnhancePath.Recoil, new() { Destruction = true }));
        Assert.Equal(10000000, rig.Money);
    }

    [Theory]
    [InlineData(1, 5000, 3500, 18500)]
    [InlineData(5, 125000, 87500, 222500)]
    [InlineData(10, 500000, 350000, 860000)]
    public void Protection_fees_rise_quadratically_with_the_attempted_level(int next, long destruction, long downgrade, int total)
    {
        Assert.Equal(destruction, ProtectionCosts.Fee(10000, next, 50));
        Assert.Equal(downgrade, ProtectionCosts.Fee(10000, next, 35));
        Assert.Equal(total, ProtectionCosts.Total(10000, destruction, downgrade));
        Assert.Throws<InvalidOperationException>(() => ProtectionCosts.Total(100000000, 1, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evolution_requires_all_first_star_tracks_at_maximum_for_both_equipment_categories(bool weapon)
    {
        var rig = new Rig(1000000); rig.Backend.WeaponCategory = weapon;
        if (weapon) rig.Seed((EnhancePath.Recoil, 10), (EnhancePath.Ergonomics, 10), (EnhancePath.Precision, 9));
        else rig.Seed((EnhancePath.ArmorDurability, 10), (EnhancePath.Mobility, 9));
        Assert.Throws<InvalidOperationException>(() => rig.Service.Quote(Session, Target.ToString(), new(),
            action: EnhanceAction.Evolve));
        Assert.Equal(1000000, rig.Money);
        if (weapon) rig.Seed((EnhancePath.Recoil, 10), (EnhancePath.Ergonomics, 10), (EnhancePath.Precision, 10));
        else rig.Seed((EnhancePath.ArmorDurability, 10), (EnhancePath.Mobility, 10));
        var quote = rig.Service.Quote(Session, Target.ToString(), new(), action: EnhanceAction.Evolve);
        Assert.True(quote.IsEvolution);
        Assert.Equal(50, quote.SuccessPercent);
        Assert.Equal(300000, quote.Cost);
        Assert.True(quote.CanDestroy);
        Assert.False(quote.DestructionProtectionAvailable);
        Assert.False(quote.DowngradeProtectionAvailable);
    }

    [Fact]
    public void Successful_separate_evolution_persists_two_stars_and_bonus_without_changing_paid_track_levels()
    {
        var rig = new Rig(1000000);
        rig.Seed((EnhancePath.Recoil, 10), (EnhancePath.Ergonomics, 10), (EnhancePath.Precision, 10));
        var before = new EffectRegistry(); before.Replace(rig.Service.Snapshot(Session), Session.ToString());
        Assert.True(before.IsComplete(Target.ToString()));
        Assert.False(before.IsEvolved(Target.ToString()));
        Assert.Equal(15, before.Amount(Target.ToString(), EnhancePath.Recoil));
        var quote = rig.Service.Quote(Session, Target.ToString(), new() { EvolutionSuccessPercent = 100 },
            protection: new() { Destruction = true, Downgrade = true }, action: EnhanceAction.Evolve);
        Assert.Equal(0, quote.ProtectionCost);
        Assert.False(quote.CanDestroy);
        Assert.Equal(15, quote.CurrentEffect);
        Assert.Equal(18, quote.NextEffect);
        rig.Execute(quote);
        var receipt = rig.Service.Receipt(Session, quote.QuoteId)!;
        Assert.True(receipt.IsEvolution); Assert.True(receipt.Success); Assert.True(receipt.Evolved);
        Assert.Equal(700000, rig.Money);
        Assert.All(rig.Service.Snapshot(Session).Items, i => { Assert.Equal(10, i.Level); Assert.True(i.Evolved); });
        before.ApplyReceipt(receipt, Session.ToString());
        Assert.Equal("★★ 진화 +30", before.Badge(Target.ToString()));
        Assert.Equal(18, before.Amount(Target.ToString(), EnhancePath.Recoil));
        rig.Backend.Saved = Json.Deserialize<SptProfile>(Json.Serialize(rig.Backend.Saved))!;
        var restored = new EnhanceService(rig.Backend, Json);
        var registry = new EffectRegistry(); registry.Replace(restored.Snapshot(Session), Session.ToString());
        Assert.True(registry.IsEvolved(Target.ToString()));
        Assert.Equal(18, registry.Amount(Target.ToString(), EnhancePath.Recoil));
        Assert.Equal(12, registry.Amount(Target.ToString(), EnhancePath.Ergonomics));
        Assert.Throws<InvalidOperationException>(() => restored.Quote(Session, Target.ToString(), new(), action: EnhanceAction.Evolve));
        Assert.Throws<InvalidOperationException>(() => rig.Execute(quote));
        Assert.Equal(700000, rig.Money);
    }

    [Fact]
    public void Failed_evolution_ignores_requested_protection_requires_confirmation_and_destroys_the_entire_tree()
    {
        var rig = new Rig(1000000); rig.AddPart(); rig.Pmc.InsuredItems!.Add(new() { ItemId = Part });
        rig.Seed((EnhancePath.Recoil, 10), (EnhancePath.Ergonomics, 10), (EnhancePath.Precision, 10));
        var quote = rig.Service.Quote(Session, Target.ToString(), new() { EvolutionSuccessPercent = 0 },
            protection: new() { Destruction = true, Downgrade = true }, action: EnhanceAction.Evolve);
        Assert.False(quote.Protection.Destruction); Assert.False(quote.Protection.Downgrade);
        Assert.Equal(0, quote.ProtectionCost);
        Assert.Throws<InvalidOperationException>(() => rig.Execute(quote));
        Assert.Equal(1000000, rig.Money);
        rig.Execute(quote, true);
        Assert.Equal(700000, rig.Money);
        var receipt = rig.Service.Receipt(Session, quote.QuoteId)!;
        Assert.True(receipt.IsEvolution); Assert.False(receipt.Success); Assert.False(receipt.Evolved);
        Assert.Equal(FailureMode.Destroy, receipt.FailureMode);
        Assert.False(receipt.DestructionPrevented);
        Assert.Empty(rig.Service.Snapshot(Session).Items);
        Assert.DoesNotContain(rig.Pmc.Inventory!.Items!, i => i.Id == Target || i.Id == Part);
        Assert.Empty(rig.Pmc.InsuredItems!);
    }

    [Fact]
    public async Task Evolution_kind_survives_the_actual_client_router_and_receipt_serializers()
    {
        var rig = new Rig(1000000);
        rig.Seed((EnhancePath.Recoil, 10), (EnhancePath.Ergonomics, 10), (EnhancePath.Precision, 10));
        var router = new EnhanceQueries(Json, rig.Service);
        var body = Newtonsoft.Json.JsonConvert.SerializeObject(new { ProtocolVersion = EnhanceProtocol.Version,
            ItemId = Target.ToString(), Action = EnhanceAction.Evolve, Settings = new EnhanceSettings { AlwaysSucceed = true } });
        var response = await router.HandleStaticAsync("/rf/gear-enhance/quote", body, Session, "");
        var quote = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiReply<EnhanceQuote>>((string)response)!;
        Assert.True(quote.Ok, quote.Error); Assert.True(quote.Data!.IsEvolution);
        rig.Execute(quote.Data);
        var receipt = rig.Service.Receipt(Session, quote.Data.QuoteId)!;
        var clientReceipt = Newtonsoft.Json.JsonConvert.DeserializeObject<EnhanceReceipt>(Json.Serialize(receipt)!)!;
        Assert.True(clientReceipt.IsEvolution); Assert.True(clientReceipt.Evolved);
    }
}
