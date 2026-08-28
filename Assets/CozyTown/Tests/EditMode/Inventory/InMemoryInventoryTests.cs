using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Inventory
{
    public sealed class InMemoryInventoryTests
    {
        [Test]
        public void AddAndRemove_WhenCapacityOrQuantityIsExceeded_LeaveInventoryUnchanged()
        {
            var inventory = new InMemoryInventory(
                new[] { new ItemDefinition("potato", "Potato", ItemCategory.Crop, maxStack: 2) },
                capacitySlots: 1);

            OperationResult initialAdd = inventory.Add("potato", 2);
            OperationResult overCapacity = inventory.Add("potato", 1);
            OperationResult overRemoval = inventory.Remove("potato", 3);

            Assert.That(initialAdd.IsSuccess, Is.True);
            Assert.That(overCapacity.IsSuccess, Is.False);
            Assert.That(overCapacity.ErrorCode, Is.EqualTo("inventory.capacity_exceeded"));
            Assert.That(overRemoval.IsSuccess, Is.False);
            Assert.That(overRemoval.ErrorCode, Is.EqualTo("inventory.insufficient_quantity"));
            Assert.That(inventory.Count("potato"), Is.EqualTo(2));
        }

        [Test]
        public void Add_WhenStackSlotCalculationWouldOverflow_RejectsQuantityWithoutMutation()
        {
            var inventory = new InMemoryInventory(
                new[] { new ItemDefinition("potato", "Potato", ItemCategory.Crop, maxStack: 2) },
                capacitySlots: 1);

            OperationResult result = inventory.Add("potato", int.MaxValue);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("inventory.capacity_exceeded"));
            Assert.That(inventory.Count("potato"), Is.Zero);
        }
    }
}
