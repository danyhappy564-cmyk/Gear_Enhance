using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace GearEnhance;

// What a quote asks for beyond the action itself (1.4.0).
public sealed class QuoteOptions
{
    public BoostChoice[]? Boost { get; init; }
    public bool Instant { get; init; }
    public bool UseTicket { get; init; }
    // BuyTicket: the evolution the ticket is for (2 or 3).
    public int TicketStars { get; init; }
    // Reroll: which added zone (0 or 1).
    public int Slot { get; init; }
}

// 1.4.0: a gear that passed ???. Armor keeps the zones it gained and the zones it could gain,
// fixed at that moment so swapping attachments later cannot change the reroll options.
public sealed class MythicRecord
{
    public int Level { get; set; }
    public double Pity { get; set; }
    public List<string> Zones { get; set; } = new();
    public List<string> Candidates { get; set; } = new();
    public int Rerolls { get; set; }
}

// Destruction tickets of one gear: star evolutions (2, 3) and ??? (MythicRules.MysteryTicket).
// Bought counts are lifetime limits.
public sealed class TicketRecord
{
    public int SecondBought { get; set; }
    public int SecondHeld { get; set; }
    public int ThirdBought { get; set; }
    public int ThirdHeld { get; set; }
    public int MysteryBought { get; set; }
    public int MysteryHeld { get; set; }

    public int Bought(int stars) => stars == MythicRules.MysteryTicket ? MysteryBought : stars == 3 ? ThirdBought : stars == 2 ? SecondBought : 0;
    public int Held(int stars) => stars == MythicRules.MysteryTicket ? MysteryHeld : stars == 3 ? ThirdHeld : stars == 2 ? SecondHeld : 0;

    public void Buy(int stars)
    {
        if (stars == MythicRules.MysteryTicket) { MysteryBought++; MysteryHeld++; }
        else if (stars == 3) { ThirdBought++; ThirdHeld++; }
        else if (stars == 2) { SecondBought++; SecondHeld++; }
    }

    public bool Use(int stars)
    {
        if (Held(stars) < 1) return false;
        if (stars == MythicRules.MysteryTicket) MysteryHeld--; else if (stars == 3) ThirdHeld--; else SecondHeld--;
        return true;
    }

    // Returns how many vanished.
    public int Void(int stars)
    {
        var left = Held(stars);
        if (stars == MythicRules.MysteryTicket) MysteryHeld = 0; else if (stars == 3) ThirdHeld = 0; else if (stars == 2) SecondHeld = 0;
        return left;
    }

    public bool IsEmpty() => SecondBought == 0 && SecondHeld == 0 && ThirdBought == 0 && ThirdHeld == 0 && MysteryBought == 0 && MysteryHeld == 0;
}

// A successful enhancement insurance return waiting for its tier's delay. The tree is the
// gear as lost (old ids); the mail hook moves its records to the new ids when it is sent.
public sealed class PendingReturn
{
    public string Root { get; set; } = "";
    public string Template { get; set; } = "";
    public bool IsWeapon { get; set; }
    public List<Item> Tree { get; set; } = new();
    public InsuranceTier Tier { get; set; }
    public double Percent { get; set; }
    public long StartUnixSeconds { get; set; }
    public long DueUnixSeconds { get; set; }
}

