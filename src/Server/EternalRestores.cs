using System.Collections;
using System.Reflection;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace GearEnhance;

// User 2026-10-31: EquipmentIsEternal (ScrewTSW) puts the pre-raid gear back with the same ids
// after a PMC death, at the next profile save (after HandlePostRaidPmc). Without this the raid
// end forfeited that gear's enhancement and enhancement insurance mailed a second copy.
// Read only while the mod is loaded; its queued restore is left untouched.
internal static class EternalRestores
{
    private const string SnapshotType = "EquipmentIsEternal.EquipmentSnapshot";
    private const string SaveType = "EquipmentIsEternal.Patches.SaveProfilePatch";

    // Its SaveProfilePatch (1.2.0): these slots always come back, the rest by its config.
    private static readonly string[] AlwaysSlots =
    [
        "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster",
        "Backpack", "Pockets", "TacticalVest", "ArmorVest",
        "Eyewear", "FaceCover", "Headwear", "Earpiece"
    ];

    internal sealed record Options(bool OnlyInsured, bool ArmBand, bool Scabbard, bool Secured, bool Special);

    private static bool searched;
    private static FieldInfo? pendingField;
    private static FieldInfo? configField;
    private static bool warned;

    // Ids it will put back for this session; null when the mod is absent or nothing is queued
    // (no death). If its layout changed, the raid end runs as without it and says so once.
    internal static HashSet<string>? Pending(MongoId session, Action<string>? warn)
    {
        if (!searched)
        {
            searched = true;
            var snapshot = Find(SnapshotType);
            if (snapshot == null) return null;
            pendingField = snapshot.GetField("PendingRestores", BindingFlags.NonPublic | BindingFlags.Static);
            configField = Find(SaveType)?.GetField("_config", BindingFlags.NonPublic | BindingFlags.Static);
            if (pendingField == null || configField == null) Warn(warn);
        }
        if (pendingField == null || configField == null) return null;
        try
        {
            if (pendingField.GetValue(null) is not IDictionary queue) { Warn(warn); return null; }
            var pending = queue[session.ToString()];
            if (pending == null) return null;
            var data = Get(pending, "Snapshot");
            var config = configField.GetValue(null);
            if (data == null || config == null || Get(data, "EquipmentItems") is not List<Item> items
                || Get(data, "InsuredItemIds") is not List<string> insured)
            {
                Warn(warn);
                return null;
            }
            var options = new Options(Flag(config, "OnlyRestoreInsured"), Flag(config, "RestoreArmBand"),
                Flag(config, "RestoreScabbard"), Flag(config, "RestoreSecuredContainer"), Flag(config, "RestoreSpecialSlots"));
            return Restored(items, insured, options);
        }
        catch (Exception)
        {
            Warn(warn);
            return null;
        }
    }

    // Mirrors its restore: each slot root of the snapshot with every item under it.
    internal static HashSet<string> Restored(IReadOnlyList<Item> snapshot, ICollection<string> insured, Options options)
    {
        var ids = snapshot.Select(i => i.Id.ToString()).ToHashSet();
        var restored = new HashSet<string>();
        var roots = snapshot.Where(i => i.ParentId != null && !ids.Contains(i.ParentId) && i.SlotId != null).ToList();
        foreach (var root in roots.Where(r => AlwaysSlots.Contains(r.SlotId)))
            if (!options.OnlyInsured || insured.Contains(root.Id.ToString())) Add(snapshot, root, restored);
        // One root each, as its RestoreTier2Slot takes the first.
        foreach (var (slot, on) in new[] { ("ArmBand", options.ArmBand), ("Scabbard", options.Scabbard), ("SecuredContainer", options.Secured) })
            if (on && roots.FirstOrDefault(r => r.SlotId == slot) is { } root) Add(snapshot, root, restored);
        if (options.Special)
            foreach (var root in roots.Where(r => r.SlotId!.StartsWith("SpecialSlot", StringComparison.Ordinal)))
                Add(snapshot, root, restored);
        return restored;
    }

    private static void Add(IReadOnlyList<Item> snapshot, Item root, HashSet<string> restored)
    {
        // The snapshot is a tree; the set check stops a malformed parent loop.
        var open = new Queue<string>();
        if (restored.Add(root.Id.ToString())) open.Enqueue(root.Id.ToString());
        while (open.Count > 0)
        {
            var parent = open.Dequeue();
            foreach (var child in snapshot.Where(i => i.ParentId == parent))
                if (restored.Add(child.Id.ToString())) open.Enqueue(child.Id.ToString());
        }
    }

    private static Type? Find(string name) =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);

    private static object? Get(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);

    // A renamed option must not read as "off": that would claim gear it does not restore.
    private static bool Flag(object config, string name) =>
        Get(config, name) as bool? ?? throw new MissingMemberException(config.GetType().FullName, name);

    private static void Warn(Action<string>? warn)
    {
        if (warned) return;
        warned = true;
        warn?.Invoke("Gear Enhance: EquipmentIsEternal 복구 목록을 읽지 못해 호환 처리를 건너뜁니다(다른 버전). "
            + "Could not read EquipmentIsEternal's pending restore (other version); compatibility skipped.");
    }
}
