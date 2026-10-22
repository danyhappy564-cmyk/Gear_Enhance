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
    public Dictionary<string, EnhancePath> Paths { get; set; } = new();
}

[Injectable]
public sealed class EnhanceService(SptEnhanceBackend backend, JsonUtil serializer)
{
    private const string StateKey = "rfGearEnhance";
    private const string Roubles = "5449016a4bdc2d6f028b456f";
    private readonly object sync = new();
    private readonly Dictionary<MongoId, PendingQuote> pending = new();

    private sealed record PendingQuote(EnhanceQuote View, string Fingerprint);

    public EnhanceQuote Quote(MongoId session, string itemId, EnhanceSettings settings, EnhancePath? path = null)
    {
        lock (sync)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            var profile = GetPmc(session);
            var target = GetTarget(profile, itemId);
            var state = State(session);
            // Discard metadata only for items no longer owned by this profile.
            var owned = InventoryItems(profile).Select(i => i.Id.ToString()).ToHashSet();
            foreach (var key in state.Levels.Keys.Where(k => !owned.Contains(k)).ToArray())
            {
                state.Levels.Remove(key);
                state.Paths.Remove(key);
            }
            var level = state.Levels.GetValueOrDefault(itemId);
            if (level < 0 || level > 100) throw new InvalidOperationException("강화 단계 기록이 올바르지 않습니다.");
            var weapon = backend.IsWeapon(target.Template);
            var hasPath = state.Paths.TryGetValue(itemId, out var previous);
            var selected = path ?? (hasPath ? previous : weapon ? EnhancePath.Recoil : EnhancePath.ArmorDurability);
            if (!EnhanceEffects.Allowed(selected, weapon))
                throw new ArgumentException("이 장비에 사용할 수 없는 강화 방향입니다.");
            var legacy = level > 0 && !hasPath;
            var changing = level > 0 && hasPath && selected != previous;
            var nextLevel = legacy || changing ? level : level + 1;
            var (price, source) = backend.Price(target.Template, settings.PriceBasis);
            var tree = Tree(profile, itemId);
            var quote = new EnhanceQuote
            {
                QuoteId = Guid.NewGuid().ToString("N"), ItemId = itemId,
                CurrentLevel = level, NextLevel = nextLevel,
                Cost = legacy ? 0 : EnhanceRules.Cost(price, level, settings, changing),
                AvailableRoubles = Money(profile).Sum(i => Count(i)),
                SuccessPercent = legacy || changing || settings.AlwaysSucceed ? 100 : settings.SuccessPercent,
                PriceSource = source, FailureMode = legacy || changing ? FailureMode.KeepItem : settings.FailureMode,
                AttachedItems = tree.Count - 1,
                ExpiresUnixSeconds = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds(),
                Path = selected, PreviousPath = hasPath ? previous : null, IsWeapon = weapon,
                BindLegacy = legacy, ChangePath = changing,
                CurrentEffect = hasPath && previous == selected ? EnhanceEffects.Amount(selected, level, settings.Effects) : 0,
                NextEffect = EnhanceEffects.Amount(selected, nextLevel, settings.Effects),
                EffectCap = EnhanceEffects.Cap(selected, settings.Effects)
            };
            // One current quote per profile; opening another item invalidates the old one.
            pending[session] = new(quote, Fingerprint(tree));
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
                Items = state.Paths.Where(p => owned.TryGetValue(p.Key, out var item)
                    && EnhanceEffects.Allowed(p.Value, backend.IsWeapon(item.Template)))
                    .Select(p => new EnhanceRecord { ItemId = p.Key, Path = p.Value,
                        Level = Math.Clamp(state.Levels.GetValueOrDefault(p.Key), 0, 100) }).ToArray()
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
            EnhancePath? currentPath = state.Paths.TryGetValue(quote.ItemId, out var savedPath) ? savedPath : null;
            if (state.Levels.GetValueOrDefault(quote.ItemId) != quote.CurrentLevel
                || currentPath != quote.PreviousPath
                || Fingerprint(tree) != saved.Fingerprint)
                throw new InvalidOperationException("장비 상태가 바뀌었습니다. 견적을 다시 불러와 주세요.");
            if (quote.FailureMode == FailureMode.Destroy && quote.SuccessPercent < 100 && !confirmDestruction)
                throw new InvalidOperationException("장비와 부착물 파괴 안내를 확인해 주세요.");
            var purse = Money(profile).ToList();
            if (purse.Sum(i => Count(i)) < quote.Cost)
                throw new InvalidOperationException("창고의 루블이 부족합니다.");

            // All checks precede payment. Native item-event output keeps the
            // client's stacks, deleted items and durability in sync with SPT.
            var success = EnhanceRules.Succeeds(quote.SuccessPercent,
                RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue);
            pending.Remove(session);
            var remaining = quote.Cost;
            foreach (var money in purse)
            {
                if (remaining == 0) break;
                var take = (int)Math.Min(Count(money), remaining);
                backend.RemoveMoney(profile, money.Id, take, session, output);
                remaining -= take;
            }
            if (success)
            {
                state.Levels[quote.ItemId] = quote.NextLevel;
                state.Paths[quote.ItemId] = quote.Path;
            }
            else if (quote.FailureMode == FailureMode.ZeroDurability)
            {
                foreach (var part in tree.Where(i => i.Upd?.Repairable != null))
                {
                    part.Upd!.Repairable!.Durability = 0;
                    changedItems.Add(part);
                }
            }
            else if (quote.FailureMode == FailureMode.Destroy)
            {
                backend.Destroy(profile, target.Id, session, output);
                foreach (var part in tree)
                {
                    state.Levels.Remove(part.Id.ToString());
                    state.Paths.Remove(part.Id.ToString());
                }
            }
            state.Receipts.Add(new EnhanceReceipt
            {
                QuoteId = quoteId, ItemId = quote.ItemId, Success = success,
                Level = success ? quote.NextLevel : quote.CurrentLevel, Paid = quote.Cost,
                FailureMode = quote.FailureMode, Path = quote.Path,
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
        if (raw is not EnhanceState state || state.Levels == null || state.Receipts == null || state.Paths == null)
            throw new InvalidOperationException("강화 기록 형식이 다릅니다.");
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
}
