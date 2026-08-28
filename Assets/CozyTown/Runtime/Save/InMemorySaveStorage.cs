using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Save
{
    public sealed class InMemorySaveStorage : ISaveStorage
    {
        private readonly Dictionary<string, GameSaveSnapshot> _slots =
            new Dictionary<string, GameSaveSnapshot>(StringComparer.Ordinal);

        public bool Exists(string slotId)
        {
            return !string.IsNullOrWhiteSpace(slotId) && _slots.ContainsKey(slotId);
        }

        public OperationResult Save(string slotId, GameSaveSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(slotId))
            {
                return OperationResult.Failure("save.slot_invalid");
            }

            if (!IsValid(snapshot))
            {
                return OperationResult.Failure("save.snapshot_invalid");
            }

            _slots[slotId] = Clone(snapshot);
            return OperationResult.Success();
        }

        public OperationResult<GameSaveSnapshot> Load(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
            {
                return OperationResult<GameSaveSnapshot>.Failure("save.slot_invalid");
            }

            if (!_slots.TryGetValue(slotId, out GameSaveSnapshot snapshot))
            {
                return OperationResult<GameSaveSnapshot>.Failure("save.slot_missing");
            }

            return OperationResult<GameSaveSnapshot>.Success(Clone(snapshot));
        }

        private static bool IsValid(GameSaveSnapshot snapshot)
        {
            return snapshot != null
                && snapshot.SchemaVersion == GameSaveSnapshot.CurrentSchemaVersion
                && snapshot.Clock.Day >= 1
                && snapshot.Clock.MinuteOfDay >= 0
                && snapshot.Clock.MinuteOfDay < InMemoryTimeService.MinutesPerDay
                && snapshot.Wallet.Balance >= 0
                && snapshot.Inventory != null
                && snapshot.Farm != null
                && snapshot.Livestock != null;
        }

        private static GameSaveSnapshot Clone(GameSaveSnapshot snapshot)
        {
            var inventory = new InventorySnapshot(snapshot.Inventory.Items.ToArray());
            var farm = new FarmSnapshot(
                snapshot.Farm.LastProcessedDay,
                snapshot.Farm.Plots.ToArray());
            var livestock = new LivestockSnapshot(
                snapshot.Livestock.LastProcessedDay,
                snapshot.Livestock.Animals.ToArray());

            return new GameSaveSnapshot(
                snapshot.SchemaVersion,
                snapshot.Clock,
                inventory,
                snapshot.Wallet,
                farm,
                livestock);
        }
    }
}
