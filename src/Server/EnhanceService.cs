using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using SPTarkov.Server.Core.Utils;

namespace GearEnhance;

public sealed class EnhanceState
{
    public Dictionary<string, int> Levels { get; set; } = new();
    public List<EnhanceReceipt> Receipts { get; set; } = new();
    // Legacy 0.1/0.2 fields are read once, then removed per migrated item.
    public Dictionary<string, EnhancePath> Paths { get; set; } = new();
    public Dictionary<string, Dictionary<EnhancePath, int>> Tracks { get; set; } = new();
    public HashSet<string> ThirdStarItems { get; set; } = new();
    public HashSet<string> EvolvedItems { get; set; } = new();
    // 1.2.0: parts registered to each enhanced armor, enhancement insurance per item,
    // and ids whose enhancement was forfeited in a raid (traders return them as +0).
    public Dictionary<string, InsertRecord> Inserts { get; set; } = new();
    public Dictionary<string, InsuranceCoverage> Coverage { get; set; } = new();
    public HashSet<string> Forfeited { get; set; } = new();
    // 1.3.0: transcendence per gear, and max durability added to armor items by it.
    public Dictionary<string, TranscendRecord> Transcend { get; set; } = new();
    public Dictionary<string, DurabilityGrant> Grants { get; set; } = new();
    // Plates inserted into enhanced armor register on their own when that is free; the raid
    // end does the same with the settings and switch the client last sent.
    public EnhanceSettings? LastSettings { get; set; }
    public bool AutoRegisterInserts { get; set; } = true;
}

public sealed class InsertRecord
{
    // Every part ever registered; only these share the armor's effect while inserted.
    public HashSet<string> Ids { get; set; } = new();
    // Templates of the most valuable registered set; their price was paid at every level.
    public List<string> PaidTemplates { get; set; } = new();
}

public sealed class InsuranceCoverage
{
    public InsuranceTier Tier { get; set; }
    public double ReturnPercent { get; set; }
    public int Paid { get; set; }
}

// Recorded gear that entered a raid: slot trees cloned before SPT rewrites the inventory.
public sealed class RaidCapture
{
    public int InsuranceCount { get; set; }
    public List<(string Root, List<Item> Tree)> Gear { get; } = new();
    // Items holding added max durability that were in the inventory before the raid.
    public HashSet<string> Granted { get; } = new();
}

// The query router and item-event router are resolved separately by SPT.
// Quotes and their lock must belong to the same server-wide instance.
[Injectable(InjectionType.Singleton)]
public sealed partial class EnhanceService(SptEnhanceBackend backend, JsonUtil serializer)
{
    private const string StateKey = "rfGearEnhance";
    private const string Roubles = "5449016a4bdc2d6f028b456f";
    private readonly object sync = new();
    private readonly Dictionary<MongoId, PendingQuote> pending = new();

    private sealed record PendingQuote(EnhanceQuote View, string Fingerprint, string TrackFingerprint, int Stars, PriceBasis Basis,
        TranscendTuning Tuning);

