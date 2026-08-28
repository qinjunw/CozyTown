using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Save
{
    public sealed class InMemorySaveStorageTests
    {
        [Test]
        public void SaveAndLoad_RoundTripsEveryModuleSnapshot()
        {
            var storage = new InMemorySaveStorage();
            GameSaveSnapshot snapshot = CreateSnapshot();

            Assert.That(storage.Save("main", snapshot).IsSuccess, Is.True);
            var loaded = storage.Load("main");

            Assert.That(loaded.IsSuccess, Is.True);
            Assert.That(loaded.Value.SchemaVersion, Is.EqualTo(GameSaveSnapshot.CurrentSchemaVersion));
            Assert.That(loaded.Value.Clock.Day, Is.EqualTo(3));
            Assert.That(loaded.Value.Clock.MinuteOfDay, Is.EqualTo(7 * 60));
            Assert.That(loaded.Value.Wallet.Balance, Is.EqualTo(25));
            Assert.That(loaded.Value.Inventory.Items[0].ItemId, Is.EqualTo("potato"));
            Assert.That(loaded.Value.Inventory.Items[0].Quantity, Is.EqualTo(2));
            Assert.That(loaded.Value.Farm.LastProcessedDay, Is.EqualTo(3));
            Assert.That(loaded.Value.Farm.Plots[0].Status, Is.EqualTo(FarmPlotStatus.Growing));
            Assert.That(loaded.Value.Livestock.LastProcessedDay, Is.EqualTo(3));
            Assert.That(loaded.Value.Livestock.Animals[0].FedToday, Is.True);
        }

        [Test]
        public void SaveAndLoad_CopyArraysAtBothStorageBoundaries()
        {
            var sourceItems = new[] { new ItemStack("potato", 2) };
            var sourcePlots =
                new[]
                {
                    new FarmPlotSnapshot(
                        "plot-1",
                        "potato-crop",
                        growthProgressDays: 1,
                        wateredToday: true,
                        status: FarmPlotStatus.Growing)
                };
            var sourceAnimals =
                new[] { new AnimalSnapshot("hen-1", "chicken", fedToday: true, productReady: false) };
            var snapshot = new GameSaveSnapshot(
                GameSaveSnapshot.CurrentSchemaVersion,
                new GameClockSnapshot(3, 7 * 60),
                new InventorySnapshot(sourceItems),
                new WalletSnapshot(25),
                new FarmSnapshot(3, sourcePlots),
                new LivestockSnapshot(3, sourceAnimals));
            var storage = new InMemorySaveStorage();

            Assert.That(storage.Save("main", snapshot).IsSuccess, Is.True);
            sourceItems[0] = new ItemStack("potato", 99);
            sourcePlots[0] = new FarmPlotSnapshot("plot-1", string.Empty, 0, false, FarmPlotStatus.Empty);
            sourceAnimals[0] = new AnimalSnapshot("hen-1", "chicken", false, true);

            var firstLoad = storage.Load("main");
            Assert.That(firstLoad.IsSuccess, Is.True);
            Assert.That(firstLoad.Value.Inventory.Items[0].Quantity, Is.EqualTo(2));
            Assert.That(firstLoad.Value.Farm.Plots[0].Status, Is.EqualTo(FarmPlotStatus.Growing));
            Assert.That(firstLoad.Value.Livestock.Animals[0].FedToday, Is.True);
            firstLoad.Value.Inventory.Items[0] = new ItemStack("potato", 77);

            var secondLoad = storage.Load("main");
            Assert.That(secondLoad.IsSuccess, Is.True);
            Assert.That(secondLoad.Value.Inventory.Items[0].Quantity, Is.EqualTo(2));
            Assert.That(secondLoad.Value.Inventory.Items, Is.Not.SameAs(firstLoad.Value.Inventory.Items));
        }

        private static GameSaveSnapshot CreateSnapshot()
        {
            return new GameSaveSnapshot(
                GameSaveSnapshot.CurrentSchemaVersion,
                new GameClockSnapshot(3, 7 * 60),
                new InventorySnapshot(new[] { new ItemStack("potato", 2) }),
                new WalletSnapshot(25),
                new FarmSnapshot(
                    3,
                    new[]
                    {
                        new FarmPlotSnapshot(
                            "plot-1",
                            "potato-crop",
                            growthProgressDays: 1,
                            wateredToday: true,
                            status: FarmPlotStatus.Growing)
                    }),
                new LivestockSnapshot(
                    3,
                    new[]
                    {
                        new AnimalSnapshot(
                            "hen-1",
                            "chicken",
                            fedToday: true,
                            productReady: false)
                    }));
        }
    }
}
