using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Cooking
{
    public sealed class InMemoryCookingServiceTests
    {
        [Test]
        public void Cook_WhenOutputCannotFit_RestoresEveryConsumedIngredient()
        {
            var inventory = new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("potato", "Potato", ItemCategory.Crop, maxStack: 2),
                    new ItemDefinition("baked-potato", "Baked Potato", ItemCategory.Food, maxStack: 1)
                },
                capacitySlots: 1);
            Assert.That(inventory.Add("potato", 2).IsSuccess, Is.True);
            var cooking = new InMemoryCookingService(
                new[]
                {
                    new RecipeDefinition(
                        "baked-potato-recipe",
                        new[] { new RecipeIngredient("potato", 1) },
                        "baked-potato",
                        outputQuantity: 1)
                },
                inventory);

            OperationResult<CookingResult> result = cooking.Cook("baked-potato-recipe");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("inventory.capacity_exceeded"));
            Assert.That(inventory.Count("potato"), Is.EqualTo(2));
            Assert.That(inventory.Count("baked-potato"), Is.Zero);
        }
    }
}
