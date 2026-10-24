using System.Collections.Concurrent;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace GearEnhance;

// 1.3.0: levels and pity of the two transcendence tracks of one gear.
public sealed class TranscendRecord
{
    public int Second { get; set; }
    public int Third { get; set; }
    public double SecondPity { get; set; }
    public double ThirdPity { get; set; }

    public int Level(TranscendTrack track) => track == TranscendTrack.Third ? Third : track == TranscendTrack.Second ? Second : 0;
    public double Pity(TranscendTrack track) => track == TranscendTrack.Third ? ThirdPity : track == TranscendTrack.Second ? SecondPity : 0;

    public void Set(TranscendTrack track, int level, double pity)
    {
        if (track == TranscendTrack.Third) (Third, ThirdPity) = (level, pity);
        else if (track == TranscendTrack.Second) (Second, SecondPity) = (level, pity);
    }

    public bool IsEmpty() => Second == 0 && Third == 0 && SecondPity == 0 && ThirdPity == 0;
}

// Max durability the armor 2nd transcendence added to one item, and the armor that gave it.
public sealed class DurabilityGrant
{
    public double Amount { get; set; }
    public string ArmorId { get; set; } = "";
}

// Item id → added max durability for SPT's flea and Fence pricing. Item ids are unique,
// so one table serves every profile. Only the grant helpers below write to it.
internal static class GrantIndex
{
    private static readonly ConcurrentDictionary<string, double> amounts = new();
    internal static void Set(string id, double amount)
    {
        if (amount > 0) amounts[id] = amount;
        else amounts.TryRemove(id, out _);
    }
    internal static void Remove(string id) => amounts.TryRemove(id, out _);
    internal static bool TryGet(string id, out double amount) => amounts.TryGetValue(id, out amount);
}

