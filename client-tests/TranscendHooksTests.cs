using System.Reflection;
using System.Reflection.Emit;
using GearEnhance;
using HarmonyLib;
using Xunit;

namespace EFT.InventoryLogic
{
    // Test fixture only: the trader price transpiler matches fields declared on this name.
    public class RepairableComponent(GearEnhanceClientTests.TestItem item, float max, float durability)
    {
        public GearEnhanceClientTests.TestItem Item { get; } = item;
        public float MaxDurability = max;
        public float Durability = durability;
    }

    // Like the game: the class is an explicit interface property over a lowercase field.
    public interface IArmorComponentTemplate { int ArmorClass { get; } float BluntThroughput { get; } }
    public sealed class TestPlateTemplate(int armorClass, float blunt) : IArmorComponentTemplate
    {
        public int armorClass = armorClass;
        public float BluntThroughput = blunt;
        int IArmorComponentTemplate.ArmorClass => armorClass;
        float IArmorComponentTemplate.BluntThroughput => BluntThroughput;
    }
    // Like the game: one-line getters that the shot and hit code reads (inlined in game).
    public sealed class Weapon(string id, int rate, params GearEnhanceClientTests.TestFireMode[] modes)
    {
        public string Id { get; } = id;
        public GearEnhanceClientTests.TestFireMode[] WeapFireType { get; } = modes;
        public int bFirerate = rate;
        public int FireRate => bFirerate;
        public sealed class AutomaticFireOperation(Weapon weapon)
        {
            public float ShotsTime = 60f / weapon.FireRate;
            public int Unrelated() => weapon.bFirerate;
        }
    }
    public sealed class ArmorComponent(GearEnhanceClientTests.TestItem item, int armorClass)
    {
        public GearEnhanceClientTests.TestItem Item { get; } = item;
        public int ArmorClass => armorClass;
    }
    public sealed class TestPlateComponent(GearEnhanceClientTests.TestItem item, int armorClass, float blunt)
    {
        public GearEnhanceClientTests.TestItem Item { get; } = item;
        public readonly IArmorComponentTemplate Template = new TestPlateTemplate(armorClass, blunt);
    }
}

namespace GearEnhanceClientTests
{
    public enum TestFireMode { fullauto, single, burst }
    public sealed class TestGun(string id, params TestFireMode[] modes)
    {
        public string Id { get; } = id;
        public TestFireMode[] WeapFireType { get; } = modes;
    }
    public sealed record TestHands(TestGun Item);
    public sealed class TestArmorTemplate { public int ArmorClass; public float BluntThroughput; }
    public sealed class TestArmorComponent(TestItem item, int armorClass, float blunt)
    {
        public TestItem Item { get; } = item;
        public readonly TestArmorTemplate Template = new() { ArmorClass = armorClass, BluntThroughput = blunt };
    }
    public sealed class TestAttribute
    {
        public Func<float> Base = () => 0;
        public Func<float> Delta = () => 0;
        public Func<string> StringValue = () => "";
    }

