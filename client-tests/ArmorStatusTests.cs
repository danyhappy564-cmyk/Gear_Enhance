using System.Reflection;
using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

public sealed class ArmorStatusTests : IDisposable
{
    public ArmorStatusTests() { Loc.English = false; }
    public void Dispose() => Loc.English = false;

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Missing_armor_values_never_show_negative_classes_or_percent_units(float missing)
    {
        var raw = new ArmorStats { ArmorClass = int.MinValue, Zones = -1, TemplateMax = missing,
            Speed = missing, Turn = missing, Ergonomics = missing };
        var now = new GearLevels { Mythic = -1 };
        var next = now; next.Third = 3; next.Second = 1; next.Mobility = 10;
        var rows = StatSheet.Armor(raw, now, next, new(), new());
        foreach (var index in new[] { 0, 1, 2, 6, 7, 8 })
        {
            Assert.Equal("—", rows[index].Base);
            Assert.Equal("—", rows[index].Now);
            Assert.Null(rows[index].Next);
        }
    }

    private static ArmorStats Read(object item)
    {
        var reader = typeof(StatSheet).Assembly.GetType("GearEnhance.ArmorStatus");
        Assert.NotNull(reader);
        return (ArmorStats)reader.GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { item })!;
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void Invalid_game_class_is_rejected_before_integer_conversion(float invalid)
    {
        var raw = Read(new Gear { Components = new object[] { new InvalidArmorComponent { ArmorClass = invalid } } });
        Assert.Equal(-1, raw.ArmorClass);
        var rows = StatSheet.Armor(raw, new GearLevels { Third = 3, Mythic = -1 }, null, new(), new());
        Assert.Equal("—", rows[0].Now);
    }

    // 1.4.2 (user 2026-10-28): armor with no gear penalty turns its Mobility track into impact
    // absorption, the same amount as blunt damage reduction.
    [Fact]
    public void Penalty_free_armor_shows_mobility_as_blunt_reduction()
    {
        var free = new ArmorStats { ArmorClass = 4, Speed = 0, Turn = 0, Ergonomics = 0 };
        var heavy = new ArmorStats { ArmorClass = 4, Speed = -3, Turn = 0, Ergonomics = 0 };
        Assert.True(StatSheet.NoPenalty(free));
        Assert.True(StatSheet.NoPenalty(new ArmorStats()));
        Assert.False(StatSheet.NoPenalty(heavy));
        var now = new GearLevels { Mythic = -1, Mobility = 10 };
        Assert.Equal("90%", StatSheet.Armor(free, now, null, new(), new())[4].Now);
        Assert.Equal("100%", StatSheet.Armor(heavy, now, null, new(), new())[4].Now);
        try
        {
            EnhanceEffects.ImpactMobility = true;
            Assert.Equal("충격 흡수", EnhanceEffects.Name(EnhancePath.Mobility));
            Assert.Equal("둔탁상 감소", EnhanceEffects.StatName(EnhancePath.Mobility));
            // The F12 cfg key never changes.
            Assert.Equal("장비 페널티 완화", EnhanceEffects.KoreanStatName(EnhancePath.Mobility));
        }
        finally { EnhanceEffects.ImpactMobility = false; }
        Assert.Equal("기동성", EnhanceEffects.Name(EnhancePath.Mobility));
    }

    [Fact]
    public void Modular_armor_reads_derived_components_locked_soft_inserts_and_unique_plate_zones_once()
    {
        var soft = Part(4, 74.4f, 80, new[] { "RibcageUp", "Neck" });
        var front = Part(6, 150, 160, Array.Empty<string>(), new[] { "PlateFront" });
        var back = Part(6, 150, 160, Array.Empty<string>(), new[] { "PlateBack", "PlateFront" });
        var root = new Gear
        {
            Components = new object[] { new EquipmentPenaltyComponent(new()) },
            Slots = new[] { new GearSlot(soft, true), new GearSlot(front), new GearSlot(back), new GearSlot(front) }
        };
        // AllSlots already includes descendants. Following it recursively would sum these twice.
        soft.Slots = new[] { new GearSlot(front) };
        var rows = StatSheet.Armor(Read(root), new GearLevels { Mythic = -1 }, null, new(), new());
        Assert.Equal("4 · 판 6", rows[0].Now);
        Assert.Equal("4곳", rows[1].Now);
        Assert.Equal("374.4", rows[2].Now);
        Assert.Equal("0%", rows[6].Now);
        Assert.Equal("0%", rows[7].Now);
        Assert.Equal("0", rows[8].Now);
    }

