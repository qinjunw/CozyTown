using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Application
{
    public sealed class FarmGameplayCoordinator : IFarmGameplayCoordinator
    {
        private readonly IReadOnlyDictionary<string, CropDefinition> _crops;
        private readonly IReadOnlyDictionary<string, string> _displayNames;
        private readonly IFarmService _farm;
        private readonly IInventory _inventory;

        public FarmGameplayCoordinator(
            IEnumerable<ItemDefinition> items,
            IEnumerable<CropDefinition> crops,
            IFarmService farm,
            IInventory inventory)
        {
            _farm = farm ?? throw new ArgumentNullException(nameof(farm));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _displayNames = BuildDisplayNames(items);
            _crops = BuildCrops(crops, _displayNames);
        }

        public FarmViewState GetCurrentState()
        {
            FarmPlotView[] plots = _farm.Plots
                .OrderBy(plot => plot.PlotId, StringComparer.Ordinal)
                .Select(ToPlotView)
                .ToArray();
            FarmSeedOption[] seeds = _crops.Values
                .OrderBy(crop => crop.Id, StringComparer.Ordinal)
                .Select(crop => new FarmSeedOption(
                    crop.Id,
                    crop.SeedItemId,
                    _displayNames[crop.SeedItemId],
                    _inventory.Count(crop.SeedItemId),
                    crop.GrowthDays,
                    crop.HarvestQuantity))
                .ToArray();
            return new FarmViewState(plots, seeds);
        }

        public OperationResult Plant(string plotId, string seedItemId) =>
            _farm.Plant(plotId, seedItemId);

        public OperationResult Water(string plotId) => _farm.Water(plotId);

        public OperationResult Harvest(string plotId) => _farm.Harvest(plotId);

        private FarmPlotView ToPlotView(FarmPlotSnapshot plot)
        {
            if (plot.Status == FarmPlotStatus.Empty)
            {
                return new FarmPlotView(
                    plot.PlotId,
                    string.Empty,
                    string.Empty,
                    plot.Status,
                    plot.GrowthProgressDays,
                    growthDays: 0,
                    wateredToday: plot.WateredToday);
            }

            if (!_crops.TryGetValue(plot.CropId ?? string.Empty, out CropDefinition crop))
            {
                throw new InvalidOperationException(
                    $"Farm plot '{plot.PlotId}' references an unknown crop.");
            }

            return new FarmPlotView(
                plot.PlotId,
                crop.Id,
                _displayNames[crop.HarvestItemId],
                plot.Status,
                plot.GrowthProgressDays,
                crop.GrowthDays,
                plot.WateredToday);
        }

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

        private static IReadOnlyDictionary<string, CropDefinition> BuildCrops(
            IEnumerable<CropDefinition> crops,
            IReadOnlyDictionary<string, string> displayNames)
        {
            if (crops == null)
            {
                throw new ArgumentNullException(nameof(crops));
            }

            var result = new Dictionary<string, CropDefinition>(StringComparer.Ordinal);
            foreach (CropDefinition crop in crops)
            {
                if (crop == null
                    || string.IsNullOrWhiteSpace(crop.Id)
                    || !displayNames.ContainsKey(crop.SeedItemId ?? string.Empty)
                    || !displayNames.ContainsKey(crop.HarvestItemId ?? string.Empty)
                    || !result.TryAdd(crop.Id, crop))
                {
                    throw new ArgumentException(
                        "Crop definitions must have unique IDs and known items.",
                        nameof(crops));
                }
            }

            return result;
        }
    }
}
