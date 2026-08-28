using System;
using System.Collections.Generic;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Fishing;
using CozyTown.Runtime.Inventory;
using CozyTown.Tests.EditMode.TestDoubles;
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

        [TestCase(9)]
        [TestCase(20)]
        public void Catch_WhenRollIsOutsideHalfOpenRange_AddsNothing(int roll)
        {
            var inventory = CreateInventory();
            var fishing = CreateFishing(inventory);

            OperationResult<FishingCatch> result = fishing.Catch(roll);

            Assert.That(result.ErrorCode, Is.EqualTo("fishing.roll_has_no_catch"));
            Assert.That(inventory.Count("river-fish"), Is.Zero);
        }

        [Test]
        public void Catch_WhenInventoryMutatesThenFails_RestoresCaughtItem()
        {
            var inventory = new FaultInjectingInventory(CreateInventory())
            {
                FailAddAfterMutation = true
            };
            var fishing = CreateFishing(inventory);

            OperationResult<FishingCatch> result = fishing.Catch(10);

            Assert.That(result.ErrorCode, Is.EqualTo("injected.add_failure"));
            Assert.That(inventory.Count("river-fish"), Is.Zero);
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Catch_WhenInventoryRollbackFails_ReturnsStableDiagnostic()
        {
            var inventory = new FaultInjectingInventory(CreateInventory())
            {
                FailAddAfterMutation = true,
                FailRestore = true
            };
            var fishing = CreateFishing(inventory);

            OperationResult<FishingCatch> result = fishing.Catch(10);

            Assert.That(result.ErrorCode, Is.EqualTo("fishing.rollback_inventory_failed"));
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Entries_CannotBeCastToMutableArrayOrList()
        {
            var fishing = CreateFishing(CreateInventory());

            Assert.That(fishing.Entries, Is.Not.InstanceOf<FishingEntry[]>());
            Assert.Throws<NotSupportedException>(() =>
                ((IList<FishingEntry>)fishing.Entries)[0] =
                    new FishingEntry("changed", "river-fish", 0, 1));
            Assert.That(((IList<FishingEntry>)fishing.Entries)[0].FishId, Is.EqualTo("carp"));
        }

        private static InMemoryInventory CreateInventory()
        {
            return new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("river-fish", "River Fish", ItemCategory.Fish, 99)
                },
                capacitySlots: 2);
        }

        private static InMemoryFishingService CreateFishing(IInventory inventory)
        {
            return new InMemoryFishingService(
                new[] { new FishingEntry("carp", "river-fish", 10, 20) },
                inventory);
        }
    }
}
