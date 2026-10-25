#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace GearEnhance;

// Copy-on-replace snapshots: hot getters read a single item's independent tracks.
public sealed class EffectRegistry
{
    private volatile Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>> records
        = new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>();
    private volatile EffectTuning tuning = new EffectTuning();
    // Armor id → parts registered to it (1.2.0). Only these inherit while inserted.
    private volatile Dictionary<string, HashSet<string>> inserts = new Dictionary<string, HashSet<string>>();
    // 1.3.0: transcendence levels per gear and max durability added per armor item.
    private volatile Dictionary<string, (int Second, int Third)> transcend = new Dictionary<string, (int, int)>();
    private volatile Dictionary<string, double> grants = new Dictionary<string, double>();
    private volatile Dictionary<string, (InsuranceTier Tier, double Percent)> coverage = new Dictionary<string, (InsuranceTier, double)>();
    private volatile TranscendTuning transcendTuning = new TranscendTuning();
    // 1.4.0: mythic level and added zones per gear, and insured gear waiting to be mailed back.
    private volatile Dictionary<string, (int Level, string[] Zones)> mythic = new Dictionary<string, (int, string[])>();
    private volatile ReturnLink[] returns = Array.Empty<ReturnLink>();
    private volatile MythicTuning mythicTuning = new MythicTuning();
    private int version;
    public string ProfileId { get; private set; } = "";
    // Bumped on every change; per-weapon caches compare it instead of re-reading ids.
    public int Version => Volatile.Read(ref version);
    // Hot hooks leave at once while nothing is transcended or granted.
    public bool HasTranscend => transcend.Count > 0;
    public bool HasGrants => grants.Count > 0;
    public bool HasMythic => mythic.Count > 0;
    public TranscendTuning TranscendTuning => transcendTuning;
    public MythicTuning MythicTuning => mythicTuning;
    // -1 when the gear is not mythic.
    public int MythicLevel(string id) => mythic.TryGetValue(id, out var held) ? held.Level : -1;
    public string[] MythicZones(string id) => mythic.TryGetValue(id, out var held) ? held.Zones : Array.Empty<string>();
    public ReturnLink[] Returns => returns;
    public void ConfigureMythic(MythicTuning value)
    {
        value.Validate();
        mythicTuning = value;
        Interlocked.Increment(ref version);
    }
    public void Clear()
    {
        records = new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>();
        inserts = new Dictionary<string, HashSet<string>>();
        transcend = new Dictionary<string, (int, int)>();
        grants = new Dictionary<string, double>();
        coverage = new Dictionary<string, (InsuranceTier, double)>();
        mythic = new Dictionary<string, (int, string[])>();
        returns = Array.Empty<ReturnLink>();
        ProfileId = "";
        Interlocked.Increment(ref version);
    }
    public InsuranceTier Coverage(string id) => coverage.TryGetValue(id, out var held) ? held.Tier : InsuranceTier.None;
    public double CoveragePercent(string id) => coverage.TryGetValue(id, out var held) ? held.Percent : 0;
    public void ConfigureTranscend(TranscendTuning value)
    {
        value.Validate();
        transcendTuning = value;
        Interlocked.Increment(ref version);
    }
    public int TranscendLevel(string id, TranscendTrack track) => !transcend.TryGetValue(id, out var levels) ? 0
        : track == TranscendTrack.Third ? levels.Third : track == TranscendTrack.Second ? levels.Second : 0;
    // Both tracks together, 0..10: the stash marker and the main window button.
    public int TranscendTotal(string id) => TranscendLevel(id, TranscendTrack.Second) + TranscendLevel(id, TranscendTrack.Third);
    public double Grant(string id) => grants.TryGetValue(id, out var amount) ? amount : 0;
    public bool Registered(string armorId, string partId) =>
        inserts.TryGetValue(armorId, out var parts) && parts.Contains(partId);
    public void Configure(EffectTuning value) { value.Validate(); tuning = value; }
    public void Replace(EnhanceSnapshot snapshot, string expectedProfile)
    {
        if (string.IsNullOrEmpty(expectedProfile) || snapshot.ProfileId != expectedProfile)
            throw Loc.Invalid("강화 기록의 PMC 프로필이 일치하지 않습니다.", "The enhancement records belong to a different PMC profile.");
        var next = new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>(StringComparer.Ordinal);
        foreach (var item in snapshot.Items ?? Array.Empty<EnhanceRecord>())
        {
            if (string.IsNullOrEmpty(item.ItemId) || item.Level < 1 || item.Level > 100
                || !Enum.IsDefined(typeof(EnhancePath), item.Path)) continue;
            if (!next.TryGetValue(item.ItemId, out var tracks)) next[item.ItemId] = tracks = new Dictionary<EnhancePath, EnhanceRecord>();
            tracks[item.Path] = new EnhanceRecord { ItemId = item.ItemId, Level = item.Level, Path = item.Path, Evolved = item.Evolved, StarLevel = StarEvolution.Level(item.Evolved, item.StarLevel) };
        }
        var links = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var link in snapshot.Inserts ?? Array.Empty<InsertLink>())
            if (!string.IsNullOrEmpty(link?.ArmorId))
                links[link!.ArmorId] = new HashSet<string>(link.PartIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var levels = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var link in snapshot.Transcend ?? Array.Empty<TranscendLink>())
            if (!string.IsNullOrEmpty(link?.ItemId) && (link!.Second > 0 || link.Third > 0))
                levels[link.ItemId] = (TranscendRules.Clamp(link.Second), TranscendRules.Clamp(link.Third));
        var added = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var link in snapshot.Durability ?? Array.Empty<DurabilityLink>())
            if (!string.IsNullOrEmpty(link?.ItemId) && link!.Amount > 0 && !double.IsInfinity(link.Amount)) added[link.ItemId] = link.Amount;
        var insured = new Dictionary<string, (InsuranceTier, double)>(StringComparer.Ordinal);
        foreach (var link in snapshot.Coverage ?? Array.Empty<CoverageLink>())
            if (!string.IsNullOrEmpty(link?.ItemId) && link!.Tier != InsuranceTier.None && Enum.IsDefined(typeof(InsuranceTier), link.Tier))
                insured[link.ItemId] = (link.Tier, double.IsFinite(link.Percent) ? Math.Max(0, Math.Min(100, link.Percent)) : 0);
        var mythical = new Dictionary<string, (int, string[])>(StringComparer.Ordinal);
        foreach (var link in snapshot.Mythic ?? Array.Empty<MythicLink>())
            if (!string.IsNullOrEmpty(link?.ItemId) && link!.Level >= 0)
                mythical[link.ItemId] = (MythicRules.Clamp(link.Level), link.Zones ?? Array.Empty<string>());
        records = next; inserts = links; transcend = levels; grants = added; coverage = insured; mythic = mythical;
        returns = snapshot.Returns ?? Array.Empty<ReturnLink>(); ProfileId = expectedProfile;
        Interlocked.Increment(ref version);
    }
    public EnhanceRecord? Find(string id, EnhancePath path) =>
        records.TryGetValue(id, out var tracks) && tracks.TryGetValue(path, out var item) ? item : null;
    public EnhanceRecord? Find(string id)
    {
        if (!records.TryGetValue(id, out var tracks)) return null;
        EnhanceRecord? best = null;
        foreach (var item in tracks.Values) if (best == null || item.Level > best.Level) best = item;
        return best;
    }
    public int TotalLevel(string id)
    {
        if (!records.TryGetValue(id, out var tracks)) return 0;
        var total = 0;
        foreach (var track in tracks.Values) total += track.Level;
        return total;
    }
    public string Badge(string id)
    {
        // 1.4.0 (design 8): the total only while at one star; stars alone once evolved; ✦ Mythic instead of stars.
        var total = TotalLevel(id);
        return total <= 0 ? "" : MythicLevel(id) >= 0 ? Loc.L("✦ 신화", "✦ Mythic") : IsEvolved(id) ? StarEvolution.Stars(StarLevel(id)) : $"★ +{total}";
    }
    public bool IsEvolved(string id) => StarLevel(id) >= 2;
    public int StarLevel(string id)
    {
        var level = 1;
        if (!records.TryGetValue(id, out var tracks)) return level;
        foreach (var track in tracks.Values) level = Math.Max(level, StarEvolution.Level(track.Evolved, track.StarLevel));
        return level;
    }
    public bool IsComplete(string id) => records.TryGetValue(id, out var tracks) && Complete(tracks, tuning);
    private static bool Complete(Dictionary<EnhancePath, EnhanceRecord> tracks, EffectTuning config)
    {
        bool Max(EnhancePath path) => tracks.TryGetValue(path, out var record) && record.Level >= config.MaxLevel;
        return Max(EnhancePath.Recoil) && Max(EnhancePath.Ergonomics) && Max(EnhancePath.Precision)
            || Max(EnhancePath.ArmorDurability) && Max(EnhancePath.Mobility);
    }
    public void ApplyReceipt(EnhanceReceipt receipt, string expectedProfile)
    {
        if (string.IsNullOrEmpty(expectedProfile)) throw Loc.Invalid("PMC 프로필이 없습니다.", "No PMC profile.");
        // Insurance and registration never change levels. A transcendence receipt carries
        // the track's level in Level and names some enhancement Path: never a track level.
        if (receipt.Action == EnhanceAction.Insure || receipt.Action == EnhanceAction.RegisterInserts || receipt.Action == EnhanceAction.BuyTicket) return;
        // 1.4.0 ???, mythic and reroll receipts carry the mythic level in MythicLevel; never a track level.
        if (receipt.Action == EnhanceAction.Mystery || receipt.Action == EnhanceAction.Mythic || receipt.Action == EnhanceAction.Reroll)
        {
            if (ProfileId != expectedProfile) return;
            var held = new Dictionary<string, (int, string[])>(mythic, StringComparer.Ordinal);
            if (receipt.Action == EnhanceAction.Mystery && !receipt.Success && receipt.FailureMode == FailureMode.Destroy)
            {
                held.Remove(receipt.ItemId);
                var left = new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>(records, StringComparer.Ordinal);
                left.Remove(receipt.ItemId);
                records = left;
            }
            else if (receipt.MythicLevel >= 0) held[receipt.ItemId] = (MythicRules.Clamp(receipt.MythicLevel), receipt.Zones ?? Array.Empty<string>());
            mythic = held;
            Interlocked.Increment(ref version);
            return;
        }
        if (receipt.Action == EnhanceAction.Transcend)
        {
            if (ProfileId != expectedProfile || !TranscendRules.Valid(receipt.Transcend)) return;
            var levels = new Dictionary<string, (int Second, int Third)>(transcend, StringComparer.Ordinal);
            levels.TryGetValue(receipt.ItemId, out var current);
            var level = TranscendRules.Clamp(receipt.Level);
            current = receipt.Transcend == TranscendTrack.Third ? (current.Second, level) : (level, current.Third);
            if (current.Second == 0 && current.Third == 0) levels.Remove(receipt.ItemId); else levels[receipt.ItemId] = current;
            transcend = levels;
            Interlocked.Increment(ref version);
            return;
        }
        var next = ProfileId == expectedProfile
            ? new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>(records, StringComparer.Ordinal)
            : new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>(StringComparer.Ordinal);
        if (receipt.Success && receipt.IsEvolution)
        {
            if (next.TryGetValue(receipt.ItemId, out var existingTracks))
            {
                var evolved = new Dictionary<EnhancePath, EnhanceRecord>();
                foreach (var entry in existingTracks)
                    evolved[entry.Key] = new EnhanceRecord { ItemId = entry.Value.ItemId, Path = entry.Key, Level = entry.Value.Level, Evolved = true, StarLevel = StarEvolution.Level(true, receipt.StarLevel) };
                next[receipt.ItemId] = evolved;
            }
        }
        else if (receipt.Success)
        {
            var tracks = next.TryGetValue(receipt.ItemId, out var current)
                ? new Dictionary<EnhancePath, EnhanceRecord>(current) : new Dictionary<EnhancePath, EnhanceRecord>();
            tracks[receipt.Path] = new EnhanceRecord { ItemId = receipt.ItemId, Path = receipt.Path, Level = receipt.Level, Evolved = receipt.Evolved, StarLevel = StarEvolution.Level(receipt.Evolved, receipt.StarLevel) };
            next[receipt.ItemId] = tracks;
        }
        else if (receipt.FailureMode == FailureMode.Downgrade && next.TryGetValue(receipt.ItemId, out var existing))
        {
            var tracks = new Dictionary<EnhancePath, EnhanceRecord>(existing);
            if (receipt.Level > 0)
                tracks[receipt.Path] = new EnhanceRecord { ItemId = receipt.ItemId, Path = receipt.Path, Level = receipt.Level, Evolved = receipt.Evolved, StarLevel = StarEvolution.Level(receipt.Evolved, receipt.StarLevel) };
            else tracks.Remove(receipt.Path);
            if (tracks.Count == 0) next.Remove(receipt.ItemId); else next[receipt.ItemId] = tracks;
        }
        else if (receipt.FailureMode == FailureMode.Destroy) next.Remove(receipt.ItemId);
        records = next; ProfileId = expectedProfile;
        Interlocked.Increment(ref version);
    }
    public double Amount(string id, EnhancePath path)
    {
        var config = tuning;
        if (!records.TryGetValue(id, out var tracks) || !tracks.TryGetValue(path, out var item)) return 0;
        return EnhanceEffects.Amount(path, item.Level, config, StarEvolution.Level(item.Evolved, item.StarLevel));
    }
}