public sealed partial class EnhanceService
{
    private EnhanceQuote TranscendQuote(MongoId session, PmcData profile, Item target, EnhanceState state, EnhanceSettings settings,
        TranscendTrack track, bool weapon, List<Item> parts, double bodyPrice, string source, double partsPrice, double paidParts,
        EnhanceProtection? protection, bool chanceBoost)
    {
        var itemId = target.Id.ToString();
        if (!TranscendRules.Valid(track)) throw Loc.Argument("초월 종류를 골라 주세요.", "Choose a transcendence track.");
        var tracks = state.Tracks.GetValueOrDefault(itemId);
        var stars = Stars(state, itemId);
        if (tracks == null || tracks.Count == 0 || stars < TranscendRules.StarsNeeded(track))
            throw track == TranscendTrack.Third
                ? Loc.Invalid("3차 스타 진화(★★★)를 마친 장비만 3성 초월을 할 수 있습니다.", "Only gear with the 3rd star evolution (★★★) can take 3-star transcendence.")
                : Loc.Invalid("2차 스타 진화(★★)를 마친 장비만 2성 초월을 할 수 있습니다.", "Only gear with the 2nd star evolution (★★) can take 2-star transcendence.");
        var tuning = settings.Transcend;
        var record = state.Transcend.GetValueOrDefault(itemId) ?? new TranscendRecord();
        var level = record.Level(track);
        if (level < 0 || level > TranscendRules.MaxLevel)
            throw Loc.Invalid("초월 단계 기록이 올바르지 않습니다.", "The saved transcendence level is invalid.");
        if (level >= TranscendRules.MaxLevel)
            throw Loc.Invalid("이미 최대 초월 단계입니다.", "Already at the maximum transcendence level.");
        var next = level + 1;
        var automatic = weapon && backend.Automatic(target.Template);
        var kind = TranscendEffects.Kind(weapon, track, automatic);
        var pity = Math.Clamp(record.Pity(track), 0, tuning.PityCap);
        var chance = settings.AlwaysSucceed ? 100 : Math.Min(100, TranscendRules.Success(track, level, tuning) + pity);
        var tree = Tree(profile, itemId);
        var coverage = state.Coverage.GetValueOrDefault(itemId);
        var quote = new EnhanceQuote
        {
            QuoteId = Guid.NewGuid().ToString("N"), ItemId = itemId, Action = EnhanceAction.Transcend,
            Transcend = track, TranscendKind = kind, Automatic = automatic,
            TranscendSecond = record.Second, TranscendThird = record.Third,
            // Execute re-checks the gear's tracks; Path only has to name one of them.
            Path = tracks.Keys.First(), CurrentLevel = level, NextLevel = next,
            Cost = TranscendRules.Cost(bodyPrice + partsPrice, track, level, tuning),
            AvailableRoubles = Money(profile).Sum(i => Count(i)),
            AvailableBitcoins = Materials(profile, SuccessBoost.BitcoinTemplate).Sum(Count),
            SuccessPercent = chance, BaseSuccessPercent = chance,
            PityPercent = pity, PityStep = TranscendRules.PityStep(track, tuning), PityCap = tuning.PityCap,
            PriceSource = source, FailureMode = TranscendRules.DropsOnFailure(next) ? FailureMode.Downgrade : FailureMode.KeepItem,
            AttachedItems = tree.Count - 1, ExpiresUnixSeconds = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds(),
            IsWeapon = weapon, CurrentStars = stars, NextStars = stars, Complete = true, CompletesOnSuccess = true,
            PartsPrice = partsPrice, InsertedParts = parts.Count, UnregisteredParts = Unregistered(state, itemId, parts),
            CurrentInsurance = coverage?.Tier ?? InsuranceTier.None, CurrentInsurancePercent = coverage?.ReturnPercent ?? 0
        };
        Describe(quote, kind, level, next, tuning);
        quote.BaseCost = quote.Cost;
        quote.ChanceBoost = chanceBoost && quote.ChanceBoostAvailable;
        if (quote.ChanceBoost)
        {
            quote.BoostCost = SuccessBoost.TranscendRoubles;
            quote.BoostBitcoins = SuccessBoost.TranscendBitcoins;
            quote.SuccessPercent = Math.Min(100, quote.SuccessPercent + SuccessBoost.TranscendPoints);
        }
        // Nothing is ever destroyed; only the drop from level 4 and 5 attempts can be prevented.
        quote.Protection = new() { Downgrade = protection?.Downgrade == true && quote.DowngradeProtectionAvailable };
        quote.DowngradeProtectionFee = quote.DowngradeProtectionAvailable
            ? TranscendRules.ProtectionFee(quote.BaseCost, tuning) : 0;
        quote.Cost = ProtectionCosts.Total(quote.BaseCost, 0, quote.Protection.Downgrade ? quote.DowngradeProtectionFee : 0);
        quote.Cost = ProtectionCosts.Total(quote.Cost, quote.BoostCost, 0);
        quote.InsertSurcharge = weapon ? 0 : EnhanceRules.InsertSurcharge(partsPrice - paidParts, tracks, stars, settings, record.Second, record.Third);
        quote.Cost = ProtectionCosts.Total(quote.Cost, quote.InsertSurcharge, 0);
        quote.Materials = tuning.RequireMaterials
            ? TranscendRules.Materials(weapon, track, next).Select(m => new TranscendMaterial
                { Template = m.Template, Count = m.Count, Owned = Materials(profile, m.Template).Sum(Count) }).ToArray()
            : Array.Empty<TranscendMaterial>();
        pending[session] = new(quote, Fingerprint(tree), StateFingerprint(state, itemId), stars, settings.PriceBasis, tuning);
        return quote;
    }

