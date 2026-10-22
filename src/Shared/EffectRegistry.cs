#nullable enable
using System;
using System.Collections.Generic;

namespace GearEnhance;

// Copy-on-replace snapshots: hot getters read a single item's independent tracks.
public sealed class EffectRegistry
{
    private volatile Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>> records
        = new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>();
    private volatile EffectTuning tuning = new EffectTuning();
    public string ProfileId { get; private set; } = "";
    public void Clear()
    {
        records = new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>();
        ProfileId = "";
    }
    public void Configure(EffectTuning value) { value.Validate(); tuning = value; }
    public void Replace(EnhanceSnapshot snapshot, string expectedProfile)
    {
        if (string.IsNullOrEmpty(expectedProfile) || snapshot.ProfileId != expectedProfile)
            throw new InvalidOperationException("강화 기록의 PMC 프로필이 일치하지 않습니다.");
        var next = new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>(StringComparer.Ordinal);
        foreach (var item in snapshot.Items ?? Array.Empty<EnhanceRecord>())
        {
            if (string.IsNullOrEmpty(item.ItemId) || item.Level < 1 || item.Level > 100
                || !Enum.IsDefined(typeof(EnhancePath), item.Path)) continue;
            if (!next.TryGetValue(item.ItemId, out var tracks)) next[item.ItemId] = tracks = new Dictionary<EnhancePath, EnhanceRecord>();
            tracks[item.Path] = new EnhanceRecord { ItemId = item.ItemId, Level = item.Level, Path = item.Path, Evolved = item.Evolved };
        }
        records = next; ProfileId = expectedProfile;
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
        var total = TotalLevel(id);
        return total > 0 ? (IsEvolved(id) ? $"★★ 진화 +{total}" : $"★ +{total}") : "";
    }
    public bool IsEvolved(string id)
    {
        if (!records.TryGetValue(id, out var tracks)) return false;
        foreach (var track in tracks.Values) if (track.Evolved) return true;
        return false;
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
        if (string.IsNullOrEmpty(expectedProfile)) throw new InvalidOperationException("PMC 프로필이 없습니다.");
        var next = ProfileId == expectedProfile
            ? new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>(records, StringComparer.Ordinal)
            : new Dictionary<string, Dictionary<EnhancePath, EnhanceRecord>>(StringComparer.Ordinal);
        if (receipt.Success && receipt.IsEvolution)
        {
            if (next.TryGetValue(receipt.ItemId, out var existingTracks))
            {
                var evolved = new Dictionary<EnhancePath, EnhanceRecord>();
                foreach (var entry in existingTracks)
                    evolved[entry.Key] = new EnhanceRecord { ItemId = entry.Value.ItemId, Path = entry.Key, Level = entry.Value.Level, Evolved = true };
                next[receipt.ItemId] = evolved;
            }
        }
        else if (receipt.Success)
        {
            var tracks = next.TryGetValue(receipt.ItemId, out var current)
                ? new Dictionary<EnhancePath, EnhanceRecord>(current) : new Dictionary<EnhancePath, EnhanceRecord>();
            tracks[receipt.Path] = new EnhanceRecord { ItemId = receipt.ItemId, Path = receipt.Path, Level = receipt.Level, Evolved = receipt.Evolved };
            next[receipt.ItemId] = tracks;
        }
        else if (receipt.FailureMode == FailureMode.Downgrade && next.TryGetValue(receipt.ItemId, out var existing))
        {
            var tracks = new Dictionary<EnhancePath, EnhanceRecord>(existing);
            if (receipt.Level > 0)
                tracks[receipt.Path] = new EnhanceRecord { ItemId = receipt.ItemId, Path = receipt.Path, Level = receipt.Level, Evolved = receipt.Evolved };
            else tracks.Remove(receipt.Path);
            if (tracks.Count == 0) next.Remove(receipt.ItemId); else next[receipt.ItemId] = tracks;
        }
        else if (receipt.FailureMode == FailureMode.Destroy) next.Remove(receipt.ItemId);
        records = next; ProfileId = expectedProfile;
    }
    public double Amount(string id, EnhancePath path)
    {
        var config = tuning;
        if (!records.TryGetValue(id, out var tracks) || !tracks.TryGetValue(path, out var item)) return 0;
        return EnhanceEffects.Amount(path, item.Level, config, item.Evolved);
    }
}
