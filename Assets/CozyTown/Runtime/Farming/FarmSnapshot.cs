using System;

namespace CozyTown.Runtime.Farming
{
    public enum FarmPlotStatus
    {
        Empty = 0,
        Growing = 1,
        Ready = 2
    }

    [Serializable]
    public readonly struct FarmPlotSnapshot
    {
        public FarmPlotSnapshot(
            string plotId,
            string cropId,
            int growthProgressDays,
            bool wateredToday,
            FarmPlotStatus status)
        {
            PlotId = plotId;
            CropId = cropId;
            GrowthProgressDays = growthProgressDays;
            WateredToday = wateredToday;
            Status = status;
        }

        public string PlotId { get; }

        public string CropId { get; }

        public int GrowthProgressDays { get; }

        public bool WateredToday { get; }

        public FarmPlotStatus Status { get; }
    }

    [Serializable]
    public sealed class FarmSnapshot
    {
        public FarmSnapshot(int lastProcessedDay, FarmPlotSnapshot[] plots)
        {
            LastProcessedDay = lastProcessedDay;
            Plots = plots ?? Array.Empty<FarmPlotSnapshot>();
        }

        public int LastProcessedDay { get; }

        public FarmPlotSnapshot[] Plots { get; }
    }
}
