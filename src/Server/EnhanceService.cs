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
    public HashSet<string> EvolvedItems { get; set; } = new();
}

// The query router and item-event router are resolved separately by SPT.
// Quotes and their lock must belong to the same server-wide instance.
[Injectable(InjectionType.Singleton)]
public sealed class EnhanceService(SptEnhanceBackend backend, JsonUtil serializer)
{
    private const string StateKey = "rfGearEnhance";
    private const string Roubles = "5449016a4bdc2d6f028b456f";
    private readonly object sync = new();
    private readonly Dictionary<MongoId, PendingQuote> pending = new();

    private sealed record PendingQuote(EnhanceQuote View, string Fingerprint, string TrackFingerprint, bool Evolved);

    public EnhanceQuote Quote(MongoId session, string itemId, EnhanceSettings settings, EnhancePath? path = null,
        EnhanceProtection? protection = null, EnhanceAction action = EnhanceAction.Upgrade)
    {
        lock (sync)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            if (!Enum.IsDefined(action)) throw new ArgumentException("강화 작업이 올바르지 않습니다.");
            var profile = GetPmc(session);
            var target = GetTarget(profile, itemId);
            var state = State(session);
            // Discard metadata only for items no longer owned by this profile.
            var owned = InventoryItems(profile).Select(i => i.Id.ToString()).ToHashSet();
            foreach (var key in state.Levels.Keys.Concat(state.Tracks.Keys).Where(k => !owned.Contains(k)).Distinct().ToArray())
            {
                state.Levels.Remove(key);
                state.Paths.Remove(key);
                state.Tracks.Remove(key);
                state.EvolvedItems.Remove(key);
            }
            var weapon = backend.IsWeapon(target.Template);
            var tracks = state.Tracks.GetValueOrDefault(itemId);
            var selected = path ?? (tracks?.Count > 0 ? tracks.Keys.First()
                : weapon ? EnhancePath.Recoil : EnhancePath.ArmorDurability);
            if (!EnhanceEffects.Allowed(selected, weapon))
                throw new ArgumentException("이 장비에 사용할 수 없는 강화 항목입니다.");
            var legacy = (tracks == null || tracks.Count == 0) && state.Levels.GetValueOrDefault(itemId) > 0;
            var level = legacy ? state.Levels[itemId] : tracks?.GetValueOrDefault(selected) ?? 0;
            if (level < 0 || level > 100) throw new InvalidOperationException("강화 단계 기록이 올바르지 않습니다.");
            var evolving = action == EnhanceAction.Evolve;
            var evolved = state.EvolvedItems.Contains(itemId);
            if (evolving && (legacy || !EnhanceProgression.AllAtLeast(tracks, weapon, settings.MaxLevel)))
                throw new InvalidOperationException("모든 항목을 최대 단계까지 강화한 뒤 스타 강화 진화를 시도해 주세요.");
            if (evolving && evolved) throw new InvalidOperationException("이미 스타 강화 진화를 완료한 장비입니다.");
            var nextLevel = legacy || evolving ? level : level + 1;
            var (price, source) = backend.Price(target.Template, settings.PriceBasis);
            var tree = Tree(profile, itemId);
            var highRisk = !legacy && !evolving && weapon && settings.HighRiskEnabled
                && EnhanceProgression.AllAtLeast(tracks, true, EnhanceProgression.HalfLevel(settings.MaxLevel));
            var nextTracks = tracks == null ? new Dictionary<EnhancePath, int>() : new(tracks);
            nextTracks[selected] = nextLevel;
            var complete = EnhanceProgression.AllAtLeast(tracks, weapon, settings.MaxLevel);
            var completes = EnhanceProgression.AllAtLeast(nextTracks, weapon, settings.MaxLevel);
            var quote = new EnhanceQuote
            {
                QuoteId = Guid.NewGuid().ToString("N"), ItemId = itemId,
                CurrentLevel = level, NextLevel = nextLevel,
                Cost = legacy ? 0 : evolving ? EnhanceRules.EvolutionCost(price, settings) : EnhanceRules.Cost(price, level, settings),
                AvailableRoubles = Money(profile).Sum(i => Count(i)),
                SuccessPercent = legacy || settings.AlwaysSucceed ? 100 : evolving ? settings.EvolutionSuccessPercent : EnhanceProgression.Success(level, settings),
                PriceSource = source, FailureMode = legacy ? FailureMode.KeepItem : evolving ? FailureMode.Destroy : highRisk ? FailureMode.Downgrade : settings.FailureMode,
                HighRisk = highRisk, DestructionPercent = highRisk ? settings.HighRiskDestructionPercent : 0,
                AttachedItems = tree.Count - 1,
                ExpiresUnixSeconds = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds(),
                Path = selected, IsWeapon = weapon,
                BindLegacy = legacy,
                CurrentEffect = legacy ? 0 : EnhanceEffects.Amount(selected, level, settings.Effects, evolved),
                NextEffect = EnhanceEffects.Amount(selected, nextLevel, settings.Effects, evolved || evolving),
                EffectCap = EnhanceEffects.Amount(selected, settings.MaxLevel, settings.Effects, true),
                Complete = complete, CompletesOnSuccess = completes,
                NextGainMultiplier = EnhanceProgression.GainMultiplier(nextLevel, settings.MaxLevel), IsEvolution = evolving
            };
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
            // One current quote per profile; opening another item invalidates the old one.
            pending[session] = new(quote, Fingerprint(tree), TracksFingerprint(tracks), evolved);
            return quote;
        }
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
            var owned = InventoryItems(profile).ToDictionary(i => i.Id.ToString());
            return new EnhanceSnapshot
            {
                ProfileId = profile.Id.ToString() ?? "",
                Items = state.Tracks.Where(p => owned.ContainsKey(p.Key))
                    .SelectMany(p => p.Value.Where(track => track.Value > 0 && track.Value <= 100
                        && EnhanceEffects.Allowed(track.Key, backend.IsWeapon(owned[p.Key].Template)))
                        .Select(track => new EnhanceRecord { ItemId = p.Key, Path = track.Key, Level = track.Value,
                            Evolved = state.EvolvedItems.Contains(p.Key) }))
                    .ToArray()
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
                throw new InvalidOperationException("이미 처리된 강화입니다. 결과를 확인해 주세요.");
            if (!pending.TryGetValue(session, out var saved) || saved.View.QuoteId != quoteId)
                throw new InvalidOperationException("강화 견적을 다시 불러와 주세요.");
            var quote = saved.View;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= quote.ExpiresUnixSeconds)
                throw new InvalidOperationException("견적이 만료됐습니다. 비용을 다시 확인해 주세요.");
            var target = GetTarget(profile, quote.ItemId);
            var changedItems = output.ProfileChanges?[session]?.Items?.ChangedItems
                ?? throw new InvalidOperationException("SPT 아이템 응답을 준비할 수 없습니다.");
            var tree = Tree(profile, quote.ItemId);
            var recordedLevel = quote.BindLegacy ? state.Levels.GetValueOrDefault(quote.ItemId)
                : state.Tracks.GetValueOrDefault(quote.ItemId)?.GetValueOrDefault(quote.Path) ?? 0;
            if (recordedLevel != quote.CurrentLevel || Fingerprint(tree) != saved.Fingerprint
                || TracksFingerprint(state.Tracks.GetValueOrDefault(quote.ItemId)) != saved.TrackFingerprint
                || state.EvolvedItems.Contains(quote.ItemId) != saved.Evolved)
                throw new InvalidOperationException("장비 상태가 바뀌었습니다. 견적을 다시 불러와 주세요.");
            if (quote.CanDestroy && !confirmDestruction)
                throw new InvalidOperationException("장비와 부착물 파괴 안내를 확인해 주세요.");
            var purse = Money(profile).ToList();
            if (purse.Sum(i => Count(i)) < quote.Cost)
                throw new InvalidOperationException("창고의 루블이 부족합니다.");

