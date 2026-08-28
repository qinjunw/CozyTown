using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Fishing;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Application
{
    public sealed class FishingGameplayCoordinator : IFishingGameplayCoordinator
    {
        private readonly IReadOnlyDictionary<string, string> _displayNames;
        private readonly IFishingService _fishing;
        private readonly IInventory _inventory;

        public FishingGameplayCoordinator(
            IEnumerable<ItemDefinition> items,
            IFishingService fishing,
            IInventory inventory)
        {
            _fishing = fishing ?? throw new ArgumentNullException(nameof(fishing));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _displayNames = BuildDisplayNames(items);

            if (_fishing.Entries.Any(entry =>
                    entry == null
                    || !_displayNames.ContainsKey(entry.ItemId ?? string.Empty)))
            {
                throw new ArgumentException(
                    "Every fishing entry must reference an item definition.",
                    nameof(items));
            }
        }

        public FishingViewState GetCurrentState()
        {
            FishingEntryView[] entries = _fishing.Entries
                .OrderBy(entry => entry.FishId, StringComparer.Ordinal)
                .Select(entry => new FishingEntryView(
                    entry.FishId,
                    entry.ItemId,
                    _displayNames[entry.ItemId],
                    entry.MinRollInclusive,
                    entry.MaxRollExclusive,
                    _inventory.Count(entry.ItemId)))
                .ToArray();
            return new FishingViewState(entries);
        }

        public OperationResult<FishingCatch> Catch(int roll) => _fishing.Catch(roll);

        private static IReadOnlyDictionary<string, string> BuildDisplayNames(
            IEnumerable<ItemDefinition> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item == null
                    || string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.DisplayName)
                    || !displayNames.TryAdd(item.Id, item.DisplayName))
                {
                    throw new ArgumentException(
                        "Item definitions must have unique IDs and display names.",
                        nameof(items));
                }
            }

            return displayNames;
        }
    }
}