public sealed partial class EnhanceService
{
    // Gear-wide facts every quote carries for the window header and tabs.
    private EnhanceQuote Header(EnhanceQuote quote, EnhanceState state, EnhanceSettings settings)
    {
        var id = quote.ItemId;
        var coverage = state.Coverage.GetValueOrDefault(id);
        quote.CurrentInsurance = coverage?.Tier ?? InsuranceTier.None;
        quote.CurrentInsurancePercent = coverage?.ReturnPercent ?? 0;
        var transcend = state.Transcend.GetValueOrDefault(id);
        quote.TranscendSecond = transcend?.Second ?? 0;
        quote.TranscendThird = transcend?.Third ?? 0;
        foreach (var tier in new[] { InsuranceTier.Basic, InsuranceTier.Advanced, InsuranceTier.Premium })
        {
            quote.InsuranceHours[(int)tier] = settings.ReturnHours(tier);
            if (quote.InsuranceChances[(int)tier] == 0) quote.InsuranceChances[(int)tier] = settings.ReturnPercent(tier);
        }
        // The ticket line belongs to the next risky step: ★★ tickets before ★★, ★★★ tickets before
        // ★★★, ??? tickets on ★★★ gear that has not passed ??? yet.
        var stars = Stars(state, id);
        var mythic = state.Mythic.GetValueOrDefault(id);
        if (quote.Action != EnhanceAction.BuyTicket) quote.TicketStars = stars < 3 ? stars + 1 : mythic == null ? MythicRules.MysteryTicket : 0;
        var tickets = state.Tickets.GetValueOrDefault(id);
        quote.TicketsHeld = tickets?.Held(quote.TicketStars) ?? 0;
        quote.TicketsBought = tickets?.Bought(quote.TicketStars) ?? 0;
        quote.TicketMax = settings.TicketMax(quote.TicketStars);
        quote.TicketPrice = settings.TicketPrice(quote.TicketStars);
        quote.MythicLevel = mythic == null ? -1 : MythicRules.Clamp(mythic.Level);
        quote.Zones = mythic?.Zones.ToArray() ?? Array.Empty<string>();
        quote.Rerolls = mythic?.Rerolls ?? 0;
        return quote;
    }

    private static TicketRecord TicketsOf(EnhanceState state, string id)
    {
        if (!state.Tickets.TryGetValue(id, out var record)) state.Tickets[id] = record = new TicketRecord();
        return record;
    }

    // Chance materials (design 2): the client sends kinds and counts only; caps, the two-kind
    // limit and the price are the server's. Stock is counted again at execution.
    private void ApplyBoost(EnhanceQuote quote, PmcData profile, BoostChoice[]? choices, BoostTuning tuning)
    {
        // Stock of every kind, minus what the attempt's own materials already take.
        long Usable(BoostMaterial kind)
        {
            var templates = SuccessBoost.Templates(kind);
            var reserved = quote.Materials?.Where(m => templates.Contains(m.Template)).Sum(m => (long)m.Count) ?? 0;
            return Math.Max(0, Materials(profile, templates).Sum(Count) - reserved);
        }
        if (quote.BoostAvailable) foreach (var kind in SuccessBoost.Kinds) quote.BoostOwned[(int)kind] = Usable(kind);
        var chosen = (choices ?? Array.Empty<BoostChoice>()).Where(c => c != null && c.Count != 0).ToArray();
        if (chosen.Length == 0) return;
        if (quote.Action == EnhanceAction.Mystery)
            throw Loc.Invalid("??? 는 50/50 고정이라 성공률 재료를 쓸 수 없습니다.", "??? is a fixed 50/50; chance materials cannot be used.");
        // A guaranteed attempt (or one that is not rolled) buys nothing, like 1.3.0.
        if (!quote.BoostAvailable) return;
        if (chosen.Length > tuning.MaxKinds)
            throw Loc.Invalid($"성공률 재료는 {tuning.MaxKinds}종까지 고를 수 있습니다.", $"Choose at most {tuning.MaxKinds} kinds of chance material.");
        if (chosen.Any(c => !SuccessBoost.Valid(c.Kind)) || chosen.Select(c => c.Kind).Distinct().Count() != chosen.Length)
            throw Loc.Argument("성공률 재료 종류가 올바르지 않습니다.", "Invalid chance material kind.");
        var lines = new List<BoostLine>();
        long roubles = 0;
        foreach (var choice in chosen)
        {
            var max = SuccessBoost.MaxCount(choice.Kind, tuning);
            if (choice.Count < 1 || choice.Count > max)
                throw Loc.Invalid($"성공률 재료 개수가 올바르지 않습니다(최대 {max}개).", $"Invalid chance material count (at most {max}).");
            var line = new BoostLine
            {
                Kind = choice.Kind, Count = choice.Count, MaxCount = max, Points = SuccessBoost.Points(choice.Kind, choice.Count, tuning),
                Roubles = (long)choice.Count * SuccessBoost.RoublesEach(choice.Kind, tuning), Owned = quote.BoostOwned[(int)choice.Kind]
            };
            roubles += line.Roubles;
            lines.Add(line);
        }
        if (roubles > 100000000)
            throw Loc.Invalid("성공률 재료 비용이 1억 루블을 초과합니다.", "The chance material cost exceeds 100,000,000 roubles.");
        quote.Boost = lines.ToArray();
        quote.BoostCost = (int)roubles;
        quote.SuccessPercent = Math.Min(100, Math.Round(quote.BaseSuccessPercent + lines.Sum(l => l.Points), 4));
    }

