namespace EFT.InventoryLogic;

// Test fixture only, never referenced by the game plugin or its build.
public class EquipmentPenaltyComponent(GearEnhanceClientTests.TestItem item,
    GearEnhanceClientTests.TestPenaltyTemplate template)
{
    public GearEnhanceClientTests.TestItem Item { get; } = item;
    public readonly GearEnhanceClientTests.TestPenaltyTemplate Template = template;
}

// Stand-in for the game's slot container (armor plate slots, helmet visor slots).
public class Slot;
