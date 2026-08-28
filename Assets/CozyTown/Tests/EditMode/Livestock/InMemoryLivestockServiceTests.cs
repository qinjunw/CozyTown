using System.Linq;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
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
    }
}
