using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Tests.EditMode.TestDoubles;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Farming
{
    public sealed class InMemoryFarmServiceTests
    {
        [Test]
        public void AdvanceDay_WhenGrowingPlotWasWatered_MaturesAndCanBeHarvested()
        {
            var inventory = new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("potato-seed", "Potato Seed", ItemCategory.Seed, maxStack: 99),
                    new ItemDefinition("potato", "Potato", ItemCategory.Crop, maxStack: 99)
                },
                capacitySlots: 4);
            Assert.That(inventory.Add("potato-seed", 1).IsSuccess, Is.True);
            var farm = new InMemoryFarmService(
                new[] { "plot-1" },
                new[]
                {
                    new CropDefinition(
                        "potato-crop",
                        "potato-seed",
                        "potato",
                        growthDays: 1,
                        harvestQuantity: 2)
                },
                inventory,
                startingDay: 1);

            Assert.That(farm.Plant("plot-1", "potato-seed").IsSuccess, Is.True);
            Assert.That(farm.Water("plot-1").IsSuccess, Is.True);
            Assert.That(farm.AdvanceDay(2).IsSuccess, Is.True);

            FarmPlotSnapshot maturePlot = farm.Plots.Single();
            Assert.That(maturePlot.Status, Is.EqualTo(FarmPlotStatus.Ready));
            Assert.That(maturePlot.GrowthProgressDays, Is.EqualTo(1));
            Assert.That(maturePlot.WateredToday, Is.False);

            Assert.That(farm.Harvest("plot-1").IsSuccess, Is.True);
            Assert.That(inventory.Count("potato"), Is.EqualTo(2));
            Assert.That(farm.Plots.Single().Status, Is.EqualTo(FarmPlotStatus.Empty));
        }

        [TestCase(999, 0)]
        [TestCase((int)FarmPlotStatus.Growing, 2)]
        public void Restore_WhenPlotStateIsUndefinedOrUnreachable_RejectsSnapshot(
            int statusValue,
            int growthProgressDays)
        {
            var inventory = new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("potato-seed", "Potato Seed", ItemCategory.Seed, maxStack: 99),
                    new ItemDefinition("potato", "Potato", ItemCategory.Crop, maxStack: 99)
                },
                capacitySlots: 4);
            var farm = new InMemoryFarmService(
                new[] { "plot-1" },
                new[]
                {
                    new CropDefinition(
                        "potato-crop",
                        "potato-seed",
                        "potato",
                        growthDays: 2,
                        harvestQuantity: 1)
                },
                inventory,
                startingDay: 1);
            var invalidSnapshot = new FarmSnapshot(
                lastProcessedDay: 1,
                plots: new[]
                {
                    new FarmPlotSnapshot(
                        "plot-1",
                        "potato-crop",
                        growthProgressDays,
                        wateredToday: false,
                        status: (FarmPlotStatus)statusValue)
                });

            var result = farm.Restore(invalidSnapshot);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("farm.snapshot_invalid"));
            Assert.That(farm.Plots.Single().Status, Is.EqualTo(FarmPlotStatus.Empty));
        }

        [Test]
        public void AdvanceDay_WhenDayIsRepeatedOrSkipped_LeavesStateUnchanged()
        {
            var inventory = CreateInventory();
            var farm = CreateFarm(inventory);
            FarmSnapshot before = farm.CaptureSnapshot();

            OperationResult repeated = farm.AdvanceDay(1);
            OperationResult skipped = farm.AdvanceDay(3);

            Assert.That(repeated.ErrorCode, Is.EqualTo("farm.day_not_advanced"));
            Assert.That(skipped.ErrorCode, Is.EqualTo("farm.day_not_consecutive"));
            Assert.That(farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(before.LastProcessedDay));
            Assert.That(farm.Plots.Single().Status, Is.EqualTo(FarmPlotStatus.Empty));
        }

        [Test]
        public void Plant_WhenInventoryMutatesThenFails_RestoresSeedAndPlot()
        {
            var inner = CreateInventory();
            Assert.That(inner.Add("potato-seed", 1).IsSuccess, Is.True);
            var inventory = new FaultInjectingInventory(inner)
            {
                FailRemoveAfterMutation = true
            };
            var farm = CreateFarm(inventory);

            OperationResult result = farm.Plant("plot-1", "potato-seed");

            Assert.That(result.ErrorCode, Is.EqualTo("injected.remove_failure"));
            Assert.That(inventory.Count("potato-seed"), Is.EqualTo(1));
            Assert.That(farm.Plots.Single().Status, Is.EqualTo(FarmPlotStatus.Empty));
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Plant_WhenEmptyPlotHasNoSeed_LeavesInventoryAndPlotUnchanged()
        {
            var inventory = CreateInventory();
            var farm = CreateFarm(inventory);
            FarmSnapshot before = farm.CaptureSnapshot();

            OperationResult result = farm.Plant("plot-1", "potato-seed");

            FarmSnapshot after = farm.CaptureSnapshot();
            Assert.That(result.ErrorCode, Is.EqualTo("inventory.insufficient_quantity"));
            Assert.That(inventory.Count("potato-seed"), Is.Zero);
            Assert.That(after.LastProcessedDay, Is.EqualTo(before.LastProcessedDay));
            Assert.That(after.Plots[0].PlotId, Is.EqualTo(before.Plots[0].PlotId));
            Assert.That(after.Plots[0].CropId, Is.EqualTo(before.Plots[0].CropId));
            Assert.That(
                after.Plots[0].GrowthProgressDays,
                Is.EqualTo(before.Plots[0].GrowthProgressDays));
            Assert.That(after.Plots[0].WateredToday, Is.EqualTo(before.Plots[0].WateredToday));
            Assert.That(after.Plots[0].Status, Is.EqualTo(before.Plots[0].Status));
        }

        [Test]
        public void Plant_WhenPlotIsOccupied_DoesNotConsumeSecondSeedOrChangePlot()
        {
            var inventory = CreateInventory();
            Assert.That(inventory.Add("potato-seed", 2).IsSuccess, Is.True);
            var farm = CreateFarm(inventory);
            Assert.That(farm.Plant("plot-1", "potato-seed").IsSuccess, Is.True);
            FarmPlotSnapshot before = farm.Plots.Single();

            OperationResult result = farm.Plant("plot-1", "potato-seed");

            FarmPlotSnapshot after = farm.Plots.Single();
            Assert.That(result.ErrorCode, Is.EqualTo("farm.plot_occupied"));
            Assert.That(inventory.Count("potato-seed"), Is.EqualTo(1));
            Assert.That(after.PlotId, Is.EqualTo(before.PlotId));
            Assert.That(after.CropId, Is.EqualTo(before.CropId));
            Assert.That(after.GrowthProgressDays, Is.EqualTo(before.GrowthProgressDays));
            Assert.That(after.WateredToday, Is.EqualTo(before.WateredToday));
            Assert.That(after.Status, Is.EqualTo(before.Status));
        }

        [Test]
        public void Harvest_WhenInventoryMutatesThenFails_RestoresInventoryAndReadyPlot()
        {
            var inner = CreateInventory();
            Assert.That(inner.Add("potato-seed", 1).IsSuccess, Is.True);
            var inventory = new FaultInjectingInventory(inner);
            var farm = CreateFarm(inventory);
            Assert.That(farm.Plant("plot-1", "potato-seed").IsSuccess, Is.True);
            Assert.That(farm.Water("plot-1").IsSuccess, Is.True);
            Assert.That(farm.AdvanceDay(2).IsSuccess, Is.True);
            inventory.FailAddAfterMutation = true;

            OperationResult result = farm.Harvest("plot-1");

            Assert.That(result.ErrorCode, Is.EqualTo("injected.add_failure"));
            Assert.That(inventory.Count("potato"), Is.Zero);
            Assert.That(farm.Plots.Single().Status, Is.EqualTo(FarmPlotStatus.Ready));
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Plant_WhenInventoryRollbackFails_ReturnsStableDiagnostic()
        {
            var inner = CreateInventory();
            Assert.That(inner.Add("potato-seed", 1).IsSuccess, Is.True);
            var inventory = new FaultInjectingInventory(inner)
            {
                FailRemoveAfterMutation = true,
                FailRestore = true
            };
            var farm = CreateFarm(inventory);

            OperationResult result = farm.Plant("plot-1", "potato-seed");

            Assert.That(result.ErrorCode, Is.EqualTo("farm.rollback_inventory_failed"));
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        private static InMemoryInventory CreateInventory()
        {
            return new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("potato-seed", "Potato Seed", ItemCategory.Seed, 99),
                    new ItemDefinition("potato", "Potato", ItemCategory.Crop, 99)
                },
                capacitySlots: 4);
        }

        private static InMemoryFarmService CreateFarm(IInventory inventory)
        {
            return new InMemoryFarmService(
                new[] { "plot-1" },
                new[]
                {
                    new CropDefinition("potato-crop", "potato-seed", "potato", 1, 2)
                },
                inventory,
                startingDay: 1);
        }
    }
}
