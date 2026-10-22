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
    public TestItem(string id) => this.id = id;
}
public record TestAddress(TestContainer Container);
public record TestContainer(TestItem ParentItem);
public record TestArmor(TestItem Item);

public sealed class HooksTests : IDisposable
{
    public HooksTests() { StatPatches.Registry.Clear(); StatPatches.Registry.Configure(new()); }
    public void Dispose() => StatPatches.Registry.Clear();
    private static void Record(string id, EnhancePath path, int level = 10) => StatPatches.Registry.Replace(
        new EnhanceSnapshot { ProfileId = "pmc", Items = new[] { new EnhanceRecord { ItemId = id, Path = path, Level = level } } }, "pmc");
    private static float Run(string method, object target, float value)
    {
        var args = new object[] { target, value };
        typeof(StatPatches).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        return (float)args[1];
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
        Record("vest", EnhancePath.ArmorDurability);
        var vest = new TestItem("vest");
        var plate = new TestItem("plate") { CurrentAddress = new(new(vest)) };
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
        a.CurrentAddress = new(new(b)); b.CurrentAddress = new(new(a));
        Assert.Equal(0, StatPatches.Amount(a, EnhancePath.ArmorDurability, true));
        Assert.InRange(a.Reads + b.Reads, 1, 64);
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
        Assert.Equal(54, Run("Ergonomics", item, 50));
        Assert.Equal(0.93f, Run("Precision", item, 1), 5);
        Assert.Equal("★ +21", StatPatches.Registry.Badge("gun"));
    }

    [Fact]
    public void Both_armor_effects_work_together_and_inherit_by_the_requested_stat()
    {
        StatPatches.Registry.Replace(new EnhanceSnapshot { ProfileId = "pmc", Items = new[] {
            new EnhanceRecord { ItemId = "vest", Path = EnhancePath.ArmorDurability, Level = 10 },
            new EnhanceRecord { ItemId = "vest", Path = EnhancePath.Mobility, Level = 5 },
            new EnhanceRecord { ItemId = "plate", Path = EnhancePath.ArmorDurability, Level = 1 }
        } }, "pmc");
        var vest = new TestItem("vest");
        var plate = new TestItem("plate") { CurrentAddress = new(new(vest)) };
        Assert.Equal(9.7f, Run("Durability", new TestArmor(plate), 10), 4);
        Assert.Equal(-9, Run("Mobility", new TestArmor(plate), -10));
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
