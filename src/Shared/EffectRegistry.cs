#nullable enable
using System;
using System.Collections.Generic;

namespace GearEnhance;

// Copy-on-replace snapshots: getters never perform HTTP or mutate item templates.
public sealed class EffectRegistry
{
    private volatile Dictionary<string, EnhanceRecord> records = new Dictionary<string, EnhanceRecord>();
    private volatile EffectTuning tuning = new EffectTuning();
    public string ProfileId { get; private set; } = "";
    public void Clear()
    {
        records = new Dictionary<string, EnhanceRecord>();
        ProfileId = "";
    }

    public void Configure(EffectTuning value)
    {
        value.Validate();
        tuning = value;
    }

    public void Replace(EnhanceSnapshot snapshot, string expectedProfile)
    {
        if (string.IsNullOrEmpty(expectedProfile) || snapshot.ProfileId != expectedProfile)
            throw new InvalidOperationException("강화 기록의 PMC 프로필이 일치하지 않습니다.");
        var next = new Dictionary<string, EnhanceRecord>(StringComparer.Ordinal);
        foreach (var item in snapshot.Items ?? Array.Empty<EnhanceRecord>())
        {
            if (string.IsNullOrEmpty(item.ItemId) || item.Level < 1 || item.Level > 100
                || !Enum.IsDefined(typeof(EnhancePath), item.Path)) continue;
            next[item.ItemId] = new EnhanceRecord { ItemId = item.ItemId, Level = item.Level, Path = item.Path };
        }
        records = next;
        ProfileId = expectedProfile;
    }

    public EnhanceRecord? Find(string id) => records.TryGetValue(id, out var item) ? item : null;
    public void ApplyReceipt(EnhanceReceipt receipt, string expectedProfile)
    {
        if (string.IsNullOrEmpty(expectedProfile)) throw new InvalidOperationException("PMC 프로필이 없습니다.");
        var next = ProfileId == expectedProfile
            ? new Dictionary<string, EnhanceRecord>(records, StringComparer.Ordinal)
            : new Dictionary<string, EnhanceRecord>(StringComparer.Ordinal);
        if (receipt.Success)
            next[receipt.ItemId] = new EnhanceRecord { ItemId = receipt.ItemId, Path = receipt.Path, Level = receipt.Level };
        else if (receipt.FailureMode == FailureMode.Destroy) next.Remove(receipt.ItemId);
        records = next; ProfileId = expectedProfile;
    }
    public double Amount(string id, EnhancePath path)
    {
        var item = Find(id);
        return item != null && item.Path == path ? EnhanceEffects.Amount(path, item.Level, tuning) : 0;
    }
}
