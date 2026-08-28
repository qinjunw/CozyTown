using System.Collections.Generic;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Farming
{
    public interface IFarmService
    {
        IReadOnlyCollection<FarmPlotSnapshot> Plots { get; }

        OperationResult Plant(string plotId, string seedItemId);

        OperationResult Water(string plotId);

        OperationResult AdvanceDay(int newDay);

        OperationResult Harvest(string plotId);

        FarmSnapshot CaptureSnapshot();

        OperationResult Restore(FarmSnapshot snapshot);
    }
}
