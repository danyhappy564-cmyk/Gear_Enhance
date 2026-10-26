using System.Reflection;
using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

// 1.4.0 mythic hooks run on stand-ins with the members the game has (checked against the real
// assembly in tests/GameApiTests.cs): only the player's own mythic gear changes anything.
public sealed class MythicHooksTests : IDisposable
{
    public sealed class FakeShot { public float Damage = 50; public float PenetrationPower = 30; }
    public sealed class FakeOwner { public object? RootItem { get; set; } }
    public sealed class FakeArmor { public int ArmorClass; public long ArmorPlateCollidersMask { get; set; } public FakeGear? Item; }
    public sealed class FakeSlot { public object? ContainedItem; }
    public sealed class FakeHolder(object parent) : EFT.InventoryLogic.Slot { public object ParentItem { get; } = parent; }
    public sealed class FakeGear(string id)
    {
        public string Id { get; } = id;
        public List<object> Components { get; } = new();
        public FakeOwner? Owner { get; set; }
        public List<FakeSlot> AllSlots { get; } = new();
        public TestAddress? CurrentAddress { get; set; }
    }
    public sealed class FakeEquipment
    {
        public readonly Dictionary<string, object> Worn = new();
        public FakeSlot GetSlot(object slot) => new() { ContainedItem = Worn.GetValueOrDefault(slot.ToString()!) };
    }
    public sealed class FakeInventory { public FakeEquipment? Equipment; }
    public sealed class FakePlayer { public bool IsYourPlayer { get; set; } public FakeInventory? Inventory; }
    public sealed class FakeHealth { public FakePlayer? Player; }
    public sealed class FakeDamage { public int DamageType; }

    private readonly FakeEquipment equipment = new();

