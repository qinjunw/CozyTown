using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Application
{
    public sealed class CookingGameplayCoordinator : ICookingGameplayCoordinator
    {
        private readonly ICookingService _cooking;
        private readonly IReadOnlyDictionary<string, string> _displayNames;
        private readonly IInventory _inventory;

        public CookingGameplayCoordinator(
            IEnumerable<ItemDefinition> items,
            ICookingService cooking,
            IInventory inventory)
        {
            _cooking = cooking ?? throw new ArgumentNullException(nameof(cooking));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _displayNames = BuildDisplayNames(items);

            if (_cooking.Recipes.Any(recipe =>
                    recipe == null
                    || !_displayNames.ContainsKey(recipe.OutputItemId ?? string.Empty)
                    || recipe.Ingredients.Any(ingredient =>
                        !_displayNames.ContainsKey(ingredient.ItemId ?? string.Empty))))
            {
                throw new ArgumentException(
                    "Every recipe must reference item definitions.",
                    nameof(items));
            }
        }

        public CookingViewState GetCurrentState()
        {
            RecipeView[] recipes = _cooking.Recipes
                .OrderBy(recipe => recipe.Id, StringComparer.Ordinal)
                .Select(ToRecipeView)
                .ToArray();
            return new CookingViewState(recipes);
        }

        public OperationResult<CookingResult> Cook(string recipeId) =>
            _cooking.Cook(recipeId);

        private RecipeView ToRecipeView(RecipeDefinition recipe)
        {
            RecipeIngredientView[] ingredients = recipe.Ingredients
                .OrderBy(ingredient => ingredient.ItemId, StringComparer.Ordinal)
                .Select(ingredient => new RecipeIngredientView(
                    ingredient.ItemId,
                    _displayNames[ingredient.ItemId],
                    ingredient.Quantity,
                    _inventory.Count(ingredient.ItemId)))
                .ToArray();
            return new RecipeView(
                recipe.Id,
                recipe.OutputItemId,
                _displayNames[recipe.OutputItemId],
                recipe.OutputQuantity,
                _cooking.CanCook(recipe.Id),
                ingredients);
        }

        private static IReadOnlyDictionary<string, string> BuildDisplayNames(
            IEnumerable<ItemDefinition> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item == null
                    || string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.DisplayName)
                    || !displayNames.TryAdd(item.Id, item.DisplayName))
                {
                    throw new ArgumentException(
                        "Item definitions must have unique IDs and display names.",
                        nameof(items));
                }
            }

            return displayNames;
        }
    }
}