    private static void ApplyInstant(EnhanceQuote quote, bool instant, EnhanceSettings settings)
    {
        if (!instant) return;
        if (!quote.InstantAvailable)
            throw Loc.Invalid("즉시 강화는 일반 강화·초월·신화에서만 쓸 수 있습니다.", "Instant is only for enhancement, transcendence and mythic attempts.");
        quote.Instant = true;
        quote.InstantFee = settings.InstantFee;
        quote.Cost = ProtectionCosts.Total(quote.Cost, quote.InstantFee, 0);
    }

    // ??? (Mystery), the mythic track (Mythic) and the armor zone reroll (Reroll).
    private EnhanceQuote MythicQuote(MongoId session, PmcData profile, Item target, EnhanceState state, EnhanceSettings settings,
        EnhanceAction action, bool weapon, List<Item> parts, string source, double partsPrice, double paidParts,
        EnhanceProtection? protection, QuoteOptions options)
    {
        var itemId = target.Id.ToString();
        var tracks = state.Tracks.GetValueOrDefault(itemId);
        var stars = Stars(state, itemId);
        var tuning = settings.Mythic;
        var record = state.Mythic.GetValueOrDefault(itemId);
        if (tracks == null || tracks.Count == 0 || stars < 3)
            throw Loc.Invalid("3차 스타 진화(★★★)를 마친 장비만 ??? 에 도전할 수 있습니다.", "Only gear with the 3rd star evolution (★★★) can take on ???.");
        var tree = Tree(profile, itemId);
        var quote = new EnhanceQuote
        {
            QuoteId = Guid.NewGuid().ToString("N"), ItemId = itemId, Action = action, IsWeapon = weapon,
            // Execute re-checks the mythic level; Path only names one of the gear's tracks.
            Path = tracks.Keys.First(), CurrentStars = stars, NextStars = stars, PriceSource = source,
            AttachedItems = tree.Count - 1, ExpiresUnixSeconds = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds(),
            AvailableRoubles = Money(profile).Sum(i => Count(i)), Complete = true, CompletesOnSuccess = true,
            PartsPrice = partsPrice, InsertedParts = parts.Count, UnregisteredParts = Unregistered(state, itemId, parts),
            FailureMode = FailureMode.KeepItem
        };
        if (action == EnhanceAction.Mystery)
        {
            if (record != null) throw Loc.Invalid("이미 신화 단계인 장비입니다.", "This gear is already mythic.");
            if (options.Boost?.Any(b => b != null && b.Count != 0) == true || options.Instant)
                throw Loc.Invalid("??? 는 50/50 고정이라 재료·즉시 강화를 쓸 수 없습니다.", "??? is a fixed 50/50; materials and instant cannot be used.");
            quote.SuccessPercent = settings.AlwaysSucceed ? 100 : tuning.MysteryChance;
            quote.FailureMode = FailureMode.Destroy;
            // The ??? ticket (one per gear) keeps the ★★★ gear on a failure; the chance stays.
            if (options.UseTicket)
            {
                if (TicketsOf(state, itemId).Held(MythicRules.MysteryTicket) < 1)
                    throw Loc.Invalid("사용할 ??? 파괴 방지권이 없습니다. 견적을 다시 불러와 주세요.", "No ??? destruction ticket to use. Reload the quote.");
                quote.UseTicket = true;
                if (quote.SuccessPercent < 100) quote.FailureMode = FailureMode.KeepItem;
            }
            quote.Cost = tuning.MysteryCost;
            quote.Materials = Need(profile, MythicRules.Ledx, tuning.RequireMaterials ? tuning.MysteryLedx : 0);
            quote.ZoneOptions = weapon ? Array.Empty<string>() : MythicRules.Candidates(backend.IsHeadArmor(target.Template), Covered(profile, itemId)).ToArray();
            quote.NextEffect = weapon ? 0 : Math.Min(tuning.ExtraZones, quote.ZoneOptions.Length);
        }
        else if (action == EnhanceAction.Mythic)
        {
            if (record == null) throw Loc.Invalid("??? 에 성공한 신화 장비만 신화 강화를 할 수 있습니다.", "Only mythic gear (??? passed) can take mythic attempts.");
            var level = record.Level;
            if (level < 0 || level > MythicRules.MaxLevel) throw Loc.Invalid("신화 단계 기록이 올바르지 않습니다.", "The saved mythic level is invalid.");
            if (level >= MythicRules.MaxLevel) throw Loc.Invalid("이미 최대 신화 단계입니다.", "Already at the maximum mythic level.");
            var pity = Math.Clamp(record.Pity, 0, tuning.PityCap);
            quote.CurrentLevel = level;
            quote.NextLevel = level + 1;
            quote.SuccessPercent = settings.AlwaysSucceed ? 100 : Math.Min(100, MythicRules.Success(level, tuning) + pity);
            (quote.PityPercent, quote.PityStep, quote.PityCap) = (pity, tuning.PityStep, tuning.PityCap);
            quote.FailureMode = MythicRules.DropsOnFailure(quote.NextLevel) ? FailureMode.Downgrade : FailureMode.KeepItem;
            quote.Cost = MythicRules.Cost(level, tuning);
            quote.Materials = Need(profile, MythicRules.Ledx, tuning.RequireMaterials ? tuning.Ledx : 0);
            double Effect(int at) => weapon ? MythicEffects.WeaponPercent(at, tuning) : MythicEffects.ArmorPercent(at, tuning);
            (quote.CurrentEffect, quote.NextEffect, quote.EffectCap) = (Effect(level), Effect(level + 1), Effect(MythicRules.MaxLevel));
        }
        else
        {
            if (weapon || record == null || record.Zones.Count == 0)
                throw Loc.Invalid("추가 보호 부위가 있는 신화 방어구만 다시 뽑을 수 있습니다.", "Only mythic armor with added zones can reroll them.");
            if (options.Slot < 0 || options.Slot >= record.Zones.Count)
                throw Loc.Argument("다시 뽑을 부위를 골라 주세요.", "Choose the zone to reroll.");
            quote.ZoneOptions = record.Candidates.Where(zone => !record.Zones.Contains(zone)).ToArray();
            if (quote.ZoneOptions.Length == 0)
                throw Loc.Invalid("다시 뽑을 수 있는 부위가 남아 있지 않습니다.", "No zone is left to reroll into.");
            quote.Slot = options.Slot;
            quote.CurrentLevel = quote.NextLevel = record.Level;
            quote.SuccessPercent = 100;
            quote.Cost = MythicRules.RerollCost(record.Rerolls, tuning);
            quote.Materials = Need(profile, SuccessBoost.GraphicsCardTemplate, tuning.RequireMaterials ? tuning.RerollGraphicsCards : 0);
        }
        quote.BaseSuccessPercent = quote.SuccessPercent;
        quote.BaseCost = quote.Cost;
        ApplyBoost(quote, profile, options.Boost, settings.Boost);
        // Nothing here is protected from destruction; only the mythic drop from 4 and 5 can be.
        quote.Protection = new() { Downgrade = protection?.Downgrade == true && quote.DowngradeProtectionAvailable };
        quote.DowngradeProtectionFee = quote.DowngradeProtectionAvailable ? MythicRules.ProtectionFee(quote.BaseCost, tuning) : 0;
        quote.Cost = ProtectionCosts.Total(quote.BaseCost, 0, quote.Protection.Downgrade ? quote.DowngradeProtectionFee : 0);
        quote.Cost = ProtectionCosts.Total(quote.Cost, quote.BoostCost, 0);
        if (!weapon && action != EnhanceAction.Reroll)
        {
            var transcend = state.Transcend.GetValueOrDefault(itemId);
            quote.InsertSurcharge = EnhanceRules.InsertSurcharge(Owed(state, itemId, parts, partsPrice, paidParts), tracks, stars, settings,
                transcend?.Second ?? 0, transcend?.Third ?? 0);
            quote.Cost = ProtectionCosts.Total(quote.Cost, quote.InsertSurcharge, 0);
        }
        ApplyInstant(quote, options.Instant, settings);
        pending[session] = new(quote, Fingerprint(tree), StateFingerprint(state, itemId), stars, settings.PriceBasis, settings.Transcend, tuning);
        return quote;
    }

