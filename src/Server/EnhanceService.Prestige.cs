using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace GearEnhance;

// 1.4.0 prestige insurance of one gear. Paid adds up; Value is what that payment covers and
// Covered the gear's records at that moment: an unpaid rise returns the gear as it was then.
public sealed class PrestigePolicy
{
    public int Paid { get; set; }
    public long Value { get; set; }
    public PrestigeCovered Covered { get; set; } = new();
}

public sealed class PrestigeCovered
{
    public Dictionary<EnhancePath, int> Tracks { get; set; } = new();
    public int Stars { get; set; } = 1;
    public TranscendRecord? Transcend { get; set; }
    public MythicRecord? Mythic { get; set; }
}

// Gear the last prestige mailed back; Root follows the mailed copy's id.
public sealed class PrestigeReturn
{
    public string Root { get; set; } = "";
    public string Template { get; set; } = "";
    public bool IsWeapon { get; set; }
    public int Level { get; set; }
}

// Insured gear SPT does not transfer by itself, mailed once the new character exists.
public sealed record PrestigeCarry(MongoId Session, int Level, List<List<Item>> Trees);

public sealed partial class EnhanceService
{
    private void FillPrestige(EnhanceQuote quote, PmcData profile, EnhanceState state, long value, EnhanceSettings settings)
    {
        var policy = state.Prestige.GetValueOrDefault(quote.ItemId);
        (quote.PrestigeLevel, quote.PrestigeMax) = PrestigeStage(profile);
        quote.PrestigeRegistered = policy != null;
        quote.PrestigePaid = policy?.Paid ?? 0;
        quote.PrestigeValue = policy?.Value ?? 0;
        quote.PrestigeFee = EnhanceRules.PrestigeFee(value, settings);
        // At the last stage there is nothing to advance to, so nothing would ever be returned.
        quote.CanBuyPrestige = quote.PrestigeLevel < quote.PrestigeMax && (policy == null || quote.PrestigeFee > policy.Paid);
    }

    private (int Level, int Max) PrestigeStage(PmcData profile) => (Math.Max(0, profile.Info?.PrestigeLevel ?? 0), backend.PrestigeMax());

    // Same value as the insurance quote: the gear, the plates paid for, levels and materials.
    private long PrestigeValue(EnhanceState state, Item item, EnhanceSettings settings)
    {
        var id = item.Id.ToString();
        var transcend = state.Transcend.GetValueOrDefault(id);
        var body = backend.Price(item.Template, settings.PriceBasis).Price;
        return EnhanceRules.Value(body + PartsValue(state.Inserts.GetValueOrDefault(id)?.PaidTemplates, settings.PriceBasis),
            state.Tracks.GetValueOrDefault(id), Stars(state, id), settings, transcend?.Second ?? 0, transcend?.Third ?? 0,
            MaterialValue(backend.IsWeapon(item.Template), transcend) + MythicValue(state.Mythic.GetValueOrDefault(id), settings.Mythic));
    }

    // Active enhancement insurance of owned gear, then lapsed ones in the stash (renewal is offered
    // only there: the window opens nothing else).
    private InsuranceLink[] InsuranceLinks(EnhanceState state, Dictionary<string, Item> owned, HashSet<string> inventory, EnhanceSettings settings)
    {
        var links = new List<InsuranceLink>();
        void Add(string id, InsuranceTier tier, double percent, bool lapsed)
        {
            var item = owned[id];
            var value = PrestigeValue(state, item, settings);
            int fee;
            try { fee = Enum.IsDefined(tier) && tier != InsuranceTier.None ? EnhanceRules.InsuranceFee(value, tier, settings) : 0; }
            catch (InvalidOperationException) { fee = 0; }
            links.Add(new InsuranceLink { ItemId = id, Template = item.Template.ToString(), IsWeapon = backend.IsWeapon(item.Template),
                Stars = Stars(state, id), MythicLevel = state.Mythic.GetValueOrDefault(id)?.Level ?? -1, Tier = tier, Percent = percent,
                Paid = InsurancePaid(state, id), CurrentValue = value, Fee = fee, Lapsed = lapsed });
        }
        foreach (var (id, coverage) in state.Coverage.Where(p => owned.ContainsKey(p.Key) && p.Value != null && p.Value.Tier != InsuranceTier.None))
            Add(id, coverage.Tier, coverage.ReturnPercent, false);
        foreach (var (id, payment) in state.InsurancePayments.Where(p => p.Value?.Lapsed == true && inventory.Contains(p.Key)
            && owned.ContainsKey(p.Key) && !state.Coverage.ContainsKey(p.Key)))
        {
            // Lapsed before 1.4.0 kept no tier: offered as Advanced, the insurance tab's default.
            var tier = Enum.IsDefined(payment.Tier) && payment.Tier != InsuranceTier.None ? payment.Tier : InsuranceTier.Advanced;
            Add(id, tier, settings.ReturnPercent(tier), true);
        }
        return links.ToArray();
    }

