using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Application
{
    public interface ICookingGameplayCoordinator
    {
        CookingViewState GetCurrentState();

        OperationResult<CookingResult> Cook(string recipeId);
    }
}