    // Current and next effect of the chosen track, in percent unless named otherwise.
    private static void Describe(EnhanceQuote quote, TranscendKind kind, int level, int next, TranscendTuning tuning)
    {
        var now = TranscendEffects.Display(kind, level, tuning);
        var after = TranscendEffects.Display(kind, next, tuning);
        (quote.CurrentEffect, quote.NextEffect, quote.EffectCap) = (now.Main, after.Main, TranscendEffects.Display(kind, TranscendRules.MaxLevel, tuning).Main);
        (quote.CurrentSecondary, quote.NextSecondary) = (now.Secondary, after.Secondary);
        (quote.CurrentClassBonus, quote.NextClassBonus) = (now.Class, after.Class);
    }

    // A success resets the track's pity; a failure adds to it and, from DropFrom, drops one
    // level unless protected. Transcendence never destroys anything.
    private static double ApplyTranscend(EnhanceState state, EnhanceQuote quote, bool success, bool drop)
    {
        if (!state.Transcend.TryGetValue(quote.ItemId, out var record)) state.Transcend[quote.ItemId] = record = new();
        var pity = success ? 0 : Math.Min(quote.PityCap, Math.Max(0, record.Pity(quote.Transcend)) + quote.PityStep);
        record.Set(quote.Transcend, success ? quote.NextLevel : drop ? Math.Max(0, quote.CurrentLevel - 1) : quote.CurrentLevel, pity);
        if (record.IsEmpty()) state.Transcend.Remove(quote.ItemId);
        return pity;
    }

    // Materials are checked with the money, before the roll, and consumed on any outcome.
    private List<(TranscendMaterial Need, List<Item> Stock)> MaterialStock(PmcData profile, EnhanceQuote quote)
    {
        var stock = (quote.Materials ?? Array.Empty<TranscendMaterial>())
            .Select(need => (need, Materials(profile, need.Template).ToList())).ToList();
        if (stock.Any(s => s.Item2.Sum(Count) < s.need.Count))
            throw Loc.Invalid("창고의 초월 재료가 부족합니다.", "Not enough transcendence materials in the stash.");
        return stock;
    }

    private void Consume(PmcData profile, List<(TranscendMaterial Need, List<Item> Stock)> stock, MongoId session,
        SPTarkov.Server.Core.Models.Eft.ItemEvent.ItemEventRouterResponse output)
    {
        foreach (var (need, items) in stock)
        {
            var remaining = need.Count;
            foreach (var item in items)
            {
                if (remaining == 0) break;
                var take = (int)Math.Min(Count(item), remaining);
                backend.RemoveMaterial(profile, item.Id, take, session, output);
                remaining -= take;
            }
        }
    }

    // Book value of the materials spent on the held levels; part of the insurance value.
    private double MaterialValue(bool weapon, TranscendRecord? record)
    {
        if (record == null) return 0;
        var total = 0.0;
        foreach (var track in new[] { TranscendTrack.Second, TranscendTrack.Third })
            for (var level = 1; level <= TranscendRules.Clamp(record.Level(track)); level++)
                foreach (var (template, count) in TranscendRules.Materials(weapon, track, level))
                {
                    var price = backend.Price(new MongoId(template), PriceBasis.Handbook).Price;
                    if (double.IsFinite(price) && price > 0) total += price * count;
                }
        return total;
    }