    // One destruction ticket for the evolution to TicketStars, up to the gear's lifetime limit.
    private EnhanceQuote TicketQuote(MongoId session, PmcData profile, string itemId, EnhanceState state, EnhanceSettings settings,
        bool weapon, string source, QuoteOptions options)
    {
        var tracks = state.Tracks.GetValueOrDefault(itemId);
        if (tracks == null || !tracks.Values.Any(level => level > 0))
            throw Loc.Invalid("강화된 장비만 파괴 방지권을 살 수 있습니다.", "Only enhanced gear can buy destruction tickets.");
        var ticketStars = options.TicketStars;
        if (ticketStars != 2 && ticketStars != 3 && ticketStars != MythicRules.MysteryTicket)
            throw Loc.Argument("방지권 종류(★★·★★★·???)를 골라 주세요.", "Choose the ticket (★★, ★★★ or ???).");
        var stars = Stars(state, itemId);
        if (ticketStars == MythicRules.MysteryTicket)
        {
            if (stars < 3 || state.Mythic.ContainsKey(itemId))
                throw Loc.Invalid("??? 에 도전하기 전의 ★★★ 장비만 ??? 파괴 방지권을 살 수 있습니다.", "Only ★★★ gear that has not passed ??? can buy the ??? ticket.");
        }
        else if (stars != ticketStars - 1)
            throw ticketStars == 3
                ? Loc.Invalid("★★ 장비만 ★★★ 진화용 방지권을 살 수 있습니다.", "Only ★★ gear can buy ★★★ evolution tickets.")
                : Loc.Invalid("★★ 진화 전 장비만 ★★ 진화용 방지권을 살 수 있습니다.", "Only gear before the ★★ evolution can buy ★★ evolution tickets.");
        var bought = state.Tickets.GetValueOrDefault(itemId)?.Bought(ticketStars) ?? 0;
        var max = settings.TicketMax(ticketStars);
        if (bought >= max)
            throw Loc.Invalid($"이 장비는 방지권을 더 살 수 없습니다(평생 {max}장).", $"This gear cannot buy more tickets (lifetime {max}).");
        var quote = new EnhanceQuote
        {
            QuoteId = Guid.NewGuid().ToString("N"), ItemId = itemId, Action = EnhanceAction.BuyTicket, IsWeapon = weapon,
            Path = tracks.Keys.First(), CurrentLevel = tracks.First().Value, NextLevel = tracks.First().Value,
            SuccessPercent = 100, BaseSuccessPercent = 100, FailureMode = FailureMode.KeepItem, PriceSource = source,
            AvailableRoubles = Money(profile).Sum(i => Count(i)), CurrentStars = stars, NextStars = stars, TicketStars = ticketStars,
            ExpiresUnixSeconds = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds(), AttachedItems = Tree(profile, itemId).Count - 1,
            Cost = settings.TicketPrice(ticketStars)
        };
        quote.BaseCost = quote.Cost;
        ApplyInstant(quote, options.Instant, settings);
        pending[session] = new(quote, Fingerprint(Tree(profile, itemId)), StateFingerprint(state, itemId), stars, settings.PriceBasis, settings.Transcend);
        return quote;
    }

