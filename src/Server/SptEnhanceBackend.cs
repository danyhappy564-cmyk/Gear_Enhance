using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Ragfair;

namespace GearEnhance;

// All calls into SPT services live at this boundary. The enhancement transaction
// can be exercised on real SPT profile models without launching the game.
[Injectable]
public class SptEnhanceBackend(SaveServer saves, ItemHelper items, InventoryHelper inventory,
    HandbookHelper handbook, RagfairPriceService flea)
{
    public virtual SptProfile Profile(MongoId session) => saves.GetProfile(session);

    public virtual bool Eligible(MongoId template) => new[] { BaseClasses.WEAPON, BaseClasses.ARMOR,
        BaseClasses.VEST, BaseClasses.HEADWEAR, BaseClasses.ARMORED_EQUIPMENT,
        BaseClasses.FACE_COVER, BaseClasses.VISORS }.Any(c => items.IsOfBaseclass(template, c));

    public virtual (double Price, string Source) Price(MongoId template, PriceBasis basis)
    {
        if (basis == PriceBasis.Flea)
        {
            var price = flea.GetDynamicPriceForItem(template) ?? 0;
            if (double.IsFinite(price) && price > 0) return (price, "SPT 플리 기준가");
            return (handbook.GetTemplatePrice(template), "북밸류 (플리 가격 없음)");
        }
        return (handbook.GetTemplatePrice(template), "북밸류");
    }

    public virtual void RemoveMoney(PmcData profile, MongoId id, int count, MongoId session,
        ItemEventRouterResponse output) => inventory.RemoveItemByCount(profile, id, count, session, output);

    public virtual void Destroy(PmcData profile, MongoId id, MongoId session,
        ItemEventRouterResponse output) => inventory.RemoveItem(profile, id, session, output);
}
