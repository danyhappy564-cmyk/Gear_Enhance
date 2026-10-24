using System.Reflection;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace GearEnhance;

// 1.3.0: the armor 2nd transcendence raises an item's real max durability. SPT rates flea
// and Fence prices by durability ÷ template maximum with no upper limit, so without this
// a granted item would sell for more. Rate it as if the grant were not there.
public sealed class DurabilityPricePatch : AbstractPatch
{
    protected override MethodBase? GetTargetMethod() => typeof(ItemHelper).GetMethod(
        "GetRepairableItemQualityValue", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

    [PatchPostfix]
    public static void Postfix(TemplateItem itemDetails, UpdRepairable repairable, Item item, ref double __result)
    {
        try
        {
            if (item == null || repairable == null || !GrantIndex.TryGet(item.Id.ToString(), out var grant)) return;
            __result = Quality(repairable.Durability ?? 0, repairable.MaxDurability ?? 0, grant, itemDetails?.Properties?.MaxDurability, __result);
        }
        // Pricing must never fail because of this mod.
        catch { }
    }

    // Durability scaled back to the maximum without the grant, then SPT's own sqrt rating.
    internal static double Quality(double durability, double max, double grant, double? templateMax, double original)
    {
        var plainMax = max - grant;
        var baseMax = templateMax is > 0 ? templateMax.Value : plainMax;
        if (grant <= 0 || max <= 0 || plainMax <= 0 || baseMax <= 0 || durability <= 0) return original;
        return Math.Sqrt(Math.Min(1, durability * plainMax / max / baseMax));
    }
}
