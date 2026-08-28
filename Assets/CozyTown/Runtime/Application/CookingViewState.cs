using System;
using System.Collections.Generic;

namespace CozyTown.Runtime.Application
{
    [Serializable]
    public sealed class RecipeIngredientView
    {
        public RecipeIngredientView(
            string itemId,
            string displayName,
            int requiredQuantity,
            int ownedQuantity)
        {
            ItemId = itemId;
            DisplayName = displayName;
            RequiredQuantity = requiredQuantity;
            OwnedQuantity = ownedQuantity;
        }

        public string ItemId { get; }
        public string DisplayName { get; }
        public int RequiredQuantity { get; }
        public int OwnedQuantity { get; }
    }

    [Serializable]
    public sealed class RecipeView
    {
        public RecipeView(
            string recipeId,
            string outputItemId,
            string outputDisplayName,
            int outputQuantity,
            bool hasIngredients,
            IEnumerable<RecipeIngredientView> ingredients)
        {
            RecipeId = recipeId;
            OutputItemId = outputItemId;
            OutputDisplayName = outputDisplayName;
            OutputQuantity = outputQuantity;
            HasIngredients = hasIngredients;
            RecipeIngredientView[] copy = ingredients == null
                ? Array.Empty<RecipeIngredientView>()
                : new List<RecipeIngredientView>(ingredients).ToArray();
            Ingredients = Array.AsReadOnly(copy);
        }

        public string RecipeId { get; }
        public string OutputItemId { get; }
        public string OutputDisplayName { get; }
        public int OutputQuantity { get; }
        public bool HasIngredients { get; }
        public IReadOnlyList<RecipeIngredientView> Ingredients { get; }
    }

    [Serializable]
    public sealed class CookingViewState
    {
        public CookingViewState(IEnumerable<RecipeView> recipes)
        {
            RecipeView[] copy = recipes == null
                ? Array.Empty<RecipeView>()
                : new List<RecipeView>(recipes).ToArray();
            Recipes = Array.AsReadOnly(copy);
        }

        public IReadOnlyList<RecipeView> Recipes { get; }
    }
}