    // Max durability from the armor 2nd transcendence: the armor and the registered parts
    // inside it now hold level × percent of their template maximum. A part's grant always
    // comes from the armor it sits in, never added up; parts taken out lose it here.
    // With `changed` null this only counts the items that would change.
    private int SyncGrants(PmcData profile, EnhanceState state, string armorId, TranscendTuning tuning, List<Item>? changed)
    {
        var items = InventoryItems(profile);
        var level = state.Transcend.GetValueOrDefault(armorId)?.Second ?? 0;
        var registered = state.Inserts.GetValueOrDefault(armorId)?.Ids;
        var want = new Dictionary<string, (Item Item, double Amount)>();
        if (level > 0)
            foreach (var item in SlotTree(items, armorId))
            {
                var id = item.Id.ToString();
                if (item.Upd?.Repairable == null || id != armorId && registered?.Contains(id) != true) continue;
                var baseMax = backend.TemplateMaxDurability(item.Template);
                if (double.IsFinite(baseMax) && baseMax > 0)
                    want[id] = (item, Math.Round(baseMax * TranscendEffects.MaxDurability(level, tuning) / 100, 2));
            }
        var count = 0;
        // Items away (flea, mail, insurance) keep their grant until they are back in the stash.
        var present = new Dictionary<string, Item>();
        foreach (var item in items) present.TryAdd(item.Id.ToString(), item);
        foreach (var (id, grant) in state.Grants.Where(g => g.Value.ArmorId == armorId && !want.ContainsKey(g.Key)).ToArray())
        {
            if (!present.TryGetValue(id, out var item) || item.Upd?.Repairable == null) continue;
            count++;
            if (changed == null) continue;
            Grant(item.Upd.Repairable, -grant.Amount);
            RemoveGrant(state, id);
            if (!changed.Contains(item)) changed.Add(item);
        }
        foreach (var (id, (item, amount)) in want)
        {
            var held = state.Grants.GetValueOrDefault(id);
            if (held != null && held.ArmorId == armorId && Math.Abs(held.Amount - amount) < 0.005) continue;
            count++;
            if (changed == null) continue;
            Grant(item.Upd!.Repairable!, amount - (held?.Amount ?? 0));
            SetGrant(state, id, new DurabilityGrant { Amount = amount, ArmorId = armorId });
            if (!changed.Contains(item)) changed.Add(item);
        }
        return count;
    }

    // Raising the maximum also fills a full item, so it stays full; a damaged one keeps its
    // durability (re-granting must never work as a free repair). Lowering it trims the excess.
    internal static void Grant(UpdRepairable repairable, double delta)
    {
        var max = repairable.MaxDurability ?? 0;
        var durability = repairable.Durability ?? 0;
        var full = durability >= max - 0.01;
        var next = Math.Max(Math.Min(1, max), max + delta);
        repairable.MaxDurability = next;
        repairable.Durability = delta > 0 && full ? next : Math.Min(durability, next);
    }

    // A trader return is the plain item: take the added maximum off the queued insurance
    // copies, then drop the records (also for gear lost without trader insurance).
    private static void StripGrants(SptProfile saved, int from, IEnumerable<string> ids, EnhanceState state)
    {
        var lost = ids.Where(state.Grants.ContainsKey).ToHashSet();
        if (lost.Count == 0) return;
        var list = saved.InsuranceList ?? new();
        for (var index = Math.Max(0, from); index < list.Count; index++)
            foreach (var item in list[index]?.Items ?? new())
                if (lost.Contains(item.Id.ToString()) && item.Upd?.Repairable != null)
                    Grant(item.Upd.Repairable, -state.Grants[item.Id.ToString()].Amount);
        foreach (var id in lost) RemoveGrant(state, id);
    }

    private static void SetGrant(EnhanceState state, string id, DurabilityGrant grant)
    {
        state.Grants[id] = grant;
        GrantIndex.Set(id, grant.Amount);
    }

    private static void RemoveGrant(EnhanceState state, string id)
    {
        state.Grants.Remove(id);
        GrantIndex.Remove(id);
    }

    // Fills the price index from every saved profile when the server starts. A profile whose
    // records cannot be read is skipped; it reports its own error when that player opens the window.
    public int IndexGrants()
    {
        lock (sync)
        {
            var total = 0;
            foreach (var (session, profile) in backend.Profiles())
            {
                if (profile?.ExtensionData?.ContainsKey(StateKey) != true) continue;
                try { total += State(session).Grants.Count; }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Text.Json.JsonException) { }
            }
            return total;
        }
    }

    private static string StateFingerprint(EnhanceState state, string id)
    {
        var record = state.Transcend.GetValueOrDefault(id);
        return TracksFingerprint(state.Tracks.GetValueOrDefault(id))
            + (record == null ? "" : $"#{record.Second}:{record.Third}:{record.SecondPity}:{record.ThirdPity}");
    }
}