    public EnhanceQuote Quote(MongoId session, string itemId, EnhanceSettings settings, EnhancePath? path = null,
        EnhanceProtection? protection = null, EnhanceAction action = EnhanceAction.Upgrade, bool chanceBoost = false,
        InsuranceTier tier = InsuranceTier.None, TranscendTrack track = TranscendTrack.None)
    {
        lock (sync)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            if (!Enum.IsDefined(action)) throw Loc.Argument("강화 작업이 올바르지 않습니다.", "Invalid enhancement action.");
            var profile = GetPmc(session);
            var target = GetTarget(profile, itemId);
            var state = State(session);
            state.LastSettings = settings;
            // Discard metadata only for items no longer owned by this profile. Insured
            // losses, flea listings and unread mail keep theirs until they come back.
            var owned = Tracked(session, profile).Keys.ToHashSet();
            foreach (var key in state.Levels.Keys.Concat(state.Tracks.Keys).Concat(state.EvolvedItems).Concat(state.ThirdStarItems)
                .Concat(state.Transcend.Keys).Where(k => !owned.Contains(k)).Distinct().ToArray())
            {
                state.Transcend.Remove(key);
                state.Levels.Remove(key);
                state.Paths.Remove(key);
                state.Tracks.Remove(key);
                state.EvolvedItems.Remove(key);
                state.ThirdStarItems.Remove(key);
            }
            foreach (var key in state.Inserts.Keys.Concat(state.Coverage.Keys).Where(k => !owned.Contains(k)).Distinct().ToArray())
            {
                state.Inserts.Remove(key);
                state.Coverage.Remove(key);
            }
            foreach (var record in state.Inserts.Values) record.Ids.RemoveWhere(id => !owned.Contains(id));
            state.Forfeited.RemoveWhere(id => !owned.Contains(id));
            foreach (var key in state.Grants.Keys.Where(k => !owned.Contains(k)).ToArray()) RemoveGrant(state, key);
            var weapon = backend.IsWeapon(target.Template);
            var parts = weapon ? new List<Item>() : Parts(profile, itemId);
            if (!weapon) EnsureInserts(state, itemId, parts);
            var (bodyPrice, source) = backend.Price(target.Template, settings.PriceBasis);
            var partsPrice = PartsValue(parts.Select(p => p.Template.ToString()), settings.PriceBasis);
            var paidParts = PartsValue(state.Inserts.GetValueOrDefault(itemId)?.PaidTemplates, settings.PriceBasis);
            if (action == EnhanceAction.Insure || action == EnhanceAction.RegisterInserts)
                return ManageQuote(session, profile, itemId, state, settings, action, tier, weapon, parts, bodyPrice, source, partsPrice, paidParts);
            if (action == EnhanceAction.Transcend)
                return TranscendQuote(session, profile, target, state, settings, track, weapon, parts, bodyPrice, source, partsPrice, paidParts,
                    protection, chanceBoost);
            var tracks = state.Tracks.GetValueOrDefault(itemId);
            var selected = path ?? (tracks?.Count > 0 ? tracks.Keys.First()
                : weapon ? EnhancePath.Recoil : EnhancePath.ArmorDurability);
            if (!EnhanceEffects.Allowed(selected, weapon))
                throw Loc.Argument("이 장비에 사용할 수 없는 강화 항목입니다.", "This track cannot be used on this gear.");
            var legacy = (tracks == null || tracks.Count == 0) && state.Levels.GetValueOrDefault(itemId) > 0;
            var level = legacy ? state.Levels[itemId] : tracks?.GetValueOrDefault(selected) ?? 0;
            if (level < 0 || level > 100) throw Loc.Invalid("강화 단계 기록이 올바르지 않습니다.", "The saved enhancement level is invalid.");
            var evolving = action != EnhanceAction.Upgrade;
            var third = action == EnhanceAction.EvolveThird;
            var stars = Stars(state, itemId);
            if (evolving && (legacy || !EnhanceProgression.AllAtLeast(tracks, weapon, settings.MaxLevel)))
                throw Loc.Invalid("모든 항목을 최대 단계까지 강화한 뒤 스타 강화 진화를 시도해 주세요.", "Max out every track before attempting star evolution.");
            if (evolving && ((!third && stars != 1) || (third && stars != 2)))
                throw third ? Loc.Invalid("2차 스타 진화를 완료한 장비만 3차 진화를 시도할 수 있습니다.", "Only gear with a completed 2nd star evolution can attempt the 3rd.")
                    : Loc.Invalid("이미 2차 스타 진화를 완료한 장비입니다.", "This gear already completed the 2nd star evolution.");
            var nextStars = evolving ? stars + 1 : stars;
            // The client hides maxed tracks; the server enforces it for any caller.
            if (!legacy && !evolving && level >= settings.MaxLevel)
                throw Loc.Invalid("이 항목은 최대 단계입니다. 다른 항목을 선택해 주세요.", "This track is at the maximum level. Choose another track.");
            var nextLevel = legacy || evolving ? level : level + 1;
            // Armor is priced with what is in its slots, so plates raise the cost with it.
            var price = bodyPrice + partsPrice;
            var tree = Tree(profile, itemId);
            var highRisk = !legacy && !evolving && weapon && settings.HighRiskEnabled
                && nextLevel >= 6;
            var nextTracks = tracks == null ? new Dictionary<EnhancePath, int>() : new(tracks);
            nextTracks[selected] = nextLevel;
            var complete = EnhanceProgression.AllAtLeast(tracks, weapon, settings.MaxLevel);
            var completes = EnhanceProgression.AllAtLeast(nextTracks, weapon, settings.MaxLevel);
            var quote = new EnhanceQuote
            {
                QuoteId = Guid.NewGuid().ToString("N"), ItemId = itemId,
                CurrentLevel = level, NextLevel = nextLevel,
                Cost = legacy ? 0 : evolving ? third ? 4000000 : EnhanceRules.EvolutionCost(price, settings) : EnhanceRules.Cost(price, level, settings),
                AvailableRoubles = Money(profile).Sum(i => Count(i)),
                SuccessPercent = legacy || settings.AlwaysSucceed ? 100 : evolving ? third ? settings.ThirdStarSuccessPercent : settings.EvolutionSuccessPercent : EnhanceProgression.Success(level, settings),
                PriceSource = source, FailureMode = legacy ? FailureMode.KeepItem : evolving ? FailureMode.Destroy : highRisk ? FailureMode.Downgrade : settings.FailureMode,
                HighRisk = highRisk, DestructionPercent = highRisk ? settings.HighRiskDestructionPercent : 0,
                AttachedItems = tree.Count - 1,
                ExpiresUnixSeconds = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds(),
                Path = selected, IsWeapon = weapon,
                BindLegacy = legacy,
                CurrentEffect = legacy ? 0 : EnhanceEffects.Amount(selected, level, settings.Effects, stars),
                NextEffect = EnhanceEffects.Amount(selected, nextLevel, settings.Effects, nextStars),
                EffectCap = EnhanceEffects.Amount(selected, settings.MaxLevel, settings.Effects, evolving ? nextStars : stars),
                Complete = complete, CompletesOnSuccess = completes,
                NextGainMultiplier = EnhanceProgression.GainMultiplier(nextLevel, settings.MaxLevel), IsEvolution = evolving, CurrentStars = stars, NextStars = nextStars
            };
            quote.BaseSuccessPercent = quote.SuccessPercent;
            quote.AvailableBitcoins = Materials(profile, SuccessBoost.BitcoinTemplate).Sum(Count);
            quote.ChanceBoost = chanceBoost && quote.ChanceBoostAvailable;
            if (quote.ChanceBoost)
            {
                quote.BoostCost = SuccessBoost.Roubles(evolving, nextStars);
                quote.BoostBitcoins = SuccessBoost.Bitcoins(evolving, nextStars);
                quote.SuccessPercent = Math.Min(100, quote.SuccessPercent + SuccessBoost.Points(evolving));
            }
            // Per-level loss must not silently turn a positive base chance into a
            // paid attempt that can never succeed. An explicit 0% base is a choice.
            if (!legacy && !evolving && settings.SuccessPercent > 0 && quote.SuccessPercent <= 0)
                throw Loc.Invalid("단계별 성공률 감소로 이 단계의 성공률이 0%가 됐습니다. F12 개발자 옵션의 단계별 감소 또는 최대 단계를 낮춰 주세요.", "The per-level chance loss brought this level's chance to 0%. Lower the per-level loss or max level in the F12 developer options.");
            quote.BaseCost = quote.Cost;
            // Inapplicable protections are removed without charging (100% success,
            // legacy restore, safe failure mode, or an outcome with zero chance).
            quote.Protection = new()
            {
                Destruction = protection?.Destruction == true && quote.DestructionProtectionAvailable,
                Downgrade = protection?.Downgrade == true && quote.DowngradeProtectionAvailable
            };
            quote.DestructionProtectionFee = quote.DestructionProtectionAvailable
                ? ProtectionCosts.Fee(quote.BaseCost, quote.NextLevel, settings.DestructionProtectionPercent) : 0;
            quote.DowngradeProtectionFee = quote.DowngradeProtectionAvailable
                ? ProtectionCosts.Fee(quote.BaseCost, quote.NextLevel, settings.DowngradeProtectionPercent) : 0;
            quote.Cost = ProtectionCosts.Total(quote.BaseCost,
                quote.Protection.Destruction ? quote.DestructionProtectionFee : 0,
                quote.Protection.Downgrade ? quote.DowngradeProtectionFee : 0);
            quote.Cost = ProtectionCosts.Total(quote.Cost, quote.BoostCost, 0);
            quote.Action = action;
            quote.PartsPrice = partsPrice;
            quote.InsertedParts = parts.Count;
            quote.UnregisteredParts = Unregistered(state, itemId, parts);
            // Shown in the window header so the coverage is visible without opening it.
            var coverage = state.Coverage.GetValueOrDefault(itemId);
            quote.CurrentInsurance = coverage?.Tier ?? InsuranceTier.None;
            quote.CurrentInsurancePercent = coverage?.ReturnPercent ?? 0;
            var transcend = state.Transcend.GetValueOrDefault(itemId);
            quote.InsertSurcharge = legacy ? 0 : EnhanceRules.InsertSurcharge(partsPrice - paidParts, tracks, stars, settings,
                transcend?.Second ?? 0, transcend?.Third ?? 0);
            quote.Cost = ProtectionCosts.Total(quote.Cost, quote.InsertSurcharge, 0);
            // One current quote per profile; opening another item invalidates the old one.
            pending[session] = new(quote, Fingerprint(tree), StateFingerprint(state, itemId), stars, settings.PriceBasis, settings.Transcend);
            return quote;
        }
    }

    // Enhancement insurance and plate registration: no roll, the fee is the whole cost.
    private EnhanceQuote ManageQuote(MongoId session, PmcData profile, string itemId, EnhanceState state, EnhanceSettings settings,
        EnhanceAction action, InsuranceTier tier, bool weapon, List<Item> parts, double bodyPrice, string source, double partsPrice, double paidParts)
    {
        var tracks = state.Tracks.GetValueOrDefault(itemId);
        if (tracks == null || !tracks.Values.Any(level => level > 0))
            throw Loc.Invalid("강화된 장비만 강화 보험 가입·방탄판 등록을 할 수 있습니다.", "Only enhanced gear can be insured or have plates registered.");
        var stars = Stars(state, itemId);
        var transcend = state.Transcend.GetValueOrDefault(itemId);
        var registration = weapon ? 0 : EnhanceRules.InsertSurcharge(partsPrice - paidParts, tracks, stars, settings,
            transcend?.Second ?? 0, transcend?.Third ?? 0);
        var unregistered = Unregistered(state, itemId, parts);
        var value = EnhanceRules.Value(bodyPrice + paidParts, tracks, stars, settings,
            transcend?.Second ?? 0, transcend?.Third ?? 0, MaterialValue(weapon, transcend));
        var coverage = state.Coverage.GetValueOrDefault(itemId);
        var quote = new EnhanceQuote
        {
            QuoteId = Guid.NewGuid().ToString("N"), ItemId = itemId, Action = action, IsWeapon = weapon,
            // Execute re-checks this path's level, so both must name the same track.
            Path = tracks.Keys.First(), CurrentLevel = tracks.First().Value, NextLevel = tracks.First().Value,
            SuccessPercent = 100, BaseSuccessPercent = 100, FailureMode = FailureMode.KeepItem, PriceSource = source,
            AvailableRoubles = Money(profile).Sum(i => Count(i)), AvailableBitcoins = Materials(profile, SuccessBoost.BitcoinTemplate).Sum(Count),
            ExpiresUnixSeconds = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds(),
            CurrentStars = stars, NextStars = stars, AttachedItems = Tree(profile, itemId).Count - 1,
            PartsPrice = partsPrice, InsertedParts = parts.Count, UnregisteredParts = unregistered, RegistrationFee = registration,
            InsuranceTier = tier, CurrentInsurance = coverage?.Tier ?? InsuranceTier.None, CurrentInsurancePercent = coverage?.ReturnPercent ?? 0,
            InsuredValue = value, TranscendSecond = transcend?.Second ?? 0, TranscendThird = transcend?.Third ?? 0,
            DurabilityPending = weapon ? 0 : SyncGrants(profile, state, itemId, settings.Transcend, null)
        };
        foreach (var option in new[] { InsuranceTier.Basic, InsuranceTier.Advanced, InsuranceTier.Premium })
        {
            quote.InsuranceFees[(int)option] = EnhanceRules.InsuranceFee(value, option, settings);
            quote.InsuranceChances[(int)option] = settings.ReturnPercent(option);
        }
        if (action == EnhanceAction.Insure)
        {
            // Plates worth more than the paid ones change the value; register them first.
            quote.CanBuyInsurance = Enum.IsDefined(tier) && tier != InsuranceTier.None && registration == 0
                && (coverage == null || coverage.Tier < tier);
            quote.Cost = quote.CanBuyInsurance ? quote.InsuranceFees[(int)tier] : 0;
        }
        else
        {
            // Also offered when a registered part went back in and its added max durability is missing.
            quote.CanRegister = !weapon && (unregistered > 0 || quote.DurabilityPending > 0);
            quote.Cost = quote.InsertSurcharge = quote.CanRegister ? registration : 0;
        }
        pending[session] = new(quote, Fingerprint(Tree(profile, itemId)), StateFingerprint(state, itemId), stars, settings.PriceBasis,
            settings.Transcend);
        return quote;
    }

    // Plates, soft inserts and visors in the gear's slots (not loose items in its pockets).
    private List<Item> Parts(PmcData profile, string root) => SlotTree(InventoryItems(profile), root).Skip(1)
        .Where(i => backend.Eligible(i.Template) && !backend.IsWeapon(i.Template)).ToList();

    // Root first, then its slot children breadth-first. Bounded by a visited set.
    private List<Item> SlotTree(List<Item> items, string root)
    {
        var start = items.FirstOrDefault(i => i.Id.ToString() == root);
        if (start == null) return new();
        var children = items.Where(i => i.ParentId != null).ToLookup(i => i.ParentId!);
        var slots = new Dictionary<string, HashSet<string>>();
        var tree = new List<Item> { start };
        var seen = new HashSet<string> { root };
        for (var index = 0; index < tree.Count; index++)
        {
            var parent = tree[index];
            var template = parent.Template.ToString();
            if (!slots.TryGetValue(template, out var names)) slots[template] = names = backend.SlotNames(parent.Template);
            foreach (var child in children[parent.Id.ToString()])
                if (child.SlotId != null && names.Contains(child.SlotId) && seen.Add(child.Id.ToString())) tree.Add(child);
        }
        return tree;
    }

    private double PartsValue(IEnumerable<string>? templates, PriceBasis basis) => templates?.Sum(template =>
    {
        var price = backend.Price(new MongoId(template), basis).Price;
        return double.IsFinite(price) && price > 0 ? price : 0;
    }) ?? 0;

    private static int Unregistered(EnhanceState state, string id, List<Item> parts) =>
        parts.Count(p => state.Inserts.GetValueOrDefault(id)?.Ids.Contains(p.Id.ToString()) != true);

    // Armor enhanced before 1.2.0 keeps the parts it had when first seen, free of charge.
    private static void EnsureInserts(EnhanceState state, string id, List<Item> parts)
    {
        if (state.Inserts.ContainsKey(id) || !Recorded(state, id)) return;
        state.Inserts[id] = new InsertRecord { Ids = parts.Select(p => p.Id.ToString()).ToHashSet(),
            PaidTemplates = parts.Select(p => p.Template.ToString()).ToList() };
    }

    private void Enroll(EnhanceState state, string id, List<Item> parts, PriceBasis basis)
    {
        if (!state.Inserts.TryGetValue(id, out var record)) state.Inserts[id] = record = new();
        foreach (var part in parts) record.Ids.Add(part.Id.ToString());
        var current = parts.Select(p => p.Template.ToString()).ToList();
        if (PartsValue(current, basis) > PartsValue(record.PaidTemplates, basis)) record.PaidTemplates = current;
    }

    private static void Forget(EnhanceState state, string id)
    {
        state.Levels.Remove(id);
        state.Paths.Remove(id);
        state.Tracks.Remove(id);
        state.EvolvedItems.Remove(id);
        state.ThirdStarItems.Remove(id);
        state.Inserts.Remove(id);
        state.Coverage.Remove(id);
        state.Transcend.Remove(id);
        RemoveGrant(state, id);
    }

    public EnhanceReceipt? Receipt(MongoId session, string quoteId)
    {
        lock (sync) return State(session).Receipts.LastOrDefault(r => r.QuoteId == quoteId);
    }

    public EnhanceSnapshot Snapshot(MongoId session)
    {
        lock (sync)
        {
            var profile = GetPmc(session);
            var state = State(session);
            // Mail attachments are included so the returned item already shows its badge.
            var owned = Tracked(session, profile);
            var inventory = InventoryItems(profile).Select(i => i.Id.ToString()).ToHashSet();
            foreach (var id in state.Tracks.Keys.Where(id => inventory.Contains(id) && !state.Inserts.ContainsKey(id)
                && !backend.IsWeapon(owned[id].Template)).ToArray())
                EnsureInserts(state, id, Parts(profile, id));
            return new EnhanceSnapshot
            {
                Inserts = state.Inserts.Where(p => owned.ContainsKey(p.Key) && p.Value.Ids.Count > 0)
                    .Select(p => new InsertLink { ArmorId = p.Key, PartIds = p.Value.Ids.ToArray() }).ToArray(),
                ProfileId = profile.Id.ToString() ?? "",
                // A part enhanced alone before 1.2.0 keeps its record, but only the armor
                // it is registered to gives it an effect now.
                Items = state.Tracks.Where(p => owned.ContainsKey(p.Key) && !backend.IsArmorPart(owned[p.Key].Template))
                    .SelectMany(p => p.Value.Where(track => track.Value > 0 && track.Value <= 100
                        && EnhanceEffects.Allowed(track.Key, backend.IsWeapon(owned[p.Key].Template)))
                        .Select(track => new EnhanceRecord { ItemId = p.Key, Path = track.Key, Level = track.Value,
                            Evolved = Stars(state, p.Key) >= 2, StarLevel = Stars(state, p.Key) }))
                    .ToArray(),
                Transcend = state.Transcend.Where(p => owned.ContainsKey(p.Key) && !backend.IsArmorPart(owned[p.Key].Template)
                        && (p.Value.Second > 0 || p.Value.Third > 0))
                    .Select(p => new TranscendLink { ItemId = p.Key, Second = TranscendRules.Clamp(p.Value.Second),
                        Third = TranscendRules.Clamp(p.Value.Third) }).ToArray(),
                Durability = state.Grants.Where(p => owned.ContainsKey(p.Key) && p.Value.Amount > 0)
                    .Select(p => new DurabilityLink { ItemId = p.Key, Amount = p.Value.Amount }).ToArray()
            };
        }
    }

    public void Execute(MongoId session, PmcData profile, string quoteId, bool confirmDestruction,
        ItemEventRouterResponse output)
    {
        lock (sync)
        {
            var state = State(session);
            if (state.Receipts.Any(r => r.QuoteId == quoteId))
                throw Loc.Invalid("이미 처리된 강화입니다. 결과를 확인해 주세요.", "This enhancement was already processed. Check the result.");
            if (!pending.TryGetValue(session, out var saved) || saved.View.QuoteId != quoteId)
                throw Loc.Invalid("강화 견적을 다시 불러와 주세요.", "Reload the enhancement quote.");
            var quote = saved.View;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= quote.ExpiresUnixSeconds)
                throw Loc.Invalid("견적이 만료됐습니다. 비용을 다시 확인해 주세요.", "The quote expired. Check the cost again.");
            var target = GetTarget(profile, quote.ItemId);
            var changedItems = output.ProfileChanges?[session]?.Items?.ChangedItems
                ?? throw Loc.Invalid("SPT 아이템 응답을 준비할 수 없습니다.", "Cannot prepare the SPT item response.");
            var tree = Tree(profile, quote.ItemId);
            var recordedLevel = quote.Action == EnhanceAction.Transcend ? state.Transcend.GetValueOrDefault(quote.ItemId)?.Level(quote.Transcend) ?? 0
                : quote.BindLegacy ? state.Levels.GetValueOrDefault(quote.ItemId)
                : state.Tracks.GetValueOrDefault(quote.ItemId)?.GetValueOrDefault(quote.Path) ?? 0;
            if (recordedLevel != quote.CurrentLevel || Fingerprint(tree) != saved.Fingerprint
                || StateFingerprint(state, quote.ItemId) != saved.TrackFingerprint
                || Stars(state, quote.ItemId) != saved.Stars)
                throw Loc.Invalid("장비 상태가 바뀌었습니다. 견적을 다시 불러와 주세요.", "The gear changed. Reload the quote.");
            if (quote.Action == EnhanceAction.Insure && !quote.CanBuyInsurance || quote.Action == EnhanceAction.RegisterInserts && !quote.CanRegister)
                throw Loc.Invalid("지금은 이 강화 보험·방탄판 등록을 할 수 없습니다. 견적을 다시 불러와 주세요.", "This insurance or plate registration is not available now. Reload the quote.");
            if (quote.CanDestroy && !confirmDestruction)
                throw Loc.Invalid("장비와 부착물 파괴 안내를 확인해 주세요.", "Confirm the gear and attachment destruction notice.");
            var material = Materials(profile, SuccessBoost.BitcoinTemplate).ToList();
            if (material.Sum(Count) < quote.BoostBitcoins)
                throw Loc.Invalid("성공률 상승에 필요한 비트코인이 부족합니다.", "Not enough Bitcoins for the chance boost.");
            var purse = Money(profile).ToList();
            if (purse.Sum(i => Count(i)) < quote.Cost)
                throw Loc.Invalid("창고의 루블이 부족합니다.", "Not enough roubles in the stash.");
            var stock = MaterialStock(profile, quote);

            // All checks precede payment. Native item-event output keeps the
            // client's stacks, deleted items and durability in sync with SPT.
            var success = EnhanceRules.Succeeds(quote.SuccessPercent,
                backend.Draw());
            var failureMode = !success ? EnhanceRules.Failure(quote,
                backend.Draw()) : quote.FailureMode;
            var destructionPrevented = !success && failureMode == FailureMode.Destroy && quote.Protection.Destruction;
            var downgradePrevented = !success && failureMode == FailureMode.Downgrade && quote.Protection.Downgrade;
            if (destructionPrevented || downgradePrevented) failureMode = FailureMode.KeepItem;
            pending.Remove(session);
            var remaining = quote.Cost;
            foreach (var money in purse)
            {
                if (remaining == 0) break;
                var take = (int)Math.Min(Count(money), remaining);
                backend.RemoveMoney(profile, money.Id, take, session, output);
                remaining -= take;
            }
            var remainingBitcoins = quote.BoostBitcoins;
            foreach (var bitcoin in material)
            {
                if (remainingBitcoins == 0) break;
                var take = (int)Math.Min(Count(bitcoin), remainingBitcoins);
                backend.RemoveMaterial(profile, bitcoin.Id, take, session, output);
                remainingBitcoins -= take;
            }
            Consume(profile, stock, session, output);
            var weapon = backend.IsWeapon(target.Template);
            var parts = weapon ? new List<Item>() : Parts(profile, quote.ItemId);
            var pity = 0.0;
            if (quote.Action == EnhanceAction.Transcend)
                pity = ApplyTranscend(state, quote, success, failureMode == FailureMode.Downgrade);
            else if (quote.Action == EnhanceAction.Insure)
                state.Coverage[quote.ItemId] = new InsuranceCoverage { Tier = quote.InsuranceTier,
                    ReturnPercent = quote.InsuranceChances[(int)quote.InsuranceTier], Paid = quote.Cost };
            else if (quote.Action == EnhanceAction.RegisterInserts) { }
            else if (success && quote.IsEvolution)
            {
                state.EvolvedItems.Add(quote.ItemId);
                if (quote.NextStars == 3) state.ThirdStarItems.Add(quote.ItemId);
            }
            else if (success)
            {
                if (!state.Tracks.TryGetValue(quote.ItemId, out var tracks))
                    state.Tracks[quote.ItemId] = tracks = new();
                tracks[quote.Path] = quote.NextLevel;
                state.Levels.Remove(quote.ItemId);
                state.Paths.Remove(quote.ItemId);
            }
            else if (failureMode == FailureMode.Downgrade)
            {
                var tracks = state.Tracks.GetValueOrDefault(quote.ItemId);
                if (tracks != null)
                {
                    if (quote.CurrentLevel > 1) tracks[quote.Path] = quote.CurrentLevel - 1;
                    else tracks.Remove(quote.Path);
                    if (tracks.Count == 0) state.Tracks.Remove(quote.ItemId);
                }
            }
            else if (failureMode == FailureMode.ZeroDurability)
            {
                foreach (var part in tree.Where(i => i.Upd?.Repairable != null))
                {
                    part.Upd!.Repairable!.Durability = 0;
                    changedItems.Add(part);
                }
            }
            else if (failureMode == FailureMode.Destroy)
            {
                backend.Destroy(profile, target.Id, session, output);
                foreach (var part in tree) Forget(state, part.Id.ToString());
            }
            // The parts' price was part of this payment, so they now share the effect.
            // A new level or more valuable plates make the old insurance fee too low.
            if (!weapon && failureMode != FailureMode.Destroy && quote.Action != EnhanceAction.Insure)
                Enroll(state, quote.ItemId, parts, saved.Basis);
            if (!weapon && failureMode != FailureMode.Destroy)
                SyncGrants(profile, state, quote.ItemId, saved.Tuning, changedItems);
            if (quote.Action != EnhanceAction.Insure && (success && quote.Action != EnhanceAction.RegisterInserts || quote.InsertSurcharge > 0))
                state.Coverage.Remove(quote.ItemId);
            state.Receipts.Add(new EnhanceReceipt
            {
                QuoteId = quoteId, ItemId = quote.ItemId, Success = success,
                Level = success ? quote.NextLevel : failureMode == FailureMode.Downgrade ? Math.Max(0, quote.CurrentLevel - 1) : quote.CurrentLevel,
                Paid = quote.Cost, BitcoinsPaid = quote.BoostBitcoins, FailureMode = failureMode, Path = quote.Path,
                DestructionPrevented = destructionPrevented, DowngradePrevented = downgradePrevented,
                IsEvolution = quote.IsEvolution, Evolved = Stars(state, quote.ItemId) >= 2,
                Action = quote.Action, InsuranceTier = quote.Action == EnhanceAction.Insure ? quote.InsuranceTier : InsuranceTier.None,
                Transcend = quote.Action == EnhanceAction.Transcend ? quote.Transcend : TranscendTrack.None, Pity = pity,
                StarLevel = failureMode == FailureMode.Destroy && !success ? 0 : Stars(state, quote.ItemId),
                CompletedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            if (state.Receipts.Count > 100) state.Receipts.RemoveRange(0, state.Receipts.Count - 100);
        }
    }

    // SPT clones every mail attachment with new ids (insurance returns, expired
    // or cancelled flea offers). Move each lost item's records to its new id.
    public int Remap(MongoId recipient, IReadOnlyList<(string Old, string New)> pairs)
    {
        lock (sync)
        {
            var saved = backend.Profile(recipient);
            if (saved?.ExtensionData == null || !saved.ExtensionData.ContainsKey(StateKey) || pairs.Count == 0) return 0;
            var state = State(recipient);
            // A mail copy of an item the player still owns is a duplicate, never a return.
            var inventory = saved.CharacterData?.PmcData?.Inventory?.Items?.Select(i => i.Id.ToString()).ToHashSet()
                ?? new HashSet<string>();
            var moved = 0;
            var map = new Dictionary<string, string>();
            foreach (var (old, next) in pairs) map.TryAdd(old, next);
            foreach (var (old, next) in pairs)
            {
                if (old == next || inventory.Contains(old) || Recorded(state, next) || state.Forfeited.Contains(old)) continue;
                if (!Recorded(state, old) && !RecoverFromReceipts(state, old)) continue;
                if (state.Levels.Remove(old, out var level)) state.Levels[next] = level;
                if (state.Paths.Remove(old, out var path)) state.Paths[next] = path;
                if (state.Tracks.Remove(old, out var tracks)) state.Tracks[next] = tracks;
                if (state.EvolvedItems.Remove(old)) state.EvolvedItems.Add(next);
                if (state.ThirdStarItems.Remove(old)) state.ThirdStarItems.Add(next);
                if (state.Inserts.Remove(old, out var inserts))
                    state.Inserts[next] = new InsertRecord { Ids = inserts.Ids.Select(id => map.GetValueOrDefault(id, id)).ToHashSet(),
                        PaidTemplates = inserts.PaidTemplates };
                if (state.Coverage.Remove(old, out var coverage)) state.Coverage[next] = coverage;
                if (state.Transcend.Remove(old, out var transcend)) state.Transcend[next] = transcend;
                moved++;
            }
            // Added max durability stays on the mailed copy (insurance return, flea expiry).
            foreach (var (old, next) in pairs)
            {
                if (old == next || inventory.Contains(old) || state.Grants.ContainsKey(next) || !state.Grants.TryGetValue(old, out var grant)) continue;
                RemoveGrant(state, old);
                SetGrant(state, next, new DurabilityGrant { Amount = grant.Amount, ArmorId = map.GetValueOrDefault(grant.ArmorId, grant.ArmorId) });
            }
            return moved;
        }
    }

    // Before SPT replaces the PMC inventory at raid end: clone every recorded item's
    // slot tree (largest first, so armor comes before a plate registered inside it).
    public RaidCapture? BeginRaidEnd(MongoId session)
    {
        lock (sync)
        {
            var saved = backend.Profile(session);
            var items = saved?.CharacterData?.PmcData?.Inventory?.Items;
            if (saved?.ExtensionData == null || !saved.ExtensionData.ContainsKey(StateKey) || items == null) return null;
            var state = State(session);
            var ids = items.Select(i => i.Id.ToString()).ToHashSet();
            var capture = new RaidCapture { InsuranceCount = saved.InsuranceList?.Count ?? 0 };
            foreach (var root in state.Tracks.Keys.Concat(state.EvolvedItems).Concat(state.ThirdStarItems).Where(ids.Contains).Distinct())
                capture.Gear.Add((root, Clone(SlotTree(items, root))));
            capture.Gear.Sort((a, b) => b.Tree.Count.CompareTo(a.Tree.Count));
            capture.Granted.UnionWith(state.Grants.Keys.Where(ids.Contains));
            return capture.Gear.Count == 0 && capture.Granted.Count == 0 ? null : capture;
        }
    }

    // After it: recorded gear that is gone was lost in the raid. Enhancement insurance
    // rolls its tier's chance to mail it back enhanced (then traders do not return a
    // second copy). Otherwise the enhancement is forfeited; a trader may return it as +0.
    public (int Returned, int Forfeited) CompleteRaidEnd(MongoId session, RaidCapture capture, PmcData? postRaid)
    {
        lock (sync)
        {
            var saved = backend.Profile(session);
            var items = saved?.CharacterData?.PmcData?.Inventory?.Items;
            if (items == null) return (0, 0);
            var state = State(session);
            var owned = items.Select(i => i.Id.ToString()).ToHashSet();
            var after = postRaid?.Inventory?.Items;
            var handled = new HashSet<string>();
            var (returned, forfeited) = (0, 0);
            foreach (var (root, before) in capture.Gear)
            {
                if (owned.Contains(root) || handled.Contains(root) || !Recorded(state, root)) continue;
                state.Coverage.Remove(root, out var coverage);
                // At-death state from the client when it carried the gear, else the pre-raid copy.
                var tree = after?.Any(i => i.Id.ToString() == root) == true ? Clone(SlotTree(after, root)) : Clone(before);
                tree = tree.Where(i => !owned.Contains(i.Id.ToString())).ToList();
                if (coverage != null && tree.Count > 0 && EnhanceRules.Succeeds(coverage.ReturnPercent, backend.Draw()))
                {
                    var ids = tree.Select(i => i.Id.ToString()).ToHashSet();
                    // Mail first: if sending fails, the trader package and records stay as they were.
                    backend.SendReturn(session, tree, $"강화 보험({Loc.TierName(coverage.Tier, false)} {coverage.ReturnPercent:0.##}%): 레이드에서 잃은 장비를 강화된 상태로 되찾았습니다. "
                        + $"Enhancement insurance ({Loc.TierName(coverage.Tier, true)} {coverage.ReturnPercent:0.##}%): the gear you lost in a raid was returned still enhanced.");
                    StripTraderInsurance(saved!, capture.InsuranceCount, ids);
                    handled.UnionWith(ids);
                    returned++;
                }
                else
                {
                    StripGrants(saved!, capture.InsuranceCount, tree.Select(i => i.Id.ToString()), state);
                    Forget(state, root);
                    state.Forfeited.Add(root);
                    forfeited++;
                }
            }
            // Granted parts lost outside recorded gear (a plate moved into other armor).
            StripGrants(saved!, capture.InsuranceCount, capture.Granted.Where(id => !owned.Contains(id) && !handled.Contains(id)), state);
            // Plates moved between armor during the raid: the client reloads the profile after
            // the raid, so the item values changed here reach it.
            var pmc = saved!.CharacterData?.PmcData;
            if (pmc != null) ReconcileInserts(pmc, state, state.LastSettings ?? new EnhanceSettings(), state.AutoRegisterInserts, new List<Item>());
            return (returned, forfeited);
        }
    }

    private static void StripTraderInsurance(SptProfile saved, int from, HashSet<string> ids)
    {
        var list = saved.InsuranceList;
        if (list == null) return;
        for (var index = list.Count - 1; index >= Math.Max(0, from); index--)
        {
            list[index]?.Items?.RemoveAll(i => ids.Contains(i.Id.ToString()));
            if (list[index]?.Items is { Count: 0 }) list.RemoveAt(index);
        }
    }

    private List<Item> Clone(List<Item> items) => serializer.Deserialize<List<Item>>(serializer.Serialize(items)!) ?? new();

    // 1.0.0 pruned the records of insured or listed gear whenever the window opened.
    // Its receipts (the last 100 attempts) survive; rebuild what they prove for that
    // exact id. A downgrade to 0 leaves no track; a destroyed item never comes back.
    internal static bool RecoverFromReceipts(EnhanceState state, string id)
    {
        var receipts = state.Receipts.Where(r => r?.ItemId == id && r.Action <= EnhanceAction.EvolveThird).ToList();
        if (receipts.Count == 0 || receipts[^1] is { Success: false, FailureMode: FailureMode.Destroy }) return false;
        var tracks = new Dictionary<EnhancePath, int>();
        foreach (var receipt in receipts)
            if (Enum.IsDefined(receipt.Path) && receipt.Level >= 0 && receipt.Level <= 100) tracks[receipt.Path] = receipt.Level;
        foreach (var path in tracks.Where(t => t.Value == 0).Select(t => t.Key).ToArray()) tracks.Remove(path);
        if (tracks.Count == 0) return false;
        state.Tracks[id] = tracks;
        var stars = StarEvolution.Level(receipts[^1].Evolved, receipts[^1].StarLevel);
        if (stars >= 2) state.EvolvedItems.Add(id);
        if (stars >= 3) state.ThirdStarItems.Add(id);
        foreach (var receipt in state.Receipts.Where(r => r?.ItemId == id && r.Action == EnhanceAction.Transcend && TranscendRules.Valid(r.Transcend)))
        {
            if (!state.Transcend.TryGetValue(id, out var record)) state.Transcend[id] = record = new();
            record.Set(receipt.Transcend, TranscendRules.Clamp(receipt.Level), Math.Max(0, receipt.Pity));
        }
        return true;
    }

    private static bool Recorded(EnhanceState state, string id) => state.Levels.ContainsKey(id) || state.Paths.ContainsKey(id)
        || state.Tracks.ContainsKey(id) || state.EvolvedItems.Contains(id) || state.ThirdStarItems.Contains(id);

    // Inventory first, then items that are away but can still come back.
    private Dictionary<string, Item> Tracked(MongoId session, PmcData profile)
    {
        var items = new Dictionary<string, Item>();
        void Add(IEnumerable<Item>? source)
        {
            foreach (var item in source ?? Enumerable.Empty<Item>()) items.TryAdd(item.Id.ToString(), item);
        }
        Add(InventoryItems(profile));
        var saved = backend.Profile(session);
        foreach (var insurance in saved?.InsuranceList ?? new()) Add(insurance?.Items);
        foreach (var offer in profile.RagfairInfo?.Offers ?? new()) Add(offer?.Items);
        foreach (var dialogue in saved?.DialogueRecords?.Values ?? Enumerable.Empty<Dialogue>())
        foreach (var message in dialogue?.Messages ?? new()) Add(message?.Items?.Data);
        return items;
    }

    private static int Stars(EnhanceState state, string id) => state.ThirdStarItems.Contains(id) ? 3 : state.EvolvedItems.Contains(id) ? 2 : 1;

    private PmcData GetPmc(MongoId session) =>
        backend.Profile(session)?.CharacterData?.PmcData
        ?? throw Loc.Invalid("PMC 프로필을 찾을 수 없습니다.", "PMC profile not found.");

    private EnhanceState State(MongoId session)
    {
        var profile = backend.Profile(session) ?? throw Loc.Invalid("프로필이 없습니다.", "Profile not found.");
        profile.ExtensionData ??= new();
        if (!profile.ExtensionData.TryGetValue(StateKey, out var raw))
            profile.ExtensionData[StateKey] = raw = new EnhanceState();
        if (raw is JsonElement json)
            profile.ExtensionData[StateKey] = raw = serializer.Deserialize<EnhanceState>(json.GetRawText())
                ?? throw Loc.Invalid("강화 기록을 읽을 수 없습니다.", "Cannot read the enhancement records.");
        if (raw is not EnhanceState state || state.Levels == null || state.Receipts == null || state.Paths == null || state.Tracks == null)
            throw Loc.Invalid("강화 기록 형식이 다릅니다.", "The enhancement record format is different.");
        state.EvolvedItems ??= new();
        state.ThirdStarItems ??= new();
        state.Inserts ??= new();
        state.Coverage ??= new();
        state.Forfeited ??= new();
        state.Transcend ??= new();
        state.Grants ??= new();
        foreach (var (id, grant) in state.Grants) GrantIndex.Set(id, grant?.Amount ?? 0);
        // Preserve the paid specialization from 0.2 as one independent track.
        // Level-only 0.1 records wait for their existing free first selection.
        foreach (var entry in state.Paths.ToArray())
        {
            var level = state.Levels.GetValueOrDefault(entry.Key);
            if (level < 1 || level > 100 || !Enum.IsDefined(entry.Value))
                throw Loc.Invalid("이전 강화 기록이 올바르지 않습니다.", "The old enhancement records are invalid.");
            if (!state.Tracks.TryGetValue(entry.Key, out var tracks))
                state.Tracks[entry.Key] = tracks = new();
            if (!tracks.ContainsKey(entry.Value)) tracks[entry.Value] = level;
            state.Levels.Remove(entry.Key);
            state.Paths.Remove(entry.Key);
        }
        return state;
    }

    private Item GetTarget(PmcData profile, string itemId)
    {
        var target = InventoryItems(profile).SingleOrDefault(i => i.Id.ToString() == itemId)
            ?? throw Loc.Invalid("내 창고에서 아이템을 찾을 수 없습니다.", "Item not found in your stash.");
        var parents = Parents(profile);
        if (!EnhanceRules.IsBelow(itemId, Stash(profile), parents))
            throw Loc.Invalid("장비를 장착 해제하고 창고에 넣어 주세요.", "Unequip the gear and put it in your stash.");
        var tree = Tree(profile, itemId);
        if (tree.Any(i => i.Id != target.Id && i.SlotId == "main"))
            throw Loc.Invalid("장비의 내부 보관칸을 비운 뒤 강화해 주세요.", "Empty the gear's storage before enhancing.");
        if (!backend.Eligible(target.Template)
            || !tree.Any(i => i.Upd?.Repairable is { MaxDurability: > 0 }))
            throw Loc.Invalid("내구도가 있는 무기 또는 방어구만 강화할 수 있습니다.", "Only weapons or armor with durability can be enhanced.");
        if (backend.IsArmorPart(target.Template))
            throw Loc.Invalid("방탄판·내피·헬멧 부착 방호구는 따로 강화할 수 없습니다. 방어구에 끼운 채 방어구를 강화하면 함께 적용됩니다.",
                "Plates, soft inserts and helmet add-ons cannot be enhanced alone. Enhance the armor they are inserted in; they share its effect.");
        if (tree.Any(i => i.Upd?.PinLockState?.ToString() == "Locked"))
            throw Loc.Invalid("잠긴 장비나 부착물의 잠금을 먼저 해제해 주세요.", "Unlock the locked gear or attachments first.");
        return target;
    }

    private static List<Item> InventoryItems(PmcData profile) => profile.Inventory?.Items
        ?? throw Loc.Invalid("아이템 목록을 읽을 수 없습니다.", "Cannot read the item list.");

    private static string Stash(PmcData profile) => profile.Inventory?.Stash?.ToString()
        ?? throw Loc.Invalid("창고를 찾을 수 없습니다.", "Stash not found.");

    private static Dictionary<string, string> Parents(PmcData profile) => InventoryItems(profile)
        .ToDictionary(i => i.Id.ToString(), i => i.ParentId?.ToString() ?? "");

    private static List<Item> Tree(PmcData profile, string root)
    {
        var parents = Parents(profile);
        return InventoryItems(profile).Where(i => EnhanceRules.IsBelow(i.Id.ToString(), root, parents)).ToList();
    }

    private static IEnumerable<Item> Money(PmcData profile) => Materials(profile, Roubles);

    private static IEnumerable<Item> Materials(PmcData profile, string template)
    {
        var parents = Parents(profile);
        // FIR/SpawnedInSession is intentionally irrelevant for enhancement costs.
        return InventoryItems(profile).Where(i => i.Template.ToString() == template
            && EnhanceRules.IsBelow(i.Id.ToString(), Stash(profile), parents)
            && i.Upd?.PinLockState?.ToString() != "Locked" && Count(i) > 0);
    }

    private static long Count(Item item)
    {
        var count = item.Upd?.StackObjectsCount ?? 1;
        if (!double.IsFinite(count) || count < 0 || count > int.MaxValue || count != Math.Floor(count))
            throw Loc.Invalid("소모 아이템 묶음의 수량이 올바르지 않습니다.", "Invalid stack count on a consumed item.");
        return (long)count;
    }

    private string Fingerprint(List<Item> tree)
    {
        var text = string.Join("|", tree.OrderBy(i => i.Id.ToString()).Select(i =>
            $"{i.Id}:{i.Template}:{i.ParentId}:{i.SlotId}:{serializer.Serialize(i.Upd)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string TracksFingerprint(Dictionary<EnhancePath, int>? tracks) => tracks == null ? ""
        : string.Join("|", tracks.OrderBy(t => t.Key).Select(t => $"{(int)t.Key}:{t.Value}"));
}
