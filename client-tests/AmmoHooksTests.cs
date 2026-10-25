using System.Reflection;
using System.Reflection.Emit;
using GearEnhance;
using HarmonyLib;
using Xunit;

namespace GearEnhanceClientTests;

public sealed class AmmoHooksTests : IDisposable
{
    public sealed class Address(object container) { public object Container = container; }
    public sealed class Slot(object parent) { public object ParentItem = parent; }
    public sealed class StackSlot(object parent) { public object ParentItem = parent; }
    public sealed class Grid(object parent) { public object ParentItem = parent; }
    public sealed class Owner(object root) { public object RootItem = root; }
    public class Item(string id)
    {
        public string Id = id;
        public Address? CurrentAddress;
        public Owner? Owner;
        public object Template = new();
    }
    public sealed class Gun(string id) : Item(id) { public object[] Chambers = Array.Empty<object>(); }
    public sealed class LoadedSlot(object item) { public object ContainedItem = item; }
    public sealed class Player(string profile) { public string ProfileId = profile; public Inventory? Inventory; }
    public sealed class Inventory(object equipment) { public object Equipment = equipment; }
    public sealed class World(Player player) { public Player MainPlayer = player; }
    public static World? CurrentWorld { get; set; }
    public sealed class Controller(Gun gun, Player player) { public Gun Item = gun; public Player _player = player; }

    private static Type Patches => typeof(StatSheet).Assembly.GetType("GearEnhance.AmmoPatches", true)!;
    private readonly object equipment = new();
    private readonly Gun gun = new("gun");

    public AmmoHooksTests()
    {
        StatPatches.Registry.Clear();
        Set("enabled", true);
        Set("weaponType", typeof(Gun)); Set("slotType", typeof(Slot)); Set("stackType", typeof(StackSlot));
        Set("address", (Func<object, object?>)(x => ((Item)x).CurrentAddress));
        Set("container", (Func<object, object?>)(x => ((Address)x).Container));
        Set("slotParent", (Func<object, object?>)(x => ((Slot)x).ParentItem));
        Set("stackParent", (Func<object, object?>)(x => ((StackSlot)x).ParentItem));
        Set("owner", (Func<object, object?>)(x => ((Item)x).Owner));
        Set("ownerRoot", (Func<object, object?>)(x => ((Owner)x).RootItem));
        Set("template", (Func<object, object?>)(x => ((Item)x).Template));
        Set("controllerWeapon", (Func<object, object?>)(x => ((Controller)x).Item));
        Set("controllerPlayer", (Func<object, object?>)(x => ((Controller)x)._player));
        Set("playerProfile", (Func<object, object?>)(x => ((Player)x).ProfileId));
        Set("chambers", (Func<object, object?>)(x => ((Gun)x).Chambers));
        Set("slotItem", (Func<object, object?>)(x => ((LoadedSlot)x).ContainedItem));
        Set("magazine", (Func<object, object?>)(_ => null));
        MythicField("world", null); MythicField("worldInstance", null);
        MythicField("mainProfile", "pmc"); MythicField("mainEquipment", equipment);
        gun.Owner = new(equipment);
        Load(5);
    }

    public void Dispose()
    {
        StatPatches.Registry.Clear(); Set("enabled", false);
        MythicField("mainProfile", null); MythicField("mainEquipment", null);
        MythicField("worldInstance", null); MythicField("world", null); CurrentWorld = null;
    }

