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
        public RecipeDefinition(
            string id,
            RecipeIngredient[] ingredients,
            string outputItemId,
            int outputQuantity)
        {
            Id = id;
            Ingredients = ingredients ?? Array.Empty<RecipeIngredient>();
            OutputItemId = outputItemId;
            OutputQuantity = outputQuantity;
        }

        public string Id { get; }

        public RecipeIngredient[] Ingredients { get; }

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