    [Fact]
    public void Helmet_body_addons_and_penalties_keep_their_own_contributions()
    {
        var root = Part(4, 35, 40, new[] { "Head" });
        var visor = Part(3, 20, 25, new[] { "Face", "Head" });
        ((ArmorComponent)root.Components[0]).SpeedPenalty = -8;
        ((ArmorComponent)visor.Components[0]).SpeedPenalty = -2;
        root.Components = new object[] { root.Components[0], new EquipmentPenaltyComponent(new() { SpeedPenaltyPercent = -8 }) };
        root.Slots = new[] { new GearSlot(visor) };
        var raw = Read(root);
        Assert.Equal(4, raw.ArmorClass);
        Assert.Equal(0, raw.PlateClass);
        Assert.Equal(2, raw.Zones);
        Assert.Equal(-10, raw.Speed);
        Assert.Equal(65, raw.TemplateMax);
    }

    [Fact]
    public void Plate_only_gear_and_absent_armor_have_safe_displays()
    {
        var root = new Gear { Slots = new[] { new GearSlot(Part(6, 40, 40, Array.Empty<string>(), new[] { "PlateFront" })) } };
        var rows = StatSheet.Armor(Read(root), new GearLevels { Mythic = -1 }, null, new(), new());
        Assert.Equal("— · 판 6", rows[0].Now);
        rows = StatSheet.Armor(Read(new Gear()), new GearLevels { Mythic = -1 }, null, new(), new());
        foreach (var index in new[] { 0, 1, 2, 6, 7, 8 }) Assert.Equal("—", rows[index].Now);
    }

    [Fact]
    public void Max_durability_preview_raises_every_part_damaged_or_not()
    {
        var raw = new ArmorStats { Parts = new()
        {
            new() { Durability = 40, Max = 40, TemplateMax = 40 },
            new() { Durability = 20, Max = 40, TemplateMax = 40 }
        } };
        var now = new GearLevels { Mythic = -1 };
        var next = now; next.Second = 1;
        var rows = StatSheet.Armor(raw, now, next, new TranscendTuning { MaxDurabilityPerLevel = 10 }, new());
        Assert.Equal("80", rows[2].Now);
        Assert.Equal("88", rows[2].Next);
    }

    [Fact]
    public void Popup_values_use_the_space_their_text_needs_and_overflow_wraps_inside_the_row()
    {
        var widths = PopupEffectLayout.Widths(556, 94, 170, 170);
        Assert.Equal(94, widths.Label);
        Assert.Equal(170, widths.Now);
        Assert.Equal(170, widths.Next);
        widths = PopupEffectLayout.Widths(556, 200, 300, 300);
        Assert.True(widths.Now > 0 && widths.Now < 300);
        Assert.True(widths.Next > 0 && widths.Next < 300);
        Assert.True(widths.Label + widths.Now + widths.Next <= 466.001f);
    }

    private static Gear Part(int armorClass, float durability, int templateMax, string[] zones, string[]? plates = null) => new()
    {
        Components = new object[] { new CompositeArmorComponent
        {
            ArmorClass = armorClass, ArmorColliders = zones, Plates = plates ?? Array.Empty<string>(),
            Repairable = new() { Durability = durability, TemplateDurability = templateMax }
        } }
    };

    public sealed class Gear
    {
        public object[] Components = Array.Empty<object>();
        public GearSlot[] Slots = Array.Empty<GearSlot>();
    }
    public record GearSlot(Gear ContainedItem, bool Locked = false);
    public class ArmorComponent
    {
        public int ArmorClass;
        public string[] ArmorColliders = Array.Empty<string>(), Plates = Array.Empty<string>();
        public RepairableComponent Repairable = new();
        public float SpeedPenalty, MousePenalty, WeaponErgonomicPenalty;
        public IEnumerable<string> GetArmorPlateColliders() => Plates;
    }
    public sealed class CompositeArmorComponent : ArmorComponent;
    public sealed class InvalidArmorComponent : ArmorComponent { public new float ArmorClass; }
    public sealed class RepairableComponent
    {
        public float Durability;
        public int TemplateDurability;
    }
    public sealed class PenaltyTemplate
    {
        public float SpeedPenaltyPercent, MousePenalty, WeaponErgonomicPenalty;
    }
    public record EquipmentPenaltyComponent(PenaltyTemplate Template);
}
