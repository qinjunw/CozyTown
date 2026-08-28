using CozyTown.Runtime.Core;
using CozyTown.Runtime.Fishing;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Fishing
{
    public sealed class InMemoryFishingServiceTests
    {
        [Test]
        public void Catch_WhenRollMatchesEntry_AddsConfiguredFishToInventory()
        {
            var inventory = new InMemoryInventory(
                new[] { new ItemDefinition("river-fish", "River Fish", ItemCategory.Fish, maxStack: 99) },
                capacitySlots: 2);
            var fishing = new InMemoryFishingService(
                new[] { new FishingEntry("carp", "river-fish", minRollInclusive: 10, maxRollExclusive: 20) },
                inventory);

            OperationResult<FishingCatch> result = fishing.Catch(15);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.FishId, Is.EqualTo("carp"));
            Assert.That(result.Value.ItemId, Is.EqualTo("river-fish"));
            Assert.That(inventory.Count("river-fish"), Is.EqualTo(1));
        }
    }
}