    // 1.3.0 transcendence hooks on stand-in objects (the real game types are checked by
    // the server-side GameApiTests against Assembly-CSharp; nothing here runs Unity).
    public sealed class TranscendHooksTests : IDisposable
    {
        public TranscendHooksTests()
        {
            StatPatches.Registry.Clear(); StatPatches.Registry.Configure(new()); StatPatches.Registry.ConfigureTranscend(new());
            var cache = (System.Collections.Concurrent.ConcurrentDictionary<string, Type>)typeof(GameBridge)
                .GetField("runtimeTypes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            cache["EFT.InventoryLogic.Slot"] = typeof(EFT.InventoryLogic.Slot);
            cache["EFT.InventoryLogic.IArmorComponentTemplate"] = typeof(EFT.InventoryLogic.IArmorComponentTemplate);
        }
        public void Dispose() => StatPatches.Registry.Clear();

        private static void Load(TranscendLink[] transcend, EnhanceRecord[]? items = null, InsertLink[]? inserts = null, DurabilityLink[]? grants = null) =>
            StatPatches.Registry.Replace(new EnhanceSnapshot { ProfileId = "pmc", Items = items ?? [], Inserts = inserts ?? [],
                Transcend = transcend, Durability = grants ?? [] }, "pmc");
        private static object?[] Call(string name, params object?[] args)
        {
            typeof(TranscendPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
            return args;
        }

        [Fact]
        public void Nothing_changes_without_transcended_gear()
        {
            Assert.False(StatPatches.Registry.HasTranscend);
            Assert.Equal(600, Call("FireRate", new TestGun("gun", TestFireMode.fullauto), 600)[1]);
            Assert.Equal(1f, Call("Overheat", new TestGun("gun", TestFireMode.single), 1f)[1]);
            Assert.Equal(4, Call("ArmorClass", new TestArmor(new("vest")), 4)[1]);
            Assert.Equal(0.3f, TranscendPatches.Blunt(new TestArmorComponent(new("vest"), 4, 0.3f), 0.3f));
            Assert.Equal(40f, TranscendPatches.PlainMax(new EFT.InventoryLogic.RepairableComponent(new("plate"), 40, 40), 40));
        }

        [Fact]
        public void Automatic_guns_fire_faster_and_semi_auto_guns_jam_and_heat_less()
        {
            Load([new TranscendLink { ItemId = "auto", Third = 5 }, new TranscendLink { ItemId = "semi", Third = 5 }]);
            // +min(50 × 5, 600 × 30%) = +180
            Assert.Equal(780, Call("FireRate", new TestGun("auto", TestFireMode.single, TestFireMode.fullauto), 600)[1]);
            Assert.Equal(1000 + 250, Call("FireRate", new TestGun("auto", TestFireMode.burst), 1000)[1]);
            Assert.Equal(1f, Call("Overheat", new TestGun("auto", TestFireMode.fullauto), 1f)[1]);

            var semi = new TestGun("semi", TestFireMode.single);
            Assert.Equal(300, Call("FireRate", semi, 300)[1]);
            Assert.Equal(0.6f, (float)Call("Overheat", semi, 1f)[1]!, 5);
            var args = Call("Malfunction", new TestHands(semi), 0.2f, 0.1, 0.02f, 0.04f, 0.06f);
            Assert.Equal(0.1f, (float)args[1]!, 5);
            Assert.Equal(0.05, (double)args[2]!, 6);
            Assert.Equal(0.01f, (float)args[3]!, 5);
            Assert.Equal(0.03f, (float)args[5]!, 5);
        }

        [Fact]
        public void A_receipt_updates_the_cache_and_never_touches_enhancement_levels()
        {
            Load([new TranscendLink { ItemId = "auto", Third = 1 }],
                [new EnhanceRecord { ItemId = "auto", Path = EnhancePath.Recoil, Level = 10, Evolved = true, StarLevel = 3 }]);
            var gun = new TestGun("auto", TestFireMode.fullauto);
            Assert.Equal(636, Call("FireRate", gun, 600)[1]);
            StatPatches.Registry.ApplyReceipt(new EnhanceReceipt { ItemId = "auto", Action = EnhanceAction.Transcend,
                Transcend = TranscendTrack.Third, Level = 2, Path = EnhancePath.Recoil, Success = true }, "pmc");
            Assert.Equal(672, Call("FireRate", gun, 600)[1]);
            Assert.Equal(10, StatPatches.Registry.Find("auto", EnhancePath.Recoil)!.Level);
            StatPatches.Registry.ApplyReceipt(new EnhanceReceipt { ItemId = "auto", Action = EnhanceAction.Insure,
                Path = EnhancePath.Recoil, Level = 1, Success = true }, "pmc");
            Assert.Equal(10, StatPatches.Registry.Find("auto", EnhancePath.Recoil)!.Level);
            // A drop to 0 on both tracks leaves nothing transcended.
            StatPatches.Registry.ApplyReceipt(new EnhanceReceipt { ItemId = "auto", Action = EnhanceAction.Transcend,
                Transcend = TranscendTrack.Third, Level = 0, FailureMode = FailureMode.Downgrade }, "pmc");
            Assert.False(StatPatches.Registry.HasTranscend);
            Assert.Equal(600, Call("FireRate", gun, 600)[1]);
        }

        [Fact]
        public void Armor_class_and_blunt_follow_the_armor_into_its_registered_plates_only()
        {
            Load([new TranscendLink { ItemId = "vest", Third = 5 }], inserts: [new InsertLink { ArmorId = "vest", PartIds = ["plate"] }]);
            var vest = new TestItem("vest");
            var plate = new TestItem("plate") { CurrentAddress = new(new TestContainer(vest)) };
            var stranger = new TestItem("other") { CurrentAddress = new(new TestContainer(vest)) };
            Assert.Equal(6, Call("ArmorClass", new TestArmor(plate), 4)[1]);
            Assert.Equal(6, Call("ArmorClass", new TestArmor(plate), 5)[1]);
            Assert.Equal(4, Call("ArmorClass", new TestArmor(stranger), 4)[1]);
            Assert.Equal(4, Call("ArmorClass", new TestArmor(new("plate")), 4)[1]);
            // Class 4 plate: -25%. Class 6 plate takes neither class: -25% - 20% = -45%.
            Assert.Equal(0.225f, TranscendPatches.Blunt(new TestArmorComponent(plate, 4, 0.3f), 0.3f), 5);
            Assert.Equal(0.165f, TranscendPatches.Blunt(new TestArmorComponent(plate, 6, 0.3f), 0.3f), 5);
            Assert.Equal(0.3f, TranscendPatches.Blunt(new TestArmorComponent(stranger, 4, 0.3f), 0.3f));
            // Game-shaped template: the class-6 substitute still applies.
            Assert.Equal(6, TranscendPatches.TemplateClass(new EFT.InventoryLogic.TestPlateTemplate(6, 0.3f)));
            Assert.Equal(0.165f, TranscendPatches.Blunt(new EFT.InventoryLogic.TestPlateComponent(plate, 6, 0.3f), 0.3f), 5);
        }

        [Fact]
        public void Blunt_transpiler_wraps_only_the_template_read_and_keeps_the_template()
        {
            Load([new TranscendLink { ItemId = "vest", Third = 1 }]);
            var component = new TestArmorComponent(new("vest"), 4, 0.4f);
            var source = new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, typeof(TestArmorComponent).GetField("Template")),
                new CodeInstruction(OpCodes.Ldfld, typeof(TestArmorTemplate).GetField("BluntThroughput")),
                new CodeInstruction(OpCodes.Ret)
            };
            Assert.Equal(0.38f, (float)Emit(source, "BluntRead", component)!, 5);
            Assert.Equal(0.4f, component.Template.BluntThroughput);
            Assert.ThrowsAny<Exception>(() => Patched("BluntRead", new[] { new CodeInstruction(OpCodes.Ret) }).ToList());
        }

