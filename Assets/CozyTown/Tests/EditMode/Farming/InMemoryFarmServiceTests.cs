using System.Linq;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
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
    }
}
