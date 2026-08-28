using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Cooking
{
    public sealed class InMemoryCookingService : ICookingService
    {
        private readonly Dictionary<string, RecipeDefinition> _recipes;
        private readonly IInventory _inventory;

        public InMemoryCookingService(IEnumerable<RecipeDefinition> recipes, IInventory inventory)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _recipes = (recipes ?? Array.Empty<RecipeDefinition>())
                .Where(IsValidRecipe)
                .GroupBy(recipe => recipe.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            Recipes = Array.AsReadOnly(
                _recipes.Values.OrderBy(recipe => recipe.Id, StringComparer.Ordinal).ToArray());
        }

        public IReadOnlyCollection<RecipeDefinition> Recipes { get; }

        public bool CanCook(string recipeId)
        {
            return _recipes.TryGetValue(recipeId ?? string.Empty, out RecipeDefinition recipe)
                && recipe.Ingredients.All(ingredient =>
                    _inventory.Contains(ingredient.ItemId, ingredient.Quantity));
        }

        public OperationResult<CookingResult> Cook(string recipeId)
        {
            if (!_recipes.TryGetValue(recipeId ?? string.Empty, out RecipeDefinition recipe))
            {
                return OperationResult<CookingResult>.Failure("cooking.recipe_missing");
            }

            if (!CanCook(recipeId))
            {
                return OperationResult<CookingResult>.Failure("cooking.ingredients_missing");
            }

            InventorySnapshot inventoryBefore = _inventory.CaptureSnapshot();
            if (inventoryBefore == null)
            {
                return OperationResult<CookingResult>.Failure(
                    "cooking.inventory_snapshot_invalid");
            }

            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                OperationResult remove = _inventory.Remove(ingredient.ItemId, ingredient.Quantity);
                if (!remove.IsSuccess)
                {
                    return RollBackInventory(inventoryBefore, remove.ErrorCode);
                }
            }

            OperationResult add = _inventory.Add(recipe.OutputItemId, recipe.OutputQuantity);
            if (!add.IsSuccess)
            {
                return RollBackInventory(inventoryBefore, add.ErrorCode);
            }

            return OperationResult<CookingResult>.Success(
                new CookingResult(recipe.Id, recipe.OutputItemId, recipe.OutputQuantity));
        }

        private OperationResult<CookingResult> RollBackInventory(
            InventorySnapshot snapshot,
            string originalError)
        {
            OperationResult restore = _inventory.Restore(snapshot);
            return OperationResult<CookingResult>.Failure(
                restore.IsSuccess
                    ? originalError
                    : "cooking.rollback_inventory_failed");
        }

        private static bool IsValidRecipe(RecipeDefinition recipe)
        {
            return recipe != null
                && !string.IsNullOrWhiteSpace(recipe.Id)
                && !string.IsNullOrWhiteSpace(recipe.OutputItemId)
                && recipe.OutputQuantity > 0
                && recipe.Ingredients.Length > 0
                && recipe.Ingredients.All(ingredient =>
                    !string.IsNullOrWhiteSpace(ingredient.ItemId) && ingredient.Quantity > 0)
                && recipe.Ingredients
                    .Select(ingredient => ingredient.ItemId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == recipe.Ingredients.Length;
        }
    }
}
