using System.Collections.Concurrent;
using GearEnhance;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace GearEnhanceTests
{
    // User 2026-10-31: EquipmentIsEternal puts the pre-raid gear back after a death. Its types are
    // stood in below with the names and members of its 1.2.0 source (ScrewTSW 49c2508).
    public sealed class EternalTests
    {
        private const string Equipment = "e00000000000000000000000";

        private static Item Slot(string id, string slot) => new() { Id = new MongoId(id), Template = new MongoId(), ParentId = Equipment, SlotId = slot };
        private static Item Child(string id, string parent) => new() { Id = new MongoId(id), Template = new MongoId(), ParentId = parent, SlotId = "main" };

        private static List<Item> Snapshot() =>
        [
            Slot("a00000000000000000000001", "FirstPrimaryWeapon"),
            Child("a00000000000000000000002", "a00000000000000000000001"),
            Slot("b00000000000000000000001", "Backpack"),
            Child("b00000000000000000000002", "b00000000000000000000001"),
            Child("b00000000000000000000003", "b00000000000000000000002"),
            Slot("c00000000000000000000001", "ArmBand"),
            Slot("d00000000000000000000001", "SecuredContainer"),
            Child("d00000000000000000000002", "d00000000000000000000001"),
            Slot("f00000000000000000000001", "SpecialSlot1"),
        ];

        private static readonly EternalRestores.Options Defaults = new(false, false, false, false, false);

        [Fact]
        public void Default_config_restores_the_always_slots_with_everything_inside()
        {
            var restored = EternalRestores.Restored(Snapshot(), [], Defaults);
            Assert.Equal(["a00000000000000000000001", "a00000000000000000000002", "b00000000000000000000001",
                "b00000000000000000000002", "b00000000000000000000003"], restored.Order().ToArray());
        }

        [Fact]
        public void Only_insured_skips_uninsured_roots_and_options_add_their_slots()
        {
            var insured = EternalRestores.Restored(Snapshot(), ["b00000000000000000000001"], Defaults with { OnlyInsured = true });
            Assert.Equal(["b00000000000000000000001", "b00000000000000000000002", "b00000000000000000000003"], insured.Order().ToArray());
            var all = EternalRestores.Restored(Snapshot(), [], new(false, true, true, true, true));
            Assert.Equal(9, all.Count);
        }

        [Fact]
        public void Queued_restore_is_read_from_the_mod_without_consuming_it()
        {
            var session = new MongoId("111111111111111111111111");
            EquipmentIsEternal.Patches.SaveProfilePatch.Set(new EquipmentIsEternal.ModConfig { RestoreSecuredContainer = true });
            Assert.Null(EternalRestores.Pending(session, _ => Assert.Fail("layout matches")));
            EquipmentIsEternal.EquipmentSnapshot.MarkPendingRestore(session.ToString(), new EquipmentIsEternal.EquipmentSnapshot.PendingRestore
            {
                Snapshot = new EquipmentIsEternal.EquipmentSnapshot.SnapshotData { EquipmentItems = Snapshot() }
            });
            var restored = EternalRestores.Pending(session, _ => Assert.Fail("layout matches"));
            Assert.NotNull(restored);
            Assert.Equal(7, restored!.Count);
            Assert.Contains("d00000000000000000000002", restored);
            Assert.DoesNotContain("c00000000000000000000001", restored);
            // Its own save patch still finds the queue.
            Assert.NotNull(EquipmentIsEternal.EquipmentSnapshot.ConsumePendingRestore(session.ToString()));
        }
    }
}

namespace EquipmentIsEternal
{
    public record ModConfig
    {
        public bool RestoreArmBand { get; init; }
        public bool RestoreSpecialSlots { get; init; }
        public bool RestoreScabbard { get; init; }
        public bool RestoreSecuredContainer { get; init; }
        public bool OnlyRestoreInsured { get; init; }
        public bool KeepQuestItems { get; init; }
        public bool DeduplicateItems { get; init; } = true;
    }

    public static class EquipmentSnapshot
    {
        private static readonly ConcurrentDictionary<string, SnapshotData> Snapshots = new();
        private static readonly ConcurrentDictionary<string, PendingRestore> PendingRestores = new();

        public record SnapshotData
        {
            public List<Item> EquipmentItems { get; init; } = [];
            public List<string> InsuredItemIds { get; init; } = [];
        }

        public record PendingRestore
        {
            public required SnapshotData Snapshot { get; init; }
            public List<Item>? QuestItems { get; init; }
        }

        public static void MarkPendingRestore(string sessionId, PendingRestore pending) => PendingRestores[sessionId] = pending;

        public static PendingRestore? ConsumePendingRestore(string sessionId)
        {
            PendingRestores.TryRemove(sessionId, out var data);
            return data;
        }
    }
}

namespace EquipmentIsEternal.Patches
{
    public class SaveProfilePatch
    {
        private static ModConfig _config = default!;
        public static void Set(ModConfig config) => _config = config;
    }
}
