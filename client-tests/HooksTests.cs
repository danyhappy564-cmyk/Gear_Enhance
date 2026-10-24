using System.Reflection;
using System.Reflection.Emit;
using GearEnhance;
using HarmonyLib;
using Xunit;

namespace GearEnhanceClientTests;

public class TestItem
{
    private readonly string id;
    public int Reads { get; private set; }
    public string Id { get { Reads++; return id; } }
    public TestAddress? CurrentAddress { get; set; }
    public TestTemplate? Template { get; set; }
    public TestItem(string id) => this.id = id;
}
public record TestAddress(object Container);
// Armor plate slots and helmet visor slots are EFT Slots; pockets are grids.
public class TestContainer(TestItem parentItem) : EFT.InventoryLogic.Slot { public TestItem ParentItem { get; } = parentItem; }
public record TestGrid(TestItem ParentItem);
public record TestTemplate(string _id, TestTemplate? Parent = null);
public record TestArmor(TestItem Item);

public sealed class HooksTests : IDisposable
{
    public HooksTests()
    {
        StatPatches.Registry.Clear(); StatPatches.Registry.Configure(new());
        // Seed the runtime type cache so the .NET test host never scans for game types.
        var cache = (System.Collections.Concurrent.ConcurrentDictionary<string, Type>)typeof(GameBridge)
            .GetField("runtimeTypes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        cache["EFT.InventoryLogic.Slot"] = typeof(EFT.InventoryLogic.Slot);
        cache["EFT.InventoryLogic.Weapon"] = typeof(TestWeapon);
    }
    public class TestWeapon;
    private static readonly TestTemplate Armored = new("57bef4c42459772e8d35a53b");
    private static TestItem Vest() => new("vest") { Template = new("5448e5284bdc2dcb718b4567") };
    private static TestItem Plate(string id, object? container) => new(id)
    {
        Template = new("644120aa86ffbe10ee032b6f", Armored),
        CurrentAddress = container == null ? null : new(container)
    };
    private static string Named(TestItem item)
    {
        var args = new object[] { new MarkerView(item), "Plate" };
        typeof(ItemMarkers).GetMethod("Name", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        return (string)args[1];
    }
    public void Dispose() => StatPatches.Registry.Clear();
    private static void Record(string id, EnhancePath path, int level = 10) => StatPatches.Registry.Replace(
        new EnhanceSnapshot { ProfileId = "pmc", Items = new[] { new EnhanceRecord { ItemId = id, Path = path, Level = level } } }, "pmc");
    private static float Run(string method, object target, float value)
    {
        var args = new object[] { target, value };
        typeof(StatPatches).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        return (float)args[1];
    }

    // 1.2.0: parts get no "Enhance gear" menu entry; worn armor and weapons keep it.
    [Fact]
    public void Plates_inserts_and_helmet_add_ons_are_parts_but_worn_armor_is_not()
    {
        TestItem Of(string id, TestTemplate? parent = null) => new(id) { Template = new(id, parent) };
        var plate = Of("644120aa86ffbe10ee032b6f", Armored);
        var insert = Of("65649eb40bf0ed77b8044453", Armored);
        var visor = Of("5a16b672fcdbcb001912fa83", Armored);
        var helmet = Of("5a341c4086f77401f2541505", Armored);
        var armor = Of("5448e54d4bdc2dcc718b4568", Armored);
        var rig = Vest();
        var gun = Of("5447b5cf4bdc2d65278b4567", new("5422acb9af1c889c16000029"));
        foreach (var part in new[] { plate, insert, visor })
        {
            Assert.True(GameBridge.Eligible(part));
            Assert.True(GameBridge.IsArmorPart(part));
        }
        foreach (var worn in new[] { helmet, armor, rig, gun })
        {
            Assert.True(GameBridge.Eligible(worn));
            Assert.False(GameBridge.IsArmorPart(worn));
        }
        Assert.False(GameBridge.IsArmorPart(new TestItem("loot") { Template = new("543be5cb4bdc2deb348b4568") }));
    }

    [Fact]
    public void Recoil_postfix_scales_existing_attachment_multiplier_once_per_call()
    {
        Record("gun", EnhancePath.Recoil);
        var gun = new TestItem("gun");
        Assert.Equal(-0.49f, Run("Recoil", gun, -0.4f), 5);
        Assert.Equal(-0.49f, Run("Recoil", gun, -0.4f), 5);
        Assert.Equal(-0.4f, Run("Recoil", new TestItem("other"), -0.4f));
    }

    [Fact]
    public void Ergonomics_postfix_adds_flat_points_and_clamps_to_game_limit()
    {
        Record("gun", EnhancePath.Ergonomics);
        Assert.Equal(60, Run("Ergonomics", new TestItem("gun"), 50));
        Assert.Equal(100, Run("Ergonomics", new TestItem("gun"), 95));
        Assert.Equal(50, Run("Ergonomics", new TestItem("other"), 50));
    }

    [Fact]
    public void Precision_postfix_reduces_dispersion_without_adding_ergonomics()
    {
        Record("gun", EnhancePath.Precision);
        var gun = new TestItem("gun");
        Assert.Equal(0.9f, Run("Precision", gun, 1));
        Assert.Equal(50, Run("Ergonomics", gun, 50));
    }

    [Fact]
    public void Durability_prefix_scales_actual_damage_argument_for_attached_plate()
    {
        StatPatches.Registry.Replace(new EnhanceSnapshot { ProfileId = "pmc",
            Items = new[] { new EnhanceRecord { ItemId = "vest", Path = EnhancePath.ArmorDurability, Level = 10 } },
            Inserts = new[] { new InsertLink { ArmorId = "vest", PartIds = new[] { "plate" } } } }, "pmc");
        var vest = new TestItem("vest");
        var plate = new TestItem("plate") { CurrentAddress = new(new TestContainer(vest)) };
        Assert.Equal(7, Run("Durability", new TestArmor(plate), 10));
        Assert.Equal(-10, Run("Durability", new TestArmor(plate), -10));
        Assert.Equal(10, Run("Durability", new TestArmor(new("other")), 10));
    }

    [Fact]
    public void Mobility_postfix_reduces_negative_penalties_and_preserves_positive_bonuses()
    {
        Record("vest", EnhancePath.Mobility);
        var armor = new TestArmor(new("vest"));
        Assert.Equal(-8, Run("Mobility", armor, -10));
        Assert.Equal(5, Run("Mobility", armor, 5));
    }

    [Fact]
    public void Live_tuning_changes_existing_item_effects_without_repeated_upgrade()
    {
        Record("gun", EnhancePath.Recoil);
        StatPatches.Registry.Configure(new() { RecoilPerLevel = 2, RecoilCap = 20 });
        Assert.Equal(-0.2f, Run("Recoil", new TestItem("gun"), 0), 5);
        StatPatches.Registry.Configure(new() { RecoilPerLevel = 0 });
        Assert.Equal(0, Run("Recoil", new TestItem("gun"), 0));
    }

    [Fact]
    public void Native_parent_cycle_has_bounded_reads()
    {
        var a = new TestItem("a"); var b = new TestItem("b");
        a.CurrentAddress = new(new TestContainer(b)); b.CurrentAddress = new(new TestContainer(a));
        Assert.Equal(0, StatPatches.Amount(a, EnhancePath.ArmorDurability, true));
        Assert.InRange(a.Reads + b.Reads, 1, 64);
        // Badge and redraw walks are bounded the same way.
        a.Template = b.Template = new("644120aa86ffbe10ee032b6f", Armored);
        Assert.Equal("Plate", Named(a));
        Assert.InRange(a.Reads + b.Reads, 1, 200);
    }

    [Fact]
    public void Actual_display_transpiler_emits_valid_owner_aware_code_and_leaves_template_unchanged()
    {
        Record("vest", EnhancePath.Mobility);
        var owner = new EFT.InventoryLogic.EquipmentPenaltyComponent(new("vest"), new() { SpeedPenaltyPercent = -10 });
        var source = new[] {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldfld, typeof(EFT.InventoryLogic.EquipmentPenaltyComponent).GetField("Template")),
            new CodeInstruction(OpCodes.Callvirt, typeof(TestPenaltyTemplate).GetProperty("SpeedPenaltyPercent")!.GetMethod),
            new CodeInstruction(OpCodes.Ret)
        };
        var patched = (IEnumerable<CodeInstruction>)typeof(StatPatches).GetMethod("PenaltyDisplay", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { source })!;
        var generated = new DynamicMethod("PenaltyFixture", typeof(float), new[] { owner.GetType() }, typeof(StatPatches), true);
        var il = generated.GetILGenerator();
        foreach (var instruction in patched)
        {
            if (instruction.operand is MethodInfo method) il.Emit(instruction.opcode, method);
            else if (instruction.operand is FieldInfo field) il.Emit(instruction.opcode, field);
            else il.Emit(instruction.opcode);
        }
        var result = generated.Invoke(null, new object[] { owner });
        Assert.Equal(-8f, result);
        Assert.Equal(-10, owner.Template.SpeedPenaltyPercent);
    }

    [Fact]
    public void All_three_weapon_effects_are_applied_together_without_replacing_each_other()
    {
        StatPatches.Registry.Replace(new EnhanceSnapshot { ProfileId = "pmc", Items = new[] {
            new EnhanceRecord { ItemId = "gun", Path = EnhancePath.Recoil, Level = 10 },
            new EnhanceRecord { ItemId = "gun", Path = EnhancePath.Ergonomics, Level = 4 },
            new EnhanceRecord { ItemId = "gun", Path = EnhancePath.Precision, Level = 7 }
        } }, "pmc");
        var item = new TestItem("gun");
        Assert.Equal(-0.49f, Run("Recoil", item, -0.4f), 5);
        Assert.Equal(52.105263f, Run("Ergonomics", item, 50), 5);
        Assert.Equal(0.9536842f, Run("Precision", item, 1), 5);
        Assert.Equal("★ +21", StatPatches.Registry.Badge("gun"));
    }

    [Fact]
    public void Both_armor_effects_work_together_and_inherit_by_the_requested_stat()
    {
        StatPatches.Registry.Replace(new EnhanceSnapshot { ProfileId = "pmc", Items = new[] {
            new EnhanceRecord { ItemId = "vest", Path = EnhancePath.ArmorDurability, Level = 10 },
            new EnhanceRecord { ItemId = "vest", Path = EnhancePath.Mobility, Level = 5 },
            new EnhanceRecord { ItemId = "plate", Path = EnhancePath.ArmorDurability, Level = 1 }
        }, Inserts = new[] { new InsertLink { ArmorId = "vest", PartIds = new[] { "plate" } } } }, "pmc");
        var vest = new TestItem("vest");
        var plate = new TestItem("plate") { CurrentAddress = new(new TestContainer(vest)) };
        // 1.2.0: an inserted plate takes the higher of its own and the armor's level.
        Assert.Equal(7, Run("Durability", new TestArmor(plate), 10), 4);
        Assert.Equal(-9.473684f, Run("Mobility", new TestArmor(plate), -10), 4);
    }

    [Fact]
    public void Plate_inserted_in_enhanced_armor_gets_its_effect_and_badge_until_removed()
    {
        var records = new[] {
            new EnhanceRecord { ItemId = "vest", Path = EnhancePath.ArmorDurability, Level = 10 },
            new EnhanceRecord { ItemId = "vest", Path = EnhancePath.Mobility, Level = 5 }
        };
        StatPatches.Registry.Replace(new EnhanceSnapshot { ProfileId = "pmc", Items = records,
            Inserts = new[] { new InsertLink { ArmorId = "vest", PartIds = new[] { "plate" } } } }, "pmc");
        var vest = Vest();
        var plate = Plate("plate", new TestContainer(vest));
        Assert.Equal(7, Run("Durability", new TestArmor(plate), 10), 4);
        Assert.Equal("Plate <color=#D9B66F>★ +15</color>", Named(plate));
        // Swapped into another armor's slot or kept loose in the enhanced rig's pocket: nothing.
        plate.CurrentAddress = new(new TestGrid(vest));
        Assert.Equal(10, Run("Durability", new TestArmor(plate), 10), 4);
        Assert.Equal("Plate", Named(plate));
        plate.CurrentAddress = new(new TestContainer(new TestItem("plain") { Template = new("5448e5284bdc2dcb718b4567") }));
        Assert.Equal(10, Run("Durability", new TestArmor(plate), 10), 4);
        Assert.Equal("Plate", Named(plate));
        // 1.2.0: a newly bought plate pushed into the enhanced armor waits for registration,
        // so swapping in a better plate after a cheap enhancement does not inherit it.
        var bought = Plate("bought", new TestContainer(vest));
        Assert.Equal(10, Run("Durability", new TestArmor(bought), 10), 4);
        Assert.Equal("Plate", Named(bought));
        StatPatches.Registry.Replace(new EnhanceSnapshot { ProfileId = "pmc", Items = records,
            Inserts = new[] { new InsertLink { ArmorId = "vest", PartIds = new[] { "plate", "bought" } } } }, "pmc");
        Assert.Equal(7, Run("Durability", new TestArmor(bought), 10), 4);
        Assert.Equal("Plate <color=#D9B66F>★ +15</color>", Named(bought));
    }

    [Fact]
    public void Separately_enhanced_plate_keeps_its_higher_level_inside_weaker_armor()
    {
        StatPatches.Registry.Replace(new EnhanceSnapshot { ProfileId = "pmc", Items = new[] {
            new EnhanceRecord { ItemId = "vest", Path = EnhancePath.ArmorDurability, Level = 2 },
            new EnhanceRecord { ItemId = "plate", Path = EnhancePath.ArmorDurability, Level = 10 }
        } }, "pmc");
        var plate = Plate("plate", new TestContainer(Vest()));
        Assert.Equal(7, Run("Durability", new TestArmor(plate), 10), 4);
        Assert.Equal("Plate <color=#D9B66F>★ +10</color>", Named(plate));
    }

    [Fact]
    public void Weapon_attachments_never_show_the_weapon_badge()
    {
        Record("gun", EnhancePath.Recoil);
        var gun = new TestItem("gun") { Template = new("5422acb9af1c889c16000029") };
        var scope = new TestItem("scope") { CurrentAddress = new(new TestContainer(gun)) };
        Assert.Equal("Plate", Named(scope));
    }

    [Fact]
    public void Completed_weapon_bonus_reaches_all_real_postfixes_and_the_purple_name_marker()
    {
        foreach (var path in new[] { EnhancePath.Recoil, EnhancePath.Ergonomics, EnhancePath.Precision })
            StatPatches.Registry.ApplyReceipt(new() { Success = true, ItemId = "gun", Level = 10, Path = path }, "pmc");
        Assert.False(StatPatches.Registry.IsEvolved("gun"));
        Assert.Equal(15, StatPatches.Registry.Amount("gun", EnhancePath.Recoil));
        StatPatches.Registry.ApplyReceipt(new() { Success = true, ItemId = "gun", IsEvolution = true, Evolved = true }, "pmc");
        var gun = new TestItem("gun");
        Assert.Equal(-0.58f, Run("Recoil", gun, -0.4f), 5);
        Assert.Equal(70, Run("Ergonomics", gun, 50));
        Assert.Equal(0.8f, Run("Precision", gun, 1), 5);
        var args = new object[] { new MarkerView(gun), "K2" };
        typeof(ItemMarkers).GetMethod("Name", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        Assert.Equal("K2 <color=#B989F5>★★ 진화 +30</color>", args[1]);
        StatPatches.Registry.ApplyReceipt(new() { ItemId = "gun", Path = EnhancePath.Recoil, Level = 9, FailureMode = FailureMode.Downgrade, Evolved = true }, "pmc");
        args[1] = "K2";
        typeof(ItemMarkers).GetMethod("Name", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        Assert.Equal("K2 <color=#B989F5>★★ 진화 +29</color>", args[1]);
    }

    [Fact]
    public void Third_star_triples_the_base_effect_in_real_hooks_and_red_three_star_name()
    {
        foreach (var path in new[] { EnhancePath.Recoil, EnhancePath.Ergonomics, EnhancePath.Precision })
            StatPatches.Registry.ApplyReceipt(new() { Success = true, ItemId = "gun", Level = 10, Path = path }, "pmc");
        StatPatches.Registry.ApplyReceipt(new() { Success = true, ItemId = "gun", IsEvolution = true, Evolved = true, StarLevel = 3 }, "pmc");
        var gun = new TestItem("gun");
        Assert.Equal(-0.67f, Run("Recoil", gun, -0.4f), 5);
        Assert.Equal(80f, Run("Ergonomics", gun, 50));
        Assert.Equal(0.7f, Run("Precision", gun, 1), 5);
        var args = new object[] { new MarkerView(gun), "K2" };
        typeof(ItemMarkers).GetMethod("Name", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        Assert.Equal("K2 <color=#F06A6A>★★★ 진화 +30</color>", args[1]);
        Assert.Equal(FeedbackCue.Complete, GameFeedback.Result(new() { Success = true, IsEvolution = true, Evolved = true, StarLevel = 3 }, true));
        Assert.Equal(4, EmbeddedSounds.Index(FeedbackCue.Complete));
    }

    public record MarkerView(TestItem Item) { public bool Examined => true; }

    [Fact]
    public void Completed_item_stat_reads_do_not_allocate_or_scan_the_inventory()
    {
        foreach (var path in new[] { EnhancePath.Recoil, EnhancePath.Ergonomics, EnhancePath.Precision })
            StatPatches.Registry.ApplyReceipt(new() { Success = true, ItemId = "gun", Level = 10, Path = path }, "pmc");
        StatPatches.Registry.ApplyReceipt(new() { Success = true, ItemId = "gun", IsEvolution = true, Evolved = true }, "pmc");
        for (var warmup = 0; warmup < 100; warmup++) StatPatches.Registry.Amount("gun", EnhancePath.Recoil);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var amount = 0.0;
        for (var frame = 0; frame < 10000; frame++) amount = StatPatches.Registry.Amount("gun", EnhancePath.Recoil);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(30, amount);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Cached_runtime_type_reads_do_not_scan_assemblies_or_allocate_in_the_frame_loop()
    {
        var cache = (System.Collections.Concurrent.ConcurrentDictionary<string, Type>)typeof(GameBridge)
            .GetField("runtimeTypes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        const string key = "GearEnhance.Tests.CachedStatus";
        cache[key] = typeof(FieldRaidStatus);
        for (var warmup = 0; warmup < 100; warmup++) GameBridge.RuntimeType(key);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Type? last = null;
        for (var frame = 0; frame < 10000; frame++) last = GameBridge.RuntimeType(key);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(typeof(FieldRaidStatus), last);
        Assert.Equal(0, allocated);
        // A cache miss would initialize Harmony's Mono-specific resolver in this
        // .NET host; this also proves the assembly scan was never reached.
    }

    [Fact]
    public void Static_raid_status_supports_a_field_or_property_without_harmony_lookup_warnings()
    {
        Assert.False((bool)GameBridge.Get(typeof(FieldRaidStatus), "InRaid"));
        Assert.True((bool)GameBridge.Get(typeof(PropertyRaidStatus), "InRaid"));
        Assert.Null(GameBridge.Get(typeof(FieldRaidStatus), "Missing"));
    }

    public static class FieldRaidStatus { public static bool InRaid = false; }
    public static class PropertyRaidStatus { public static bool InRaid => true; }

    [Fact]
    public void Setter_only_override_reads_the_inherited_getter_like_diz_bindable_state()
    {
        // Diz.Binding.BindableState<T> overrides only the Value setter. Reading the
        // derived declaration threw and stopped ItemView.Init before SetActive(true).
        var state = new TestBindableState<bool> { Value = true };
        Assert.True((bool)GameBridge.Get(state, "Value"));
        state.Value = false;
        Assert.False((bool)GameBridge.Get(state, "Value"));
    }

    public class TestBaseBindable<T> { protected T Current = default!; public virtual T Value { get => Current; set => Current = value; } }
    public class TestBindableState<T> : TestBaseBindable<T> { public override T Value { set => Current = value; } }

    [Fact]
    public async Task Native_callback_binding_and_flush_result_are_compatible_with_typed_delegate()
    {
        var session = new TestSession();
        await GameBridge.Flush(session);
        Assert.True(await GameBridge.Execute(session, "quote", false));
        Assert.NotNull(session.LastOperation);
    }

    public interface ITestResult { bool Succeed { get; } }
    public record TestResult(bool Succeed) : ITestResult;
    public delegate void TestCallback(ITestResult result);
    public class TestSession
    {
        public object? LastOperation { get; private set; }
        public Task<TestResult> FlushOperationQueue() => Task.FromResult(new TestResult(true));
        public void SendOperationRightNow(object operation, TestCallback callback)
        { LastOperation = operation; callback(new TestResult(true)); }
    }
}

public class TestPenaltyTemplate { public float SpeedPenaltyPercent { get; set; } }