    public MythicHooksTests()
    {
        StatPatches.Registry.Clear(); StatPatches.Registry.Configure(new());
        var cache = (System.Collections.Concurrent.ConcurrentDictionary<string, Type>)typeof(GameBridge)
            .GetField("runtimeTypes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        cache["EFT.InventoryLogic.Slot"] = typeof(EFT.InventoryLogic.Slot);
        Set("shotDamage", typeof(FakeShot).GetField("Damage"));
        Set("shotPenetration", typeof(FakeShot).GetField("PenetrationPower"));
        Set("armorType", typeof(FakeArmor));
        Set("plateMask", typeof(FakeArmor).GetProperty("ArmorPlateCollidersMask"));
        Set("ownerRoot", typeof(FakeOwner).GetProperty("RootItem"));
        Set("getSlot", typeof(FakeEquipment).GetMethod("GetSlot"));
        Set("wornSlots", new object[] { "ArmorVest", "TacticalVest", "Headwear", "FaceCover", "Eyewear" });
        // No GameWorld in the test host: the cached main player stands in for it.
        Set("world", null); Set("worldInstance", null);
        Set("mainProfile", "pmc"); Set("mainEquipment", equipment);
        typeof(MythicPatches).GetField("carriers", BindingFlags.Static | BindingFlags.NonPublic)!.FieldType.GetMethod("Clear")!
            .Invoke(typeof(MythicPatches).GetField("carriers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null), null);
    }
    public void Dispose() { StatPatches.Registry.Clear(); Set("mainProfile", null); Set("mainEquipment", null); }

    private static void Set(string field, object? value) =>
        typeof(MythicPatches).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
    private static object?[] Call(string method, params object?[] args)
    {
        typeof(MythicPatches).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        return args;
    }
    private static void Mythic(string id, int level, params string[] zones) => StatPatches.Registry.Replace(new EnhanceSnapshot
    {
        ProfileId = "pmc",
        Items = new[] { new EnhanceRecord { ItemId = id, Path = EnhancePath.Recoil, Level = 10, Evolved = true, StarLevel = 3 } },
        Mythic = new[] { new MythicLink { ItemId = id, Level = level, Zones = zones } }
    }, "pmc");

    [Fact]
    public void Shots_from_the_players_mythic_gun_hit_harder_and_others_do_not()
    {
        Mythic("gun", 3);
        var shot = new FakeShot();
        Call("Bullet", shot, "pmc", new TestItem("gun"));
        Assert.Equal(54.5f, shot.Damage, 3); Assert.Equal(32.7f, shot.PenetrationPower, 3);   // 3 % a level
        // A bot firing the same gun, another gun, and a mythic gun still at level 0.
        var bot = new FakeShot();
        Call("Bullet", bot, "bot", new TestItem("gun"));
        Call("Bullet", bot, "pmc", new TestItem("other"));
        Assert.Equal((50f, 30f), (bot.Damage, bot.PenetrationPower));
        Mythic("gun", 0);
        Call("Bullet", bot, "pmc", new TestItem("gun"));
        Assert.Equal(50f, bot.Damage);
    }

    [Fact]
    public void Extra_zones_are_carried_by_the_best_soft_part_of_the_players_own_armor()
    {
        Mythic("vest", 1, "LeftThigh", "Pelvis");
        var vest = new FakeGear("vest") { Owner = new FakeOwner { RootItem = equipment } };
        var soft = new FakeGear("soft") { Owner = vest.Owner, CurrentAddress = new(new FakeHolder(vest)) };
        var plate = new FakeGear("plate") { Owner = vest.Owner, CurrentAddress = new(new FakeHolder(vest)) };
        var softArmor = new FakeArmor { ArmorClass = 4, Item = soft };
        var plateArmor = new FakeArmor { ArmorClass = 6, ArmorPlateCollidersMask = 2, Item = plate };
        soft.Components.Add(softArmor); plate.Components.Add(plateArmor);
        vest.AllSlots.Add(new FakeSlot { ContainedItem = soft }); vest.AllSlots.Add(new FakeSlot { ContainedItem = plate });
        bool Matches(FakeArmor armor, string zone, long plateCollider = 0)
        {
            var args = Call("Zone", armor, new object[] { zone, plateCollider }, false);
            return (bool)args[2]!;
        }
        Assert.True(Matches(softArmor, "LeftThigh"));
        Assert.False(Matches(softArmor, "RightThigh"));
        // The plate is not the carrier, and plate collider checks are left alone.
        Assert.False(Matches(plateArmor, "LeftThigh"));
        Assert.False(Matches(softArmor, "LeftThigh", 4));
        // A bot wearing it (another inventory root) gets nothing.
        vest.Owner.RootItem = new object();
        Typeof_carriers_clear();
        Assert.False(Matches(softArmor, "LeftThigh"));
    }

    private static void Typeof_carriers_clear()
    {
        var field = typeof(MythicPatches).GetField("carriers", BindingFlags.Static | BindingFlags.NonPublic)!;
        field.FieldType.GetMethod("Clear")!.Invoke(field.GetValue(null), null);
    }

    [Fact]
    public void Hits_on_the_player_shrink_by_the_best_worn_mythic_level_and_other_damage_stays()
    {
        Mythic("helmet", 4);
        equipment.Worn["Headwear"] = new TestItem("helmet");
        var health = new FakeHealth { Player = new FakePlayer { IsYourPlayer = true, Inventory = new FakeInventory { Equipment = equipment } } };
        float Hit(int type, float damage = 100, FakeHealth? target = null, object? part = null)
        {
            var args = Call("Taken", target ?? health, new object[] { part ?? FakePart.Chest, damage, new FakeDamage { DamageType = type } }, damage);
            return (float)args[2]!;
        }
        // Helmet (head gear): 5% a level on every hit, -20% at level 4.
        Assert.Equal(80f, Hit(512), 3);   // bullet
        Assert.Equal(80f, Hit(4), 3);     // explosion
        Assert.Equal(80f, Hit(512, 100, null, FakePart.Head), 3);
        // Body armor: 10% a level, half on head hits; the best worn piece counts.
        Mythic("vest", 4);
        equipment.Worn["ArmorVest"] = new TestItem("vest");
        Assert.Equal(60f, Hit(512), 3);
        Assert.Equal(80f, Hit(512, 100, null, FakePart.Head), 3);
        equipment.Worn.Remove("ArmorVest");
        Assert.Equal(100f, Hit(2));       // fall
        Assert.Equal(100f, Hit(16384));   // light bleeding
        var bot = new FakeHealth { Player = new FakePlayer { IsYourPlayer = false, Inventory = new FakeInventory { Equipment = equipment } } };
        Assert.Equal(100f, Hit(512, 100, bot));
        equipment.Worn.Clear();
        Assert.Equal(100f, Hit(512));
    }

    // Stands in for EFT.EBodyPart (the hook reads its name).
    private enum FakePart { Head, Chest }
}
