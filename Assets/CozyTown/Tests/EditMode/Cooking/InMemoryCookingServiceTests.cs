using System;
using System.Collections.Generic;
using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;
using CozyTown.Tests.EditMode.TestDoubles;
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

        [Test]
        public void Cook_WhenIngredientsAreAvailable_ConvertsThemOnce()
        {
            var inventory = CreateInventory();
            Assert.That(inventory.Add("potato", 1).IsSuccess, Is.True);
            Assert.That(inventory.Add("salt", 1).IsSuccess, Is.True);
            var cooking = CreateCooking(inventory);

            OperationResult<CookingResult> result = cooking.Cook("baked-potato-recipe");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.OutputItemId, Is.EqualTo("baked-potato"));
            Assert.That(inventory.Count("potato"), Is.Zero);
            Assert.That(inventory.Count("salt"), Is.Zero);
            Assert.That(inventory.Count("baked-potato"), Is.EqualTo(1));
        }

        [Test]
        public void Cook_WhenSaltIsMissing_LeavesEveryRelevantItemUnchanged()
        {
            var inventory = CreateInventory();
            Assert.That(inventory.Add("potato", 1).IsSuccess, Is.True);
            var cooking = CreateCooking(inventory);

            OperationResult<CookingResult> result = cooking.Cook("baked-potato-recipe");

            Assert.That(result.ErrorCode, Is.EqualTo("cooking.ingredients_missing"));
            Assert.That(inventory.Count("potato"), Is.EqualTo(1));
            Assert.That(inventory.Count("salt"), Is.Zero);
            Assert.That(inventory.Count("baked-potato"), Is.Zero);
        }

        [Test]
        public void Cook_WhenRemoveMutatesThenFails_RestoresEveryIngredient()
        {
            var inner = CreateInventory();
            Assert.That(inner.Add("potato", 1).IsSuccess, Is.True);
            Assert.That(inner.Add("salt", 1).IsSuccess, Is.True);
            var inventory = new FaultInjectingInventory(inner)
            {
                FailRemoveAfterMutation = true
            };
            var cooking = CreateCooking(inventory);

            OperationResult<CookingResult> result = cooking.Cook("baked-potato-recipe");

            Assert.That(result.ErrorCode, Is.EqualTo("injected.remove_failure"));
            Assert.That(inventory.Count("potato"), Is.EqualTo(1));
            Assert.That(inventory.Count("salt"), Is.EqualTo(1));
            Assert.That(inventory.Count("baked-potato"), Is.Zero);
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Cook_WhenOutputAddMutatesThenFails_RestoresIngredientsAndOutput()
        {
            var inner = CreateInventory();
            Assert.That(inner.Add("potato", 1).IsSuccess, Is.True);
            Assert.That(inner.Add("salt", 1).IsSuccess, Is.True);
            var inventory = new FaultInjectingInventory(inner)
            {
                FailAddAfterMutation = true
            };
            var cooking = CreateCooking(inventory);

            OperationResult<CookingResult> result = cooking.Cook("baked-potato-recipe");

            Assert.That(result.ErrorCode, Is.EqualTo("injected.add_failure"));
            Assert.That(inventory.Count("potato"), Is.EqualTo(1));
            Assert.That(inventory.Count("salt"), Is.EqualTo(1));
            Assert.That(inventory.Count("baked-potato"), Is.Zero);
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Cook_WhenInventoryRollbackFails_ReturnsStableDiagnostic()
        {
            var inner = CreateInventory();
            Assert.That(inner.Add("potato", 1).IsSuccess, Is.True);
            Assert.That(inner.Add("salt", 1).IsSuccess, Is.True);
            var inventory = new FaultInjectingInventory(inner)
            {
                FailRemoveAfterMutation = true,
                FailRestore = true
            };
            var cooking = CreateCooking(inventory);

            OperationResult<CookingResult> result = cooking.Cook("baked-potato-recipe");

            Assert.That(result.ErrorCode, Is.EqualTo("cooking.rollback_inventory_failed"));
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void RecipeIngredients_WhenCallerMutatesReturnedArray_RemainUnchanged()
        {
            var recipe = new RecipeDefinition(
                "recipe",
                new[] { new RecipeIngredient("potato", 1) },
                "baked-potato",
                1);
            RecipeIngredient[] returned = recipe.Ingredients;

            returned[0] = new RecipeIngredient("changed", 99);

            Assert.That(recipe.Ingredients[0].ItemId, Is.EqualTo("potato"));
            Assert.That(recipe.Ingredients[0].Quantity, Is.EqualTo(1));
        }

        [Test]
        public void Recipes_CannotBeCastToMutableArrayOrList_AndCookRuleRemainsUnchanged()
        {
            var inventory = CreateInventory();
            Assert.That(inventory.Add("potato", 1).IsSuccess, Is.True);
            Assert.That(inventory.Add("salt", 1).IsSuccess, Is.True);
            var cooking = CreateCooking(inventory);
            var replacement = new RecipeDefinition(
                "changed-recipe",
                new[] { new RecipeIngredient("potato", 1) },
                "baked-potato",
                1);

            Assert.That(cooking.Recipes, Is.Not.InstanceOf<RecipeDefinition[]>());
            Assert.Throws<NotSupportedException>(() =>
                ((IList<RecipeDefinition>)cooking.Recipes)[0] = replacement);

            OperationResult<CookingResult> result = cooking.Cook("baked-potato-recipe");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.RecipeId, Is.EqualTo("baked-potato-recipe"));
            Assert.That(inventory.Count("potato"), Is.Zero);
            Assert.That(inventory.Count("salt"), Is.Zero);
            Assert.That(inventory.Count("baked-potato"), Is.EqualTo(1));
        }

        private static InMemoryInventory CreateInventory()
        {
            return new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("potato", "Potato", ItemCategory.Crop, 99),
                    new ItemDefinition("salt", "Salt", ItemCategory.Material, 99),
                    new ItemDefinition("baked-potato", "Baked Potato", ItemCategory.Food, 99)
                },
                capacitySlots: 4);
        }

        private static InMemoryCookingService CreateCooking(IInventory inventory)
        {
            return new InMemoryCookingService(
                new[]
                {
                    new RecipeDefinition(
                        "baked-potato-recipe",
                        new[]
                        {
                            new RecipeIngredient("potato", 1),
                            new RecipeIngredient("salt", 1)
                        },
                        "baked-potato",
                        1)
                },
                inventory);
        }
    }
}
