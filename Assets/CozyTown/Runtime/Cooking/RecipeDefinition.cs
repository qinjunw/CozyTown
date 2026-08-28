using System;

namespace CozyTown.Runtime.Cooking
{
    [Serializable]
    public readonly struct RecipeIngredient
    {
        public RecipeIngredient(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public string ItemId { get; }

        public int Quantity { get; }
    }

    [Serializable]
    public sealed class RecipeDefinition
    {
        private readonly RecipeIngredient[] _ingredients;

        public RecipeDefinition(
            string id,
            RecipeIngredient[] ingredients,
            string outputItemId,
            int outputQuantity)
        {
            Id = id;
            _ingredients = ingredients == null || ingredients.Length == 0
                ? Array.Empty<RecipeIngredient>()
                : (RecipeIngredient[])ingredients.Clone();
            OutputItemId = outputItemId;
            OutputQuantity = outputQuantity;
        }

        public string Id { get; }

        public RecipeIngredient[] Ingredients => _ingredients.Length == 0
            ? Array.Empty<RecipeIngredient>()
            : (RecipeIngredient[])_ingredients.Clone();

        public string OutputItemId { get; }

        public int OutputQuantity { get; }
    }

    [Serializable]
    public readonly struct CookingResult
    {
        public CookingResult(string recipeId, string outputItemId, int outputQuantity)
        {
            RecipeId = recipeId;
            OutputItemId = outputItemId;
            OutputQuantity = outputQuantity;
        }

        public string RecipeId { get; }

        public string OutputItemId { get; }

        public int OutputQuantity { get; }
    }
}
