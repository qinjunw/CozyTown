using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Application
{
    public interface IFarmGameplayCoordinator
    {
        FarmViewState GetCurrentState();

        OperationResult Plant(string plotId, string seedItemId);

        OperationResult Water(string plotId);

        OperationResult Harvest(string plotId);
    }
}
