namespace EFT.InventoryLogic;

// The names match the game so the instance-aware template-read rewrite is exercised.
public sealed class AmmoTemplate
{
    public int ammoAccr = -20, ammoRec = 12;
    public float AmmoFactor => ammoAccr <= 0 ? (100f + Math.Abs(ammoAccr)) / 100f : 100f / (100 + ammoAccr);
}

public sealed class Ammo() : GearEnhanceClientTests.AmmoHooksTests.Item("fixture")
{
    public int ammoRec => ((AmmoTemplate)Template).ammoRec;
    public float AmmoFactor => ((AmmoTemplate)Template).AmmoFactor;
}