        [Fact]
        public void Trader_price_reads_see_the_durability_without_the_grant()
        {
            Load([new TranscendLink { ItemId = "vest", Second = 5 }], grants: [new DurabilityLink { ItemId = "plate", Amount = 12 }]);
            var granted = new EFT.InventoryLogic.RepairableComponent(new("plate"), 52, 26);
            Assert.Equal(40f, TranscendPatches.PlainMax(granted, 52));
            Assert.Equal(20f, TranscendPatches.PlainDurability(granted, 26), 4);
            var plain = new EFT.InventoryLogic.RepairableComponent(new("other"), 40, 30);
            Assert.Equal(40f, TranscendPatches.PlainMax(plain, 40));
            Assert.Equal(30f, TranscendPatches.PlainDurability(plain, 30));
            var source = new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, typeof(EFT.InventoryLogic.RepairableComponent).GetField("MaxDurability")),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, typeof(EFT.InventoryLogic.RepairableComponent).GetField("Durability")),
                new CodeInstruction(OpCodes.Sub),
                new CodeInstruction(OpCodes.Ret)
            };
            // (52 − 12) − 26 × 40/52 = 40 − 20
            Assert.Equal(20f, (float)Emit(source, "PlainDurabilityReads", granted)!, 4);
            Assert.Equal(52f, granted.MaxDurability);
        }

        [Fact]
        public void Horizontal_recoil_shares_the_80_percent_limit_with_the_recoil_track()
        {
            Assert.Equal(30, TranscendEffects.HorizontalWithCap(45, 30), 6);
            Assert.Equal(0, TranscendEffects.HorizontalWithCap(80, 30), 6);
            Assert.Equal(1 - 0.2 / 0.3, TranscendEffects.HorizontalWithCap(70, 50) / 100, 6);
            Assert.Equal((3.5f, 6.5f), TranscendEffects.Narrow(2, 8, 50));
            Load([new TranscendLink { ItemId = "gun", Second = 5 }],
                [new EnhanceRecord { ItemId = "gun", Path = EnhancePath.Recoil, Level = 10, Evolved = true, StarLevel = 3 }]);
            // Recoil track at ★★★ max = 45%; 30% more stays within 80% (45% → 61.5% total).
            Assert.Equal(30, TranscendPatches.Horizontal(new TestItem("gun")), 6);
        }

        [Fact]
        public void Inspection_values_match_the_effects()
        {
            Load([new TranscendLink { ItemId = "gun", Second = 5, Third = 1 }]);
            var gun = new TestGun("gun", TestFireMode.fullauto);
            var back = new TestAttribute { Base = () => 200, Delta = () => -0.1f, StringValue = () => "180" };
            TranscendPatches.WrapRecoilBack(gun, back);
            // 200 × 0.9 × 0.7 = 126
            Assert.Equal("126", back.StringValue());
            Assert.Equal(-0.37f, back.Delta(), 5);
            var rate = new TestAttribute { Base = () => 600, StringValue = () => "600 rpm" };
            TranscendPatches.WrapFireRate(gun, rate);
            Assert.Equal(636, rate.Base());
            Assert.Equal("636 rpm", rate.StringValue());

            var other = new TestGun("other", TestFireMode.fullauto);
            var plain = new TestAttribute { Base = () => 600, StringValue = () => "600 rpm" };
            TranscendPatches.WrapFireRate(other, plain);
            Assert.Equal("600 rpm", plain.StringValue());
        }

        // Weapon implements IWeapon.Item explicitly, so ShotEffector._weapon is the item itself.
        public sealed class TestShot(object weapon) { public object _weapon = weapon; }
        public sealed class TestWrapper(TestGun item) { public TestGun Item { get; } = item; }

        [Fact]
        public void Recoil_hooks_find_the_weapon_behind_the_shot_effector()
        {
            var gun = new TestGun("gun", TestFireMode.single);
            Assert.Same(gun, TranscendPatches.ShotWeapon(new TestShot(gun)));
            Assert.Same(gun, TranscendPatches.ShotWeapon(new TestShot(new TestWrapper(gun))));
            Assert.Null(TranscendPatches.ShotWeapon(new TestShot(null!)));
        }

        [Fact]
        public void F12_chance_text_parses_five_numbers_or_falls_back()
        {
            Assert.Equal([60, 50, 40, 30, 20], TranscendEffects.ParseChances("60, 50, 40, 30, 20")!);
            Assert.Equal([1.5, 2, 3, 4, 5], TranscendEffects.ParseChances("1.5/2/3/4/5")!);
            Assert.Null(TranscendEffects.ParseChances("60, 50, 40"));
            Assert.Null(TranscendEffects.ParseChances("60, 50, 40, 30, 120"));
            Assert.Null(TranscendEffects.ParseChances(null));
        }

        [Fact]
        public void Inlined_getters_are_rewritten_in_their_callers()
        {
            Load([new TranscendLink { ItemId = "auto", Third = 5 }, new TranscendLink { ItemId = "vest", Third = 5 }]);
            var getter = typeof(EFT.InventoryLogic.Weapon).GetProperty("FireRate")!.GetMethod!;
            var types = TranscendPatches.Nested(typeof(EFT.InventoryLogic.Weapon)).ToList();
            Assert.Contains(typeof(EFT.InventoryLogic.Weapon.AutomaticFireOperation), types);
            var callers = TranscendPatches.Callers(types, getter);
            Assert.Contains(callers, m => m.IsConstructor && m.DeclaringType == typeof(EFT.InventoryLogic.Weapon.AutomaticFireOperation));
            Assert.DoesNotContain(callers, m => m.Name == "Unrelated" || m.Name == "get_FireRate");

            // `weapon.FireRate` → ShotFireRate(weapon, weapon.FireRate): +min(50 × 5, 600 × 30%) = +180.
            var gun = new EFT.InventoryLogic.Weapon("auto", 600, TestFireMode.fullauto);
            var rate = new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Callvirt, getter), new CodeInstruction(OpCodes.Ret) };
            Assert.Equal(780, EmitInt(rate, "FireRateReads", gun));
            Assert.Equal(300, EmitInt(rate, "FireRateReads", new EFT.InventoryLogic.Weapon("semi", 300, TestFireMode.single)));
            Assert.Equal(600, gun.bFirerate);
            var plate = new EFT.InventoryLogic.ArmorComponent(new("vest"), 4);
            var armorClass = new[] { new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, typeof(EFT.InventoryLogic.ArmorComponent).GetProperty("ArmorClass")!.GetMethod!), new CodeInstruction(OpCodes.Ret) };
            Assert.Equal(6, EmitInt(armorClass, "ArmorClassReads", plate));
            // Other getters with the same name (the template's class) are left alone.
            var template = typeof(EFT.InventoryLogic.IArmorComponentTemplate).GetProperty("ArmorClass")!.GetMethod!;
            Assert.Equal(3, Patched("ArmorClassReads", new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Callvirt, template),
                new CodeInstruction(OpCodes.Ret) }).Count());
            Assert.False(TranscendPatches.CallsToken(new byte[] { 0x28, 1, 2, 3 }, 0x03020100));
            Assert.True(TranscendPatches.CallsToken(new byte[] { 0x00, 0x6F, 0, 1, 2, 3, 0x2A }, 0x03020100));
        }

        private static int EmitInt(IEnumerable<CodeInstruction> source, string transpiler, object owner)
        {
            var generated = new DynamicMethod("TranscendFixture", typeof(int), new[] { owner.GetType() }, typeof(TranscendPatches), true);
            var il = generated.GetILGenerator();
            foreach (var instruction in Patched(transpiler, source))
            {
                if (instruction.operand is MethodInfo method) il.Emit(instruction.opcode, method);
                else il.Emit(instruction.opcode);
            }
            return (int)generated.Invoke(null, new[] { owner })!;
        }

        private static IEnumerable<CodeInstruction> Patched(string transpiler, IEnumerable<CodeInstruction> source) =>
            (IEnumerable<CodeInstruction>)typeof(TranscendPatches).GetMethod(transpiler, BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { source })!;

        private static object? Emit(IEnumerable<CodeInstruction> source, string transpiler, object owner)
        {
            var generated = new DynamicMethod("TranscendFixture", typeof(float), new[] { owner.GetType() }, typeof(TranscendPatches), true);
            var il = generated.GetILGenerator();
            foreach (var instruction in Patched(transpiler, source))
            {
                if (instruction.operand is MethodInfo method) il.Emit(instruction.opcode, method);
                else if (instruction.operand is FieldInfo field) il.Emit(instruction.opcode, field);
                else il.Emit(instruction.opcode);
            }
            return generated.Invoke(null, new[] { owner });
        }
    }
}
