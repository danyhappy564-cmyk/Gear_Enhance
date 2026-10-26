using System.Security.Cryptography;
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
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Services.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace GearEnhance;

// All calls into SPT services live at this boundary. The enhancement transaction
// can be exercised on real SPT profile models without launching the game.
[Injectable]
public class SptEnhanceBackend(SaveServer saves, ItemHelper items, InventoryHelper inventory,
    HandbookHelper handbook, RagfairPriceService flea, MailSendService mail, TemplateTable tables)
{
    public virtual double Draw() => RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;
    public virtual SptProfile Profile(MongoId session) => saves.GetProfile(session);
    public virtual bool IsWeapon(MongoId template) => items.IsOfBaseclass(template, BaseClasses.WEAPON);

    public virtual bool Eligible(MongoId template) => new[] { BaseClasses.WEAPON, BaseClasses.ARMOR,
        BaseClasses.VEST, BaseClasses.HEADWEAR, BaseClasses.ARMORED_EQUIPMENT,
        BaseClasses.FACE_COVER, BaseClasses.VISORS }.Any(c => items.IsOfBaseclass(template, c));

    // Plates, soft inserts and helmet add-ons (visors, ear/neck armor): armor that only
    // works inside another piece. From 1.2.0 they share the gear's enhancement instead of
    // being enhanced alone. Worn armor (vests, rigs, helmets, face covers, eyewear) is not a part.
    public virtual bool IsArmorPart(MongoId template) => items.IsOfBaseclass(template, BaseClasses.ARMORED_EQUIPMENT)
        && !new[] { BaseClasses.ARMOR, BaseClasses.VEST, BaseClasses.HEADWEAR, BaseClasses.FACE_COVER, BaseClasses.VISORS }
            .Any(c => items.IsOfBaseclass(template, c));

    // Slot names of a template (attachments, plate slots, visor slots). Grids, chambers
    // and magazine cartridges are not slots, so ammo and stored loot never count as parts.
    public virtual HashSet<string> SlotNames(MongoId template) => items.GetItem(template).Value?.Properties?.Slots?
        .Select(s => s.Name).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToHashSet() ?? new HashSet<string>();

    // 1.3.0 third transcendence: guns with an automatic or burst mode gain fire rate.
    public virtual bool Automatic(MongoId template) => items.GetItem(template).Value?.Properties?.WeapFireType?
        .Any(mode => mode == "fullauto" || mode == "burst") == true;

    // 1.4.0 mythic armor: zones a template protects (soft armor colliders), and whether gear
    // draws its extra zones from the head (helmets, face covers, eyewear) or the body.
    public virtual IEnumerable<string> ArmorZones(MongoId template) =>
        items.GetItem(template).Value?.Properties?.ArmorColliders ?? Enumerable.Empty<string>();
    public virtual bool IsHeadArmor(MongoId template) => new[] { BaseClasses.HEADWEAR, BaseClasses.FACE_COVER, BaseClasses.VISORS }
        .Any(c => items.IsOfBaseclass(template, c));

    // Template maximum durability; the base of the armor 2nd transcendence grant.
    public virtual double TemplateMaxDurability(MongoId template) => items.GetItem(template).Value?.Properties?.MaxDurability ?? 0;

    public virtual IEnumerable<(MongoId Session, SptProfile Profile)> Profiles() =>
        saves.GetProfiles().Select(p => (p.Key, p.Value));

    // Enhancement insurance return. The mail hook moves the records to the new ids.
    public virtual void SendReturn(MongoId session, List<Item> tree, string text) =>
        mail.SendSystemMessageToPlayer(session, text, tree);

    // Prestige insurance return; kept as long as SPT keeps its own prestige transfer mail (a year).
    public virtual void SendPrestigeReturn(MongoId session, List<Item> tree, string text) =>
        mail.SendSystemMessageToPlayer(session, text, tree, 31536000L);

    // Number of prestige stages (6 with ContentBackportPrestiges); the last one has no next.
    public virtual int PrestigeMax() => tables?.Prestige?.Elements?.Count ?? 0;

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

    public virtual void RemoveMaterial(PmcData profile, MongoId id, int count, MongoId session,
        ItemEventRouterResponse output) => inventory.RemoveItemByCount(profile, id, count, session, output);

    public virtual void Destroy(PmcData profile, MongoId id, MongoId session,
        ItemEventRouterResponse output) => inventory.RemoveItem(profile, id, session, output);
}