            // All checks precede payment. Native item-event output keeps the
            // client's stacks, deleted items and durability in sync with SPT.
            var success = EnhanceRules.Succeeds(quote.SuccessPercent,
                RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue);
            var failureMode = !success ? EnhanceRules.Failure(quote,
                RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue) : quote.FailureMode;
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
            if (success && quote.IsEvolution) state.EvolvedItems.Add(quote.ItemId);
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
                foreach (var part in tree)
                {
                    state.Levels.Remove(part.Id.ToString());
                    state.Paths.Remove(part.Id.ToString());
                    state.Tracks.Remove(part.Id.ToString());
                    state.EvolvedItems.Remove(part.Id.ToString());
                }
            }
            state.Receipts.Add(new EnhanceReceipt
            {
                QuoteId = quoteId, ItemId = quote.ItemId, Success = success,
                Level = success ? quote.NextLevel : failureMode == FailureMode.Downgrade ? Math.Max(0, quote.CurrentLevel - 1) : quote.CurrentLevel,
                Paid = quote.Cost, FailureMode = failureMode, Path = quote.Path,
                DestructionPrevented = destructionPrevented, DowngradePrevented = downgradePrevented,
                IsEvolution = quote.IsEvolution, Evolved = state.EvolvedItems.Contains(quote.ItemId),
                CompletedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            if (state.Receipts.Count > 100) state.Receipts.RemoveRange(0, state.Receipts.Count - 100);
        }
    }

    private PmcData GetPmc(MongoId session) =>
        backend.Profile(session)?.CharacterData?.PmcData
        ?? throw new InvalidOperationException("PMC 프로필을 찾을 수 없습니다.");

    private EnhanceState State(MongoId session)
    {
        var profile = backend.Profile(session) ?? throw new InvalidOperationException("프로필이 없습니다.");
        profile.ExtensionData ??= new();
        if (!profile.ExtensionData.TryGetValue(StateKey, out var raw))
            profile.ExtensionData[StateKey] = raw = new EnhanceState();
        if (raw is JsonElement json)
            profile.ExtensionData[StateKey] = raw = serializer.Deserialize<EnhanceState>(json.GetRawText())
                ?? throw new InvalidOperationException("강화 기록을 읽을 수 없습니다.");
        if (raw is not EnhanceState state || state.Levels == null || state.Receipts == null || state.Paths == null || state.Tracks == null)
            throw new InvalidOperationException("강화 기록 형식이 다릅니다.");
        state.EvolvedItems ??= new();
        // Preserve the paid specialization from 0.2 as one independent track.
        // Level-only 0.1 records wait for their existing free first selection.
        foreach (var entry in state.Paths.ToArray())
        {
            var level = state.Levels.GetValueOrDefault(entry.Key);
            if (level < 1 || level > 100 || !Enum.IsDefined(entry.Value))
                throw new InvalidOperationException("이전 강화 기록이 올바르지 않습니다.");
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
            ?? throw new InvalidOperationException("내 창고에서 아이템을 찾을 수 없습니다.");
        var parents = Parents(profile);
        if (!EnhanceRules.IsBelow(itemId, Stash(profile), parents))
            throw new InvalidOperationException("장비를 장착 해제하고 창고에 넣어 주세요.");
        var tree = Tree(profile, itemId);
        if (tree.Any(i => i.Id != target.Id && i.SlotId == "main"))
            throw new InvalidOperationException("장비의 내부 보관칸을 비운 뒤 강화해 주세요.");
        if (!backend.Eligible(target.Template)
            || !tree.Any(i => i.Upd?.Repairable is { MaxDurability: > 0 }))
            throw new InvalidOperationException("내구도가 있는 무기 또는 방어구만 강화할 수 있습니다.");
        if (tree.Any(i => i.Upd?.PinLockState?.ToString() == "Locked"))
            throw new InvalidOperationException("잠긴 장비나 부착물의 잠금을 먼저 해제해 주세요.");
        return target;
    }

    private static List<Item> InventoryItems(PmcData profile) => profile.Inventory?.Items
        ?? throw new InvalidOperationException("아이템 목록을 읽을 수 없습니다.");

    private static string Stash(PmcData profile) => profile.Inventory?.Stash?.ToString()
        ?? throw new InvalidOperationException("창고를 찾을 수 없습니다.");

    private static Dictionary<string, string> Parents(PmcData profile) => InventoryItems(profile)
        .ToDictionary(i => i.Id.ToString(), i => i.ParentId?.ToString() ?? "");

    private static List<Item> Tree(PmcData profile, string root)
    {
        var parents = Parents(profile);
        return InventoryItems(profile).Where(i => EnhanceRules.IsBelow(i.Id.ToString(), root, parents)).ToList();
    }

    private static IEnumerable<Item> Money(PmcData profile)
    {
        var parents = Parents(profile);
        return InventoryItems(profile).Where(i => i.Template.ToString() == Roubles
            && EnhanceRules.IsBelow(i.Id.ToString(), Stash(profile), parents)
            && i.Upd?.PinLockState?.ToString() != "Locked" && Count(i) > 0);
    }

    private static long Count(Item item)
    {
        var count = item.Upd?.StackObjectsCount ?? 1;
        if (!double.IsFinite(count) || count < 0 || count > int.MaxValue || count != Math.Floor(count))
            throw new InvalidOperationException("루블 묶음의 수량이 올바르지 않습니다.");
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
