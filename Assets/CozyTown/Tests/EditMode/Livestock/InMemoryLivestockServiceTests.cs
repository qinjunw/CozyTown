using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Tests.EditMode.TestDoubles;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Livestock
{
    public sealed class InMemoryLivestockServiceTests
    {
        [Test]
        public void AdvanceDay_AfterFeedingChicken_ProducesOneCollectibleEgg()
        {
            var inventory = new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("chicken-feed", "Chicken Feed", ItemCategory.Feed, maxStack: 99),
                    new ItemDefinition("egg", "Egg", ItemCategory.AnimalProduct, maxStack: 99)
                },
                capacitySlots: 4);
            Assert.That(inventory.Add("chicken-feed", 1).IsSuccess, Is.True);
            var livestock = new InMemoryLivestockService(
                new[] { new AnimalSnapshot("hen-1", "chicken", fedToday: false, productReady: false) },
                new[]
                {
                    new AnimalDefinition(
                        "chicken",
                        "chicken-feed",
                        "egg",
                        productQuantity: 1)
                },
                inventory,
                startingDay: 1);

            Assert.That(livestock.Feed("hen-1").IsSuccess, Is.True);
            Assert.That(inventory.Count("chicken-feed"), Is.Zero);
            Assert.That(livestock.AdvanceDay(2).IsSuccess, Is.True);
            Assert.That(livestock.Animals.Single().ProductReady, Is.True);

            Assert.That(livestock.CollectProduct("hen-1").IsSuccess, Is.True);
            Assert.That(inventory.Count("egg"), Is.EqualTo(1));
            Assert.That(livestock.Animals.Single().ProductReady, Is.False);
        }

        [Test]
        public void Feed_WhenRepeatedOrProductIsPending_DoesNotConsumeMoreFeed()
        {
            var inventory = CreateInventory();
            Assert.That(inventory.Add("chicken-feed", 2).IsSuccess, Is.True);
            var livestock = CreateLivestock(inventory);

            Assert.That(livestock.Feed("hen-1").IsSuccess, Is.True);
            OperationResult repeated = livestock.Feed("hen-1");
            Assert.That(livestock.AdvanceDay(2).IsSuccess, Is.True);
            OperationResult pending = livestock.Feed("hen-1");

            Assert.That(repeated.ErrorCode, Is.EqualTo("livestock.already_fed"));
            Assert.That(pending.ErrorCode, Is.EqualTo("livestock.product_pending"));
            Assert.That(inventory.Count("chicken-feed"), Is.EqualTo(1));
            Assert.That(livestock.Animals.Single().ProductReady, Is.True);
        }

        [Test]
        public void AdvanceDay_WhenDayIsRepeatedOrSkipped_LeavesStateUnchanged()
        {
            var livestock = CreateLivestock(CreateInventory());

            OperationResult repeated = livestock.AdvanceDay(1);
            OperationResult skipped = livestock.AdvanceDay(3);

            Assert.That(repeated.ErrorCode, Is.EqualTo("livestock.day_not_advanced"));
            Assert.That(skipped.ErrorCode, Is.EqualTo("livestock.day_not_consecutive"));
            Assert.That(livestock.CaptureSnapshot().LastProcessedDay, Is.EqualTo(1));
            Assert.That(livestock.Animals.Single().ProductReady, Is.False);
        }

        [Test]
        public void Constructor_WhenAnimalIsFedAndProductReady_RejectsUnreachableState()
        {
            var livestock = new InMemoryLivestockService(
                new[] { new AnimalSnapshot("hen-1", "chicken", true, true) },
                new[] { new AnimalDefinition("chicken", "chicken-feed", "egg", 1) },
                CreateInventory(),
                startingDay: 1);

            Assert.That(livestock.Animals, Is.Empty);
        }

        [Test]
        public void CaptureThenRestore_RestoresServiceDayAndAnimalState()
        {
            var inventory = CreateInventory();
            Assert.That(inventory.Add("chicken-feed", 1).IsSuccess, Is.True);
            var livestock = CreateLivestock(inventory);
            Assert.That(livestock.Feed("hen-1").IsSuccess, Is.True);
            LivestockSnapshot before = livestock.CaptureSnapshot();
            Assert.That(livestock.AdvanceDay(2).IsSuccess, Is.True);

            OperationResult result = livestock.Restore(before);

            LivestockSnapshot after = livestock.CaptureSnapshot();
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(after.LastProcessedDay, Is.EqualTo(before.LastProcessedDay));
            Assert.That(after.Animals.Length, Is.EqualTo(before.Animals.Length));
            Assert.That(after.Animals[0].AnimalId, Is.EqualTo(before.Animals[0].AnimalId));
            Assert.That(after.Animals[0].SpeciesId, Is.EqualTo(before.Animals[0].SpeciesId));
            Assert.That(after.Animals[0].FedToday, Is.EqualTo(before.Animals[0].FedToday));
            Assert.That(after.Animals[0].ProductReady, Is.EqualTo(before.Animals[0].ProductReady));
        }

        [Test]
        public void AdvanceDay_WhenProductRemainsUncollectedOnThirdDay_DoesNotDuplicateIt()
        {
            var inventory = CreateInventory();
            Assert.That(inventory.Add("chicken-feed", 1).IsSuccess, Is.True);
            var livestock = CreateLivestock(inventory);
            Assert.That(livestock.Feed("hen-1").IsSuccess, Is.True);
            Assert.That(livestock.AdvanceDay(2).IsSuccess, Is.True);

            OperationResult thirdDay = livestock.AdvanceDay(3);

            Assert.That(thirdDay.IsSuccess, Is.True);
            Assert.That(livestock.Animals.Single().ProductReady, Is.True);
            Assert.That(livestock.CollectProduct("hen-1").IsSuccess, Is.True);
            OperationResult repeatedCollection = livestock.CollectProduct("hen-1");
            Assert.That(repeatedCollection.ErrorCode, Is.EqualTo("livestock.product_not_ready"));
            Assert.That(inventory.Count("egg"), Is.EqualTo(1));
        }

        [Test]
        public void Feed_WhenInventoryMutatesThenFails_RestoresFeedAndAnimal()
        {
            var inner = CreateInventory();
            Assert.That(inner.Add("chicken-feed", 1).IsSuccess, Is.True);
            var inventory = new FaultInjectingInventory(inner)
            {
                FailRemoveAfterMutation = true
            };
            var livestock = CreateLivestock(inventory);

            OperationResult result = livestock.Feed("hen-1");

            Assert.That(result.ErrorCode, Is.EqualTo("injected.remove_failure"));
            Assert.That(inventory.Count("chicken-feed"), Is.EqualTo(1));
            Assert.That(livestock.Animals.Single().FedToday, Is.False);
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void CollectProduct_WhenInventoryMutatesThenFails_RestoresReadyProduct()
        {
            var inner = CreateInventory();
            Assert.That(inner.Add("chicken-feed", 1).IsSuccess, Is.True);
            var inventory = new FaultInjectingInventory(inner);
            var livestock = CreateLivestock(inventory);
            Assert.That(livestock.Feed("hen-1").IsSuccess, Is.True);
            Assert.That(livestock.AdvanceDay(2).IsSuccess, Is.True);
            inventory.FailAddAfterMutation = true;

            OperationResult result = livestock.CollectProduct("hen-1");

            Assert.That(result.ErrorCode, Is.EqualTo("injected.add_failure"));
            Assert.That(inventory.Count("egg"), Is.Zero);
            Assert.That(livestock.Animals.Single().ProductReady, Is.True);
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Feed_WhenInventoryRollbackFails_ReturnsStableDiagnostic()
        {
            var inner = CreateInventory();
            Assert.That(inner.Add("chicken-feed", 1).IsSuccess, Is.True);
            var inventory = new FaultInjectingInventory(inner)
            {
                FailRemoveAfterMutation = true,
                FailRestore = true
            };
            var livestock = CreateLivestock(inventory);

            OperationResult result = livestock.Feed("hen-1");

            Assert.That(result.ErrorCode, Is.EqualTo("livestock.rollback_inventory_failed"));
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        private static InMemoryInventory CreateInventory()
        {
            return new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("chicken-feed", "Chicken Feed", ItemCategory.Feed, 99),
                    new ItemDefinition("egg", "Egg", ItemCategory.AnimalProduct, 99)
                },
                capacitySlots: 4);
        }

        private static InMemoryLivestockService CreateLivestock(IInventory inventory)
        {
            return new InMemoryLivestockService(
                new[] { new AnimalSnapshot("hen-1", "chicken", false, false) },
                new[] { new AnimalDefinition("chicken", "chicken-feed", "egg", 1) },
                inventory,
                startingDay: 1);
        }
    }
}