    private TranscendMaterial[] Need(PmcData profile, string template, int count) => count <= 0 ? Array.Empty<TranscendMaterial>()
        : new[] { new TranscendMaterial { Template = template, Count = count, Owned = Materials(profile, template).Sum(Count) } };

    // Zones the gear and the parts in its slots already protect (soft inserts, plates, helmet add-ons).
    private HashSet<string> Covered(PmcData profile, string itemId) =>
        SlotTree(InventoryItems(profile), itemId).SelectMany(i => backend.ArmorZones(i.Template)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static double ApplyMythic(EnhanceState state, EnhanceQuote quote, bool success, bool drop)
    {
        var record = state.Mythic[quote.ItemId];
        record.Pity = success ? 0 : Math.Min(quote.PityCap, Math.Max(0, record.Pity) + quote.PityStep);
        record.Level = success ? quote.NextLevel : drop ? Math.Max(0, quote.CurrentLevel - 1) : quote.CurrentLevel;
        return record.Pity;
    }

    // `count` distinct zones from the options, each drawn with the server's RNG.
    private List<string> DrawZones(IReadOnlyList<string>? options, int count)
    {
        var pool = (options ?? Array.Empty<string>()).Distinct().ToList();
        var picked = new List<string>();
        for (var n = 0; n < count && pool.Count > 0; n++)
        {
            var index = Math.Min(pool.Count - 1, (int)(backend.Draw() * pool.Count));
            picked.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return picked;
    }

    // ???, the held mythic levels and the LEDX they took; part of the insurance value.
    private double MythicValue(MythicRecord? record, MythicTuning tuning)
    {
        if (record == null) return 0;
        var total = MythicRules.Value(record.Level, tuning);
        var ledx = backend.Price(new MongoId(MythicRules.Ledx), PriceBasis.Handbook).Price;
        if (double.IsFinite(ledx) && ledx > 0) total += ledx * MythicRules.LedxSpent(record.Level, tuning);
        return total;
    }

    private static string ReturnText(InsuranceTier tier, double percent) =>
        $"강화 보험({Loc.TierName(tier, false)} {percent:0.##}%): 레이드에서 잃은 장비를 강화된 상태로 되찾았습니다. "
        + $"Enhancement insurance ({Loc.TierName(tier, true)} {percent:0.##}%): the gear you lost in a raid was returned still enhanced.";

    private void Delay(EnhanceState state, string root, List<Item> tree, InsuranceCoverage coverage)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var template = tree.FirstOrDefault(i => i.Id.ToString() == root)?.Template ?? tree[0].Template;
        state.Returns.Add(new PendingReturn
        {
            Root = root, Template = template.ToString(), IsWeapon = backend.IsWeapon(template), Tree = tree, Tier = coverage.Tier,
            Percent = coverage.ReturnPercent, StartUnixSeconds = now, DueUnixSeconds = now + (long)Math.Ceiling(coverage.ReturnHours * 3600)
        });
    }

    // Mails every delayed return whose time has come. Runs on the mod's own requests (state
    // sync, quotes), so a return arrives at the first of them after its due time.
    private int DeliverDue(MongoId session, EnhanceState state)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sent = 0;
        foreach (var waiting in state.Returns.Where(r => r.DueUnixSeconds <= now).ToList())
        {
            // A failed send stays queued for the next request; the request itself goes on.
            try { backend.SendReturn(session, waiting.Tree, ReturnText(waiting.Tier, waiting.Percent)); }
            catch (Exception) { break; }
            state.Returns.Remove(waiting);
            sent++;
        }
        return sent;
    }
}