    private static PrestigeCovered Covered(EnhanceState state, string id)
    {
        var transcend = state.Transcend.GetValueOrDefault(id);
        var mythic = state.Mythic.GetValueOrDefault(id);
        return new PrestigeCovered
        {
            Tracks = new(state.Tracks.GetValueOrDefault(id) ?? new()), Stars = Stars(state, id),
            Transcend = transcend == null ? null : new TranscendRecord { Second = transcend.Second, Third = transcend.Third,
                SecondPity = transcend.SecondPity, ThirdPity = transcend.ThirdPity },
            Mythic = mythic == null ? null : new MythicRecord { Level = mythic.Level, Pity = mythic.Pity, Zones = new(mythic.Zones),
                Candidates = new(mythic.Candidates), Rerolls = mythic.Rerolls }
        };
    }

    // Inside SPT's prestige, before it mails the items chosen for transfer. The old character is
    // already deleted and `fresh` registered; its records start with the insured gear only, keyed
    // by the old ids, so the mail hook moves them to the mailed copies (SPT's transfer included).
    public PrestigeCarry? BeginPrestige(SptProfile old, SptProfile fresh, int? nextLevel, IEnumerable<string> transfers)
    {
        lock (sync)
        {
            var pmc = old.CharacterData?.PmcData;
            var items = pmc?.Inventory?.Items;
            var session = fresh.ProfileInfo?.ProfileId;
            if (items == null || session == null || old.ExtensionData == null
                || !old.ExtensionData.TryGetValue(StateKey, out var raw) || raw == null) return null;
            var to = nextLevel ?? 0;
            // Only the prestige the player obtained: SPT keeps it pending on the old character, one
            // stage up. A profile created at a forced level (launcher) is not an advance.
            if (old.SptData?.PendingPrestige == null || to != Math.Max(0, pmc!.Info?.PrestigeLevel ?? 0) + 1) return null;
            var state = Read(raw, session.Value.ToString());
            // Records changed with the F12 developer options on never reach the next character:
            // the ones from before them are carried (none when they cannot be read).
            if (state.Sandbox != null)
            {
                EnhanceRecords? saved = null;
                try { saved = string.IsNullOrEmpty(state.Sandbox.Records) ? null : serializer.Deserialize<EnhanceRecords>(state.Sandbox.Records); }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { }
                if (saved == null) return null;
                var real = new EnhanceState();
                SealedStateConverter.Copy(saved, real);
                real.Sandbox = null;
                Normalize(real);
                state = real;
            }
            if (state.Prestige.Count == 0) return null;
            var settings = state.LastSettings ?? new EnhanceSettings();
            var byId = items.GroupBy(i => i.Id.ToString()).ToDictionary(g => g.Key, g => g.First());
            var moved = Descendants(items, transfers);
            var next = new EnhanceState { LastSettings = state.LastSettings, AutoRegisterInserts = state.AutoRegisterInserts };
            var carry = new PrestigeCarry(session.Value, to, new());
            var handled = new HashSet<string>();
            // Largest first, so armor comes before an insured part inside it.
            foreach (var (root, policy, tree) in state.Prestige.Where(p => byId.ContainsKey(p.Key) && Recorded(state, p.Key))
                .Select(p => (p.Key, p.Value, Tree: SlotTree(items, p.Key))).OrderByDescending(p => p.Tree.Count).ToList())
            {
                var item = byId[root];
                var unpaid = EnhanceRules.PrestigeFee(PrestigeValue(state, item, settings), settings) > policy.Paid;
                Carry(state, next, tree, root, unpaid ? policy.Covered : null);
                next.PrestigeReturns.Add(new PrestigeReturn { Root = root, Template = item.Template.ToString(),
                    IsWeapon = backend.IsWeapon(item.Template), Level = to });
                if (handled.Contains(root) || moved.Contains(root)) continue;
                var mail = tree.Where(i => !moved.Contains(i.Id.ToString()) && !handled.Contains(i.Id.ToString())).ToList();
                handled.UnionWith(mail.Select(i => i.Id.ToString()));
                carry.Trees.Add(Clone(mail));
            }
            if (next.PrestigeReturns.Count == 0) return null;
            fresh.ExtensionData ??= new();
            fresh.ExtensionData[StateKey] = next;
            return carry;
        }
    }

