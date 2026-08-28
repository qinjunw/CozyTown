using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Farming
{
    public sealed class InMemoryFarmService : IFarmService
    {
        private readonly Dictionary<string, CropDefinition> _cropsById;
        private readonly Dictionary<string, CropDefinition> _cropsBySeedItemId;
        private readonly Dictionary<string, PlotState> _plots;
        private readonly IInventory _inventory;
        private int _lastProcessedDay;

        public InMemoryFarmService(
            IEnumerable<string> plotIds,
            IEnumerable<CropDefinition> crops,
            IInventory inventory,
            int startingDay = 1)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _lastProcessedDay = startingDay < 1 ? 1 : startingDay;
            _cropsById = (crops ?? Array.Empty<CropDefinition>())
                .Where(IsValidCrop)
                .GroupBy(crop => crop.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            _cropsBySeedItemId = _cropsById.Values
                .GroupBy(crop => crop.SeedItemId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            _plots = (plotIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(id => id, id => new PlotState(id), StringComparer.Ordinal);
        }

        public IReadOnlyCollection<FarmPlotSnapshot> Plots => _plots.Values
            .OrderBy(plot => plot.Id, StringComparer.Ordinal)
            .Select(ToSnapshot)
            .ToArray();

        public OperationResult Plant(string plotId, string seedItemId)
        {
            if (!_plots.TryGetValue(plotId ?? string.Empty, out PlotState plot))
            {
                return OperationResult.Failure("farm.plot_missing");
            }

            if (plot.Status != FarmPlotStatus.Empty)
            {
                return OperationResult.Failure("farm.plot_occupied");
            }

            if (!_cropsBySeedItemId.TryGetValue(seedItemId ?? string.Empty, out CropDefinition crop))
            {
                return OperationResult.Failure("farm.seed_unknown");
            }

            OperationResult consumeSeed = _inventory.Remove(seedItemId, 1);
            if (!consumeSeed.IsSuccess)
            {
                return consumeSeed;
            }

            plot.CropId = crop.Id;
            plot.GrowthProgressDays = 0;
            plot.WateredToday = false;
            plot.Status = FarmPlotStatus.Growing;
            return OperationResult.Success();
        }

        public OperationResult Water(string plotId)
        {
            if (!_plots.TryGetValue(plotId ?? string.Empty, out PlotState plot))
            {
                return OperationResult.Failure("farm.plot_missing");
            }

            if (plot.Status != FarmPlotStatus.Growing)
            {
                return OperationResult.Failure("farm.plot_not_growing");
            }

            plot.WateredToday = true;
            return OperationResult.Success();
        }

        public OperationResult AdvanceDay(int newDay)
        {
            if (newDay <= _lastProcessedDay)
            {
                return OperationResult.Failure("farm.day_not_advanced");
            }

            foreach (PlotState plot in _plots.Values)
            {
                if (plot.Status == FarmPlotStatus.Growing && plot.WateredToday)
                {
                    plot.GrowthProgressDays++;
                    CropDefinition crop = _cropsById[plot.CropId];
                    if (plot.GrowthProgressDays >= crop.GrowthDays)
                    {
                        plot.Status = FarmPlotStatus.Ready;
                    }
                }

                plot.WateredToday = false;
            }

            _lastProcessedDay = newDay;
            return OperationResult.Success();
        }

        public OperationResult Harvest(string plotId)
        {
            if (!_plots.TryGetValue(plotId ?? string.Empty, out PlotState plot))
            {
                return OperationResult.Failure("farm.plot_missing");
            }

            if (plot.Status != FarmPlotStatus.Ready)
            {
                return OperationResult.Failure("farm.crop_not_ready");
            }

            CropDefinition crop = _cropsById[plot.CropId];
            OperationResult addHarvest = _inventory.Add(crop.HarvestItemId, crop.HarvestQuantity);
            if (!addHarvest.IsSuccess)
            {
                return addHarvest;
            }

            plot.Clear();
            return OperationResult.Success();
        }

        public FarmSnapshot CaptureSnapshot()
        {
            return new FarmSnapshot(_lastProcessedDay, Plots.ToArray());
        }

        public OperationResult Restore(FarmSnapshot snapshot)
        {
            if (snapshot == null || snapshot.LastProcessedDay < 1 || snapshot.Plots.Length != _plots.Count)
            {
                return OperationResult.Failure("farm.snapshot_invalid");
            }

            var proposed = new Dictionary<string, FarmPlotSnapshot>(StringComparer.Ordinal);
            foreach (FarmPlotSnapshot plot in snapshot.Plots)
            {
                if (!_plots.ContainsKey(plot.PlotId ?? string.Empty)
                    || proposed.ContainsKey(plot.PlotId)
                    || !IsValidPlotSnapshot(plot))
                {
                    return OperationResult.Failure("farm.snapshot_invalid");
                }

                proposed.Add(plot.PlotId, plot);
            }

            foreach (KeyValuePair<string, FarmPlotSnapshot> pair in proposed)
            {
                PlotState target = _plots[pair.Key];
                FarmPlotSnapshot source = pair.Value;
                target.CropId = source.CropId;
                target.GrowthProgressDays = source.GrowthProgressDays;
                target.WateredToday = source.WateredToday;
                target.Status = source.Status;
            }

            _lastProcessedDay = snapshot.LastProcessedDay;
            return OperationResult.Success();
        }

        private bool IsValidPlotSnapshot(FarmPlotSnapshot plot)
        {
            if (!Enum.IsDefined(typeof(FarmPlotStatus), plot.Status))
            {
                return false;
            }

            if (plot.Status == FarmPlotStatus.Empty)
            {
                return string.IsNullOrEmpty(plot.CropId)
                    && plot.GrowthProgressDays == 0
                    && !plot.WateredToday;
            }

            if (!_cropsById.TryGetValue(plot.CropId ?? string.Empty, out CropDefinition crop))
            {
                return false;
            }

            if (plot.GrowthProgressDays < 0)
            {
                return false;
            }

            if (plot.Status == FarmPlotStatus.Growing)
            {
                return plot.GrowthProgressDays < crop.GrowthDays;
            }

            return plot.GrowthProgressDays == crop.GrowthDays && !plot.WateredToday;
        }

        private static bool IsValidCrop(CropDefinition crop)
        {
            return crop != null
                && !string.IsNullOrWhiteSpace(crop.Id)
                && !string.IsNullOrWhiteSpace(crop.SeedItemId)
                && !string.IsNullOrWhiteSpace(crop.HarvestItemId)
                && crop.GrowthDays > 0
                && crop.HarvestQuantity > 0;
        }

        private static FarmPlotSnapshot ToSnapshot(PlotState plot)
        {
            return new FarmPlotSnapshot(
                plot.Id,
                plot.CropId,
                plot.GrowthProgressDays,
                plot.WateredToday,
                plot.Status);
        }

        private sealed class PlotState
        {
            public PlotState(string id)
            {
                Id = id;
                Clear();
            }

            public string Id { get; }

            public string CropId { get; set; }

            public int GrowthProgressDays { get; set; }

            public bool WateredToday { get; set; }

            public FarmPlotStatus Status { get; set; }

            public void Clear()
            {
                CropId = string.Empty;
                GrowthProgressDays = 0;
                WateredToday = false;
                Status = FarmPlotStatus.Empty;
            }
        }
    }
}
