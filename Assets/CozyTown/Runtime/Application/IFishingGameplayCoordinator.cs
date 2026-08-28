using CozyTown.Runtime.Core;
using CozyTown.Runtime.Fishing;

namespace CozyTown.Runtime.Application
{
    public interface IFishingGameplayCoordinator
    {
        FishingViewState GetCurrentState();

        OperationResult<FishingCatch> Catch(int roll);
    }
}
