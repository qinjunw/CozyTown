using System.Collections.Generic;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Cooking
{
    public interface ICookingService
    {
        IReadOnlyCollection<RecipeDefinition> Recipes { get; }

        bool CanCook(string recipeId);

        OperationResult<CookingResult> Cook(string recipeId);
    }
}
