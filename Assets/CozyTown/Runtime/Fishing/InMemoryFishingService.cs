using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Fishing
{
    public sealed class InMemoryFishingService : IFishingService
    {
        private readonly FishingEntry[] _entries;
        private readonly IInventory _inventory;

        public InMemoryFishingService(IEnumerable<FishingEntry> entries, IInventory inventory)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _entries = (entries ?? Array.Empty<FishingEntry>())
                .Where(IsValidEntry)
                .OrderBy(entry => entry.MinRollInclusive)
                .ToArray();
            Entries = Array.AsReadOnly(_entries);
        }

        public IReadOnlyCollection<FishingEntry> Entries { get; }

        public OperationResult<FishingCatch> Catch(int roll)
        {
            FishingEntry entry = _entries.FirstOrDefault(candidate =>
                roll >= candidate.MinRollInclusive && roll < candidate.MaxRollExclusive);
            if (entry == null)
            {
                return OperationResult<FishingCatch>.Failure("fishing.roll_has_no_catch");
            }

            InventorySnapshot inventoryBefore = _inventory.CaptureSnapshot();
            if (inventoryBefore == null)
            {
                return OperationResult<FishingCatch>.Failure(
                    "fishing.inventory_snapshot_invalid");
            }

            OperationResult add = _inventory.Add(entry.ItemId, 1);
            if (!add.IsSuccess)
            {
                OperationResult restore = _inventory.Restore(inventoryBefore);
                return OperationResult<FishingCatch>.Failure(
                    restore.IsSuccess
                        ? add.ErrorCode
                        : "fishing.rollback_inventory_failed");
            }

            return OperationResult<FishingCatch>.Success(
                new FishingCatch(entry.FishId, entry.ItemId));
        }

        private static bool IsValidEntry(FishingEntry entry)
        {
            return entry != null
                && !string.IsNullOrWhiteSpace(entry.FishId)
                && !string.IsNullOrWhiteSpace(entry.ItemId)
                && entry.MinRollInclusive < entry.MaxRollExclusive;
        }
    }
}
