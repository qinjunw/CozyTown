using System;
using System.Collections.Generic;
using CozyTown.Runtime.Farming;

namespace CozyTown.Runtime.Application
{
    [Serializable]
    public sealed class FarmPlotView
    {
        public FarmPlotView(
            string plotId,
            string cropId,
            string cropDisplayName,
            FarmPlotStatus status,
            int growthProgressDays,
            int growthDays,
            bool wateredToday)
        {
            PlotId = plotId;
            CropId = cropId;
            CropDisplayName = cropDisplayName;
            Status = status;
            GrowthProgressDays = growthProgressDays;
            GrowthDays = growthDays;
            WateredToday = wateredToday;
        }

        public string PlotId { get; }
        public string CropId { get; }
        public string CropDisplayName { get; }
        public FarmPlotStatus Status { get; }
        public int GrowthProgressDays { get; }
        public int GrowthDays { get; }
        public bool WateredToday { get; }
    }

    [Serializable]
    public sealed class FarmSeedOption
    {
        public FarmSeedOption(
            string cropId,
            string seedItemId,
            string displayName,
            int ownedQuantity,
            int growthDays,
            int harvestQuantity)
        {
            CropId = cropId;
            SeedItemId = seedItemId;
            DisplayName = displayName;
            OwnedQuantity = ownedQuantity;
            GrowthDays = growthDays;
            HarvestQuantity = harvestQuantity;
        }

        public string CropId { get; }
        public string SeedItemId { get; }
        public string DisplayName { get; }
        public int OwnedQuantity { get; }
        public int GrowthDays { get; }
        public int HarvestQuantity { get; }
    }

    [Serializable]
    public sealed class FarmViewState
    {
        public FarmViewState(
            IEnumerable<FarmPlotView> plots,
            IEnumerable<FarmSeedOption> seedOptions)
        {
            Plots = Copy(plots);
            SeedOptions = Copy(seedOptions);
        }

        public IReadOnlyList<FarmPlotView> Plots { get; }
        public IReadOnlyList<FarmSeedOption> SeedOptions { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source)
        {
            T[] copy = source == null ? Array.Empty<T>() : new List<T>(source).ToArray();
            return Array.AsReadOnly(copy);
        }
    }
}
