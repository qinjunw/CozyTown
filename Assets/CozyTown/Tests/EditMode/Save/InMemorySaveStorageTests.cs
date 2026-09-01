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
            Assert.That(loaded.Value.WorldSeed, Is.EqualTo(12345));
            Assert.That(loaded.Value.Characters[0].Wallet.Balance, Is.EqualTo(25));
            Assert.That(
                loaded.Value.Characters[0].Backpack.Items[0].ItemId,
                Is.EqualTo("potato"));
            Assert.That(
                loaded.Value.Characters[0].Backpack.Items[0].Quantity,
                Is.EqualTo(2));
            Assert.That(loaded.Value.Shops[0].Wallet.Balance, Is.EqualTo(10000));
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
                worldSeed: 12345,
                new GameClockSnapshot(3, 7 * 60),
                new[]
                {
                    new CharacterEconomySnapshot(
                        "character.player",
                        new InventorySnapshot(sourceItems),
                        new WalletSnapshot(25))
                },
                new[]
                {
                    new ShopEconomySnapshot(
                        "shop.town.general",
                        new InventorySnapshot(
                            new[] { new ItemStack("seed.potato", 4) }),
                        new WalletSnapshot(10000),
                        lastRestockedDay: 3,
                        restockAlgorithmVersion: 1)
                },
                new FarmSnapshot(3, sourcePlots),
                new LivestockSnapshot(3, sourceAnimals));
            var storage = new InMemorySaveStorage();

            Assert.That(storage.Save("main", snapshot).IsSuccess, Is.True);
            sourceItems[0] = new ItemStack("potato", 99);
            sourcePlots[0] = new FarmPlotSnapshot("plot-1", string.Empty, 0, false, FarmPlotStatus.Empty);
            sourceAnimals[0] = new AnimalSnapshot("hen-1", "chicken", false, true);

            var firstLoad = storage.Load("main");
            Assert.That(firstLoad.IsSuccess, Is.True);
            Assert.That(
                firstLoad.Value.Characters[0].Backpack.Items[0].Quantity,
                Is.EqualTo(2));
            Assert.That(firstLoad.Value.Farm.Plots[0].Status, Is.EqualTo(FarmPlotStatus.Growing));
            Assert.That(firstLoad.Value.Livestock.Animals[0].FedToday, Is.True);
            CharacterEconomySnapshot[] loadedCharacters = firstLoad.Value.Characters;
            loadedCharacters[0] = null;

            var secondLoad = storage.Load("main");
            Assert.That(secondLoad.IsSuccess, Is.True);
            Assert.That(
                secondLoad.Value.Characters[0].Backpack.Items[0].Quantity,
                Is.EqualTo(2));
            Assert.That(secondLoad.Value.Characters[0], Is.Not.Null);
        }

        [Test]
        public void SnapshotConstructors_WhenCallerMutatesArrays_PreserveCapturedValues()
        {
            var items = new[] { new ItemStack("potato", 2) };
            var plots = new[]
            {
                new FarmPlotSnapshot(
                    "plot-1",
                    "potato-crop",
                    1,
                    true,
                    FarmPlotStatus.Growing)
            };
            var animals = new[]
            {
                new AnimalSnapshot("hen-1", "chicken", true, false)
            };
            var inventory = new InventorySnapshot(items);
            var farm = new FarmSnapshot(3, plots);
            var livestock = new LivestockSnapshot(3, animals);

            items[0] = new ItemStack("potato", 99);
            plots[0] = new FarmPlotSnapshot(
                "plot-1",
                string.Empty,
                0,
                false,
                FarmPlotStatus.Empty);
            animals[0] = new AnimalSnapshot("hen-1", "chicken", false, true);

            Assert.That(inventory.Items[0].Quantity, Is.EqualTo(2));
            Assert.That(farm.Plots[0].Status, Is.EqualTo(FarmPlotStatus.Growing));
            Assert.That(farm.Plots[0].WateredToday, Is.True);
            Assert.That(livestock.Animals[0].FedToday, Is.True);
            Assert.That(livestock.Animals[0].ProductReady, Is.False);
        }

        [Test]
        public void Save_WhenCharacterBackpackIsMissing_ReturnsPayloadError()
        {
            GameSaveSnapshot valid = SaveTestSnapshots.Create();
            var invalid = new GameSaveSnapshot(
                valid.SchemaVersion,
                valid.WorldSeed,
                valid.Clock,
                new[]
                {
                    new CharacterEconomySnapshot(
                        "character.player",
                        backpack: null,
                        new WalletSnapshot(25))
                },
                valid.Shops,
                valid.Farm,
                valid.Livestock);
            var storage = new InMemorySaveStorage();

            var result = storage.Save("main", invalid);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.payload_invalid"));
        }

        private static GameSaveSnapshot CreateSnapshot()
        {
            return SaveTestSnapshots.Create();
        }
    }
}