    private static void Set(string name, object? value) => Patches.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
    private static void MythicField(string name, object? value) => typeof(MythicPatches)
        .GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
    private static object? Call(string name, params object?[] args) => Patches
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, args);
    private static void Load(int level) => StatPatches.Registry.Replace(new EnhanceSnapshot
    {
        ProfileId = "pmc", Items = new[] { new EnhanceRecord { ItemId = "gun", Path = EnhancePath.Recoil, Level = 10 } },
        Mythic = new[] { new MythicLink { ItemId = "gun", Level = level } }
    }, "pmc");
    private static Item In(object parent, bool stack = false) => new("round")
    {
        CurrentAddress = new(stack ? new StackSlot(parent) : new Slot(parent))
    };
    private static int Int(object ammo, int value, int kind) => (int)Call("ReadInt", ammo, value, kind)!;
    private static float Float(object ammo, float value, int kind) => (float)Call("ReadFloat", ammo, value, kind)!;

    // Reversing the harmful sign or changing the multiplier's neutral point breaks these rows.
    [Theory]
    [InlineData(0, -12, 0)] [InlineData(0, 0, 0)] [InlineData(0, 15, 15)]
    [InlineData(1, 12, 0)] [InlineData(1, 0, 0)] [InlineData(1, -15, -15)]
    [InlineData(1, int.MinValue, int.MinValue)] [InlineData(0, int.MaxValue, int.MaxValue)]
    public void Only_bad_accuracy_and_recoil_are_neutralized(int kind, int value, int expected) =>
        Assert.Equal(expected, Int(In(gun), value, kind));

    [Theory]
    [InlineData(2, 1.4f, 1f)] [InlineData(2, 0.7f, 0.7f)] [InlineData(2, 1f, 1f)]
    [InlineData(3, 1.4f, 1f)] [InlineData(3, 0.7f, 0.7f)] [InlineData(3, 1f, 1f)]
    [InlineData(4, 0.2f, 0f)] [InlineData(4, 0f, 0f)] [InlineData(4, -0.2f, -0.2f)]
    [InlineData(5, 0.2f, 0f)] [InlineData(5, 0f, 0f)] [InlineData(5, -0.2f, -0.2f)]
    [InlineData(6, 0.2f, 0f)] [InlineData(6, 0f, 0f)] [InlineData(6, -0.2f, -0.2f)]
    [InlineData(7, 1.2f, 1f)] [InlineData(7, 0.8f, 0.8f)] [InlineData(7, 1f, 1f)]
    public void Burn_heat_and_actual_shot_spread_use_one_and_chances_use_zero(int kind, float value, float expected) =>
        Assert.Equal(expected, Float(In(gun), value, kind));

    [Fact]
    public void Chamber_and_inserted_magazine_work_but_stash_loose_magazines_and_other_guns_do_not()
    {
        Assert.Equal(0, Int(In(gun), 10, 1));
        var magazine = In(gun);
        var ammo = In(magazine, true);
        Assert.Equal(0, Int(ammo, 10, 1));
        // Moving the magazine does not change the round's address: the ancestor cache must notice.
        magazine.CurrentAddress = new(new Grid(equipment));
        Assert.Equal(10, Int(ammo, 10, 1));
        magazine.CurrentAddress = new(new Slot(new Gun("other") { Owner = new(equipment) }));
        Assert.Equal(10, Int(ammo, 10, 1));
        Assert.Equal(10, Int(new Item("loose"), 10, 1));
        Assert.Equal(10, Int(new Item("stash") { CurrentAddress = new(new Grid(gun)) }, 10, 1));
    }

    [Fact]
    public void Same_round_moved_out_or_gun_picked_up_by_a_bot_loses_the_effect_immediately()
    {
        var ammo = In(gun);
        Assert.Equal(0, Int(ammo, 10, 1));
        ammo.CurrentAddress = null;
        Assert.Equal(10, Int(ammo, 10, 1));
        ammo.CurrentAddress = new(new Slot(gun));
        Assert.Equal(0, Int(ammo, 10, 1));
        gun.Owner!.RootItem = new object();
        Assert.Equal(10, Int(ammo, 10, 1));
    }

    [Fact]
    public void Registry_changes_revoke_and_restore_level_five_without_stale_cache()
    {
        var ammo = In(gun);
        Assert.Equal(0, Int(ammo, 10, 1));
        Load(4); Assert.Equal(10, Int(ammo, 10, 1));
        Load(0); Assert.Equal(10, Int(ammo, 10, 1));
        Load(5); Assert.Equal(0, Int(ammo, 10, 1));
        StatPatches.Registry.Clear(); Assert.Equal(10, Int(ammo, 10, 1));
    }

    [Fact]
    public void Parent_cycles_stop_and_never_borrow_an_unrelated_weapon()
    {
        var ammo = new Item("cycle"); ammo.CurrentAddress = new(new Slot(ammo));
        Assert.Equal(10, Int(ammo, 10, 1));
    }

    [Fact]
    public void Empty_weapon_default_ammo_is_unchanged_and_loaded_accuracy_keeps_bonuses()
    {
        Assert.Equal(1.2f, Call("WeaponFactor", 1.2f, gun));
        gun.Chambers = new object[] { new LoadedSlot(In(gun)) };
        Assert.Equal(1f, Call("WeaponFactor", 1.2f, gun));
        Assert.Equal(0.8f, Call("WeaponFactor", 0.8f, gun));
    }

    [Fact]
    public void Hideout_copy_uses_the_settled_original_effect_id()
    {
        var copy = new Gun("hideout-copy") { Owner = new(equipment) };
        var linkType = typeof(CloneLinks).GetNestedType("Link", BindingFlags.NonPublic)!;
        var link = Activator.CreateInstance(linkType, true)!;
        linkType.GetField("Version", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(link, StatPatches.Registry.Version);
        linkType.GetField("Id", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(link, "gun");
        var links = typeof(CloneLinks).GetField("links", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        links.GetType().GetMethod("Add")!.Invoke(links, new[] { copy, link });
        Assert.Equal(0, Int(In(copy), 12, 1));
    }

    private static object Begin(Controller controller, Item ammo)
    {
        object?[] args = { controller, ammo, null };
        Patches.GetMethod("BeginShot", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        return args[2]!;
    }

    [Fact]
    public void Detached_shot_and_exact_template_reads_work_and_nested_bot_scope_restores_on_exception()
    {
        var ammo = new Item("fired");
        var state = Begin(new(gun, new("pmc")), ammo);
        try
        {
            Assert.Equal(0, Int(ammo, 12, 1));
            Assert.Equal(1f, Call("TemplateFloat", ammo.Template, 1.4f, 2));
            Assert.Equal(1.4f, Call("TemplateFloat", new object(), 1.4f, 2));
            var nested = Begin(new(gun, new("bot")), ammo);
            Assert.Equal(12, Int(ammo, 12, 1));
            var failure = new InvalidOperationException("shot failed");
            Assert.Same(failure, Call("EndShot", failure, nested));
            Assert.Equal(0, Int(ammo, 12, 1));
        }
        finally { Call("EndShot", null, state); }
        Assert.Equal(12, Int(ammo, 12, 1));
        Assert.Equal(1.4f, Call("TemplateFloat", ammo.Template, 1.4f, 2));
    }

    [Fact]
    public void Raw_status_reads_and_runtime_failure_leave_values_alone()
    {
        var ammo = In(gun);
        StatPatches.Raw = true;
        try { Assert.Equal(12, Int(ammo, 12, 1)); }
        finally { StatPatches.Raw = false; }
        Set("address", (Func<object, object?>)(_ => throw new InvalidOperationException("address unavailable")));
        Assert.Equal(12, Int(ammo, 12, 1));
        // Once failed, the feature must stay disabled even if the reader recovers.
        Set("address", (Func<object, object?>)(x => ((Item)x).CurrentAddress));
        Assert.Equal(12, Int(ammo, 12, 1));
    }

    [Fact]
    public void Rewritten_getter_and_template_field_execute_without_mutating_the_template()
    {
        var getterMap = (Dictionary<MethodInfo, int>)Patches.GetField("getters", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var fieldMap = (Dictionary<FieldInfo, int>)Patches.GetField("fields", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var getter = typeof(ReadRound).GetProperty("Recoil")!.GetMethod!;
        var field = typeof(ReadTemplate).GetField("Recoil")!;
        getterMap[getter] = 1; fieldMap[field] = 1;
        try
        {
            var source = new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Callvirt, getter), new CodeInstruction(OpCodes.Ret) };
            var ammo = new ReadRound { CurrentAddress = new(new Slot(gun)) };
            var read = Emit(source, typeof(ReadRound));
            Assert.Equal(0, read(ammo));
            ammo.CurrentAddress = null;
            Assert.Equal(12, read(ammo));
            var raw = new ReadTemplate(); ammo.Template = raw;
            var templateRead = Emit(new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldfld, field), new CodeInstruction(OpCodes.Ret) }, typeof(ReadTemplate));
            Assert.Equal(12, templateRead(raw));
            var state = Begin(new(gun, new("pmc")), ammo);
            try { Assert.Equal(0, templateRead(raw)); Assert.Equal(12, templateRead(new ReadTemplate())); }
            finally { Call("EndShot", null, state); }
            Assert.Equal(12, raw.Recoil);
        }
        finally { getterMap.Remove(getter); fieldMap.Remove(field); }
    }

    public sealed class ReadRound() : Item("read") { public int Recoil => 12; }
    public sealed class ReadTemplate { public int Recoil = 12; }
    [Fact]
    public void Instance_template_reads_and_actual_accuracy_factor_keep_the_ammo_owner()
    {
        var fieldMap = (Dictionary<FieldInfo, int>)Patches.GetField("fields", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var templateType = typeof(EFT.InventoryLogic.AmmoTemplate);
        var ammoType = typeof(EFT.InventoryLogic.Ammo);
        var recoil = templateType.GetField("ammoRec")!;
        var factor = templateType.GetProperty("AmmoFactor")!.GetMethod!;
        var oldFactor = Patches.GetField("templateFactor", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        fieldMap[recoil] = 1; Set("templateFactor", factor);
        try
        {
            var raw = new EFT.InventoryLogic.AmmoTemplate();
            var ammo = new EFT.InventoryLogic.Ammo { CurrentAddress = new(new Slot(gun)), Template = raw };
            var loadTemplate = new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldfld, typeof(Item).GetField("Template")),
                new CodeInstruction(OpCodes.Castclass, templateType) };
            var recoilSource = loadTemplate.Concat(new[] { new CodeInstruction(OpCodes.Ldfld, recoil), new CodeInstruction(OpCodes.Ret) });
            var readRecoil = Emit(recoilSource, ammoType, ammoType.GetProperty("ammoRec")!.GetMethod);
            var factorSource = loadTemplate.Concat(new[] { new CodeInstruction(OpCodes.Callvirt, factor), new CodeInstruction(OpCodes.Ret) });
            var readFactor = Emit<float>(factorSource, ammoType, ammoType.GetProperty("AmmoFactor")!.GetMethod);
            Assert.Equal(0, readRecoil(ammo)); Assert.Equal(1f, readFactor(ammo));
            raw.ammoAccr = 25; Assert.Equal(0.8f, readFactor(ammo));
            ammo.CurrentAddress = null;
            Assert.Equal(12, readRecoil(ammo));
            raw.ammoAccr = -20; Assert.Equal(1.2f, readFactor(ammo));
            Assert.Equal(12, raw.ammoRec); Assert.Equal(-20, raw.ammoAccr);
        }
        finally { fieldMap.Remove(recoil); Set("templateFactor", oldFactor); }
    }

    [Fact]
    public void Compiled_reference_reader_supports_fields_properties_and_zero_argument_methods()
    {
        var target = new ReferenceMembers();
        foreach (var name in new[] { "Field", "Property", "Method" })
        {
            var read = (Func<object, object>)Call("Reader", typeof(ReferenceMembers), name)!;
            Assert.Same(target.Field, read(target));
        }
    }
    public sealed class ReferenceMembers
    {
        public readonly object Field = new();
        public object Property => Field;
        public object Method() => Field;
    }

    private static Func<object, int> Emit(IEnumerable<CodeInstruction> source, Type type, MethodBase? original = null) => Emit<int>(source, type, original);
    private static Func<object, T> Emit<T>(IEnumerable<CodeInstruction> source, Type type, MethodBase? original = null)
    {
        var generated = new DynamicMethod("AmmoFixture", typeof(T), new[] { typeof(object) }, Patches, true);
        var il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, type); il.Emit(OpCodes.Starg_S, (byte)0);
        original ??= typeof(AmmoHooksTests).GetMethod(nameof(Int), BindingFlags.Static | BindingFlags.NonPublic)!;
        var rewritten = (IEnumerable<CodeInstruction>)Call("Reads", source, original)!;
        foreach (var instruction in rewritten)
        {
            if (instruction.operand is MethodInfo method) il.Emit(instruction.opcode, method);
            else if (instruction.operand is FieldInfo field) il.Emit(instruction.opcode, field);
            else if (instruction.operand is int number) il.Emit(instruction.opcode, number);
            else if (instruction.operand is Type cast) il.Emit(instruction.opcode, cast);
            else il.Emit(instruction.opcode);
        }
        return (Func<object, T>)generated.CreateDelegate(typeof(Func<object, T>));
    }

    [Fact]
    public void Warm_reads_allocate_nothing_and_do_not_walk_parent_reflection()
    {
        var ammo = In(In(gun), true);
        var read = (Func<object, int, int, int>)Patches.GetMethod("ReadInt", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Func<object, int, int, int>));
        for (var i = 0; i < 100; i++) read(ammo, 10, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0;
        for (var i = 0; i < 1000; i++) sum += read(ammo, 10, 1);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, sum); Assert.Equal(0, allocated);
    }

    [Fact]
    public void Main_player_world_getter_is_bound_once_and_warm_reads_allocate_nothing()
    {
        CurrentWorld = new(new("pmc") { Inventory = new(equipment) });
        MythicField("worldInstance", typeof(AmmoHooksTests).GetProperty(nameof(CurrentWorld)));
        var ammo = In(gun);
        var read = (Func<object, int, int, int>)Patches.GetMethod("ReadInt", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Func<object, int, int, int>));
        for (var i = 0; i < 100; i++) read(ammo, 12, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0;
        for (var i = 0; i < 1000; i++) sum += read(ammo, 12, 1);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, sum); Assert.Equal(0, allocated);
        CurrentWorld = new(new("bot") { Inventory = new(new object()) });
        Assert.Equal(12, read(ammo, 12, 1));
    }

    [Fact]
    public void Each_shot_can_use_a_different_detached_round_without_allocating_a_round_cache()
    {
        var begin = Patches.GetMethod("BeginShot", BindingFlags.Static | BindingFlags.NonPublic)!;
        var end = Patches.GetMethod("EndShot", BindingFlags.Static | BindingFlags.NonPublic)!;
        var read = Patches.GetMethod("ReadInt", BindingFlags.Static | BindingFlags.NonPublic)!;
        var generated = new DynamicMethod("AmmoShotFixture", typeof(int), new[] { typeof(object), typeof(object) }, Patches, true);
        var il = generated.GetILGenerator();
        var state = il.DeclareLocal(Patches.GetNestedType("ShotState", BindingFlags.NonPublic)!);
        var result = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloca, state); il.Emit(OpCodes.Call, begin);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, 12); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Call, read); il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldloc, state); il.Emit(OpCodes.Call, end); il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ret);
        var shoot = (Func<object, object, int>)generated.CreateDelegate(typeof(Func<object, object, int>));
        var controller = new Controller(gun, new("pmc"));
        var ammo = Enumerable.Range(0, 1000).Select(i => new Item(i.ToString())).ToArray();
        for (var i = 0; i < 100; i++) shoot(controller, ammo[0]);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0;
        foreach (var round in ammo) sum += shoot(controller, round);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, sum); Assert.Equal(0, allocated);
    }
}
