using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Application
{
    public interface ILivestockGameplayCoordinator
    {
        LivestockViewState GetCurrentState();

        OperationResult Feed(string animalId);

        OperationResult CollectProduct(string animalId);
    }
}
