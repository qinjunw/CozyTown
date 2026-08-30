using System.Linq;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Inventory
{
    public sealed class InventoryProjectionTests
    {
        [Test]
        public void CaptureProjection_UsesCatalogOrderSplitsStacksAndPadsCapacity()
        {
            var inventory = new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("item.feed", "Feed", ItemCategory.Material, 2),
                    new ItemDefinition("item.egg", "Egg", ItemCategory.AnimalProduct, 10)
                },
                capacitySlots: 5);
            Assert.That(inventory.Add("item.feed", 5).IsSuccess, Is.True);
            Assert.That(inventory.Add("item.egg", 1).IsSuccess, Is.True);

            InventoryProjection projection = inventory.CaptureProjection();

            Assert.That(projection.CapacitySlots, Is.EqualTo(5));
            Assert.That(
                projection.Slots.Select(slot => slot.ItemId),
                Is.EqualTo(new[] { "item.feed", "item.feed", "item.feed", "item.egg", string.Empty }));
            Assert.That(
                projection.Slots.Select(slot => slot.Quantity),
                Is.EqualTo(new[] { 2, 2, 1, 1, 0 }));
            Assert.That(projection.Slots[0].DisplayName, Is.EqualTo("Feed"));
            Assert.That(projection.Slots[4].IsEmpty, Is.True);
        }
    }
}