    // After SPT's prestige: mail the insured gear it did not transfer. Returns (sent, failed).
    public (int Sent, int Failed) CompletePrestige(PrestigeCarry carry)
    {
        lock (sync)
        {
            var (sent, failed) = (0, 0);
            foreach (var tree in carry.Trees.Where(t => t.Count > 0))
            {
                // One failed mail must not keep the others back.
                try { backend.SendPrestigeReturn(carry.Session, tree, PrestigeText(carry.Level)); sent++; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failed++;
                    PrestigePatch.Logger?.Error("Gear Enhance: 프레스티지 보험 반환 우편 실패. Prestige insurance return mail failed.", ex);
                }
            }
            return (sent, failed);
        }
    }

    private static string PrestigeText(int level) =>
        $"프레스티지 보험: 프레스티지 {level} 달성 전에 등록한 강화 장비를 강화된 상태로 돌려드립니다. 다음 반환을 받으려면 다시 등록하세요. "
        + $"Prestige insurance: the gear you registered before reaching prestige {level} is returned still enhanced. Register it again for the next return.";

    // Every item under the given roots, slots and containers alike (what SPT transfers with an item).
    private static HashSet<string> Descendants(List<Item> items, IEnumerable<string> roots)
    {
        var children = items.Where(i => i.ParentId != null).ToLookup(i => i.ParentId!);
        var found = new HashSet<string>();
        var queue = new Queue<string>(roots);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!found.Add(id)) continue;
            foreach (var child in children[id]) queue.Enqueue(child.Id.ToString());
        }
        return found;
    }

    private static void Carry(EnhanceState from, EnhanceState to, List<Item> tree, string root, PrestigeCovered? covered)
    {
        // An unpaid rise of the armor 2nd transcendence: the max durability it added since the last
        // payment comes off the items (they are the old character's, so SPT's transfer mail and ours
        // both send them this way). The registration's free "restore max durability" re-adds the
        // covered level's amount afterwards.
        var strip = covered != null && covered.Tracks.Count > 0
            && (covered.Transcend?.Second ?? 0) < (from.Transcend.GetValueOrDefault(root)?.Second ?? 0);
        foreach (var item in tree)
        {
            var id = item.Id.ToString();
            if (!strip || !from.Grants.TryGetValue(id, out var added) || item.Upd?.Repairable == null) continue;
            Grant(item.Upd.Repairable, -added.Amount);
            from.Grants.Remove(id);
        }
        foreach (var id in tree.Select(i => i.Id.ToString()))
        {
            if (from.Tracks.TryGetValue(id, out var tracks)) to.Tracks[id] = new(tracks);
            if (from.EvolvedItems.Contains(id)) to.EvolvedItems.Add(id);
            if (from.ThirdStarItems.Contains(id)) to.ThirdStarItems.Add(id);
            if (from.Inserts.TryGetValue(id, out var inserts)) to.Inserts[id] = inserts;
            if (from.Transcend.TryGetValue(id, out var transcend)) to.Transcend[id] = transcend;
            if (from.Mythic.TryGetValue(id, out var mythic)) to.Mythic[id] = mythic;
            if (from.Tickets.TryGetValue(id, out var tickets)) to.Tickets[id] = tickets;
            if (from.Grants.TryGetValue(id, out var grant)) SetGrant(to, id, grant);
        }
        // An unpaid rise: the gear comes back as it was when it was last paid for.
        if (covered == null || covered.Tracks.Count == 0) return;
        to.Tracks[root] = new(covered.Tracks);
        to.EvolvedItems.Remove(root);
        to.ThirdStarItems.Remove(root);
        if (covered.Stars >= 2) to.EvolvedItems.Add(root);
        if (covered.Stars >= 3) to.ThirdStarItems.Add(root);
        if (covered.Transcend != null) to.Transcend[root] = covered.Transcend;
        else to.Transcend.Remove(root);
        if (covered.Mythic != null) to.Mythic[root] = covered.Mythic;
        else to.Mythic.Remove(root);
    }
}
