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
        private Dictionary<string, PlotState> _plots;
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

            InventorySnapshot inventoryBefore = _inventory.CaptureSnapshot();
            if (inventoryBefore == null)
            {
                return OperationResult.Failure("farm.inventory_snapshot_invalid");
            }

            OperationResult consumeSeed = _inventory.Remove(seedItemId, 1);
            if (!consumeSeed.IsSuccess)
            {
                return RollBackInventory(inventoryBefore, consumeSeed.ErrorCode);
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
            OperationResult<FarmSnapshot> candidate = CreateDayCandidate(CaptureSnapshot(), newDay);
            return candidate.IsSuccess
                ? Restore(candidate.Value)
                : OperationResult.Failure(candidate.ErrorCode);
        }

        internal OperationResult<FarmSnapshot> CreateDayCandidate(FarmSnapshot current, int newDay)
        {
            OperationResult<Action> prepared = PrepareRestore(current);
            if (!prepared.IsSuccess)
            {
                return OperationResult<FarmSnapshot>.Failure(prepared.ErrorCode);
            }

            if (newDay <= current.LastProcessedDay)
            {
                return OperationResult<FarmSnapshot>.Failure("farm.day_not_advanced");
            }

            if (current.LastProcessedDay == int.MaxValue || newDay != current.LastProcessedDay + 1)
            {
                return OperationResult<FarmSnapshot>.Failure("farm.day_not_consecutive");
            }

            var plots = new FarmPlotSnapshot[current.Plots.Length];
            for (int index = 0; index < current.Plots.Length; index++)
            {
                FarmPlotSnapshot plot = current.Plots[index];
                int growth = plot.GrowthProgressDays;
                FarmPlotStatus status = plot.Status;
                if (plot.Status == FarmPlotStatus.Growing && plot.WateredToday)
                {
                    growth++;
                    CropDefinition crop = _cropsById[plot.CropId];
                    if (growth >= crop.GrowthDays)
                    {
                        status = FarmPlotStatus.Ready;
                    }
                }

                plots[index] = new FarmPlotSnapshot(
                    plot.PlotId, plot.CropId, growth, wateredToday: false, status: status);
            }

            return OperationResult<FarmSnapshot>.Success(new FarmSnapshot(newDay, plots));
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
            InventorySnapshot inventoryBefore = _inventory.CaptureSnapshot();
            if (inventoryBefore == null)
            {
                return OperationResult.Failure("farm.inventory_snapshot_invalid");
            }

            OperationResult addHarvest = _inventory.Add(crop.HarvestItemId, crop.HarvestQuantity);
            if (!addHarvest.IsSuccess)
            {
                return RollBackInventory(inventoryBefore, addHarvest.ErrorCode);
            }

            plot.Clear();
            return OperationResult.Success();
        }

        private OperationResult RollBackInventory(
            InventorySnapshot snapshot,
            string originalError)
        {
            OperationResult restore = _inventory.Restore(snapshot);
            return restore.IsSuccess
                ? OperationResult.Failure(originalError)
                : OperationResult.Failure("farm.rollback_inventory_failed");
        }

        public FarmSnapshot CaptureSnapshot()
        {
            return new FarmSnapshot(_lastProcessedDay, Plots.ToArray());
        }

        public OperationResult Restore(FarmSnapshot snapshot)
        {
            OperationResult<Action> prepared = PrepareRestore(snapshot);
            if (!prepared.IsSuccess)
            {
                return OperationResult.Failure(prepared.ErrorCode);
            }

            prepared.Value();
            return OperationResult.Success();
        }

        internal OperationResult<Action> PrepareRestore(FarmSnapshot snapshot)
        {
            if (snapshot == null || snapshot.LastProcessedDay < 1 || snapshot.Plots.Length != _plots.Count)
            {
                return OperationResult<Action>.Failure("farm.snapshot_invalid");
            }

            var proposed = new Dictionary<string, PlotState>(StringComparer.Ordinal);
            foreach (FarmPlotSnapshot plot in snapshot.Plots)
            {
                if (!_plots.ContainsKey(plot.PlotId ?? string.Empty)
                    || proposed.ContainsKey(plot.PlotId)
                    || !IsValidPlotSnapshot(plot))
                {
                    return OperationResult<Action>.Failure("farm.snapshot_invalid");
                }

                proposed.Add(plot.PlotId, new PlotState(plot.PlotId)
                {
                    CropId = plot.CropId,
                    GrowthProgressDays = plot.GrowthProgressDays,
                    WateredToday = plot.WateredToday,
                    Status = plot.Status
                });
            }

            int completedDay = snapshot.LastProcessedDay;
            return OperationResult<Action>.Success(() =>
            {
                _plots = proposed;
                _lastProcessedDay = completedDay;
            });
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
