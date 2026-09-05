using CozyTown.Runtime.Core;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Save
{
    internal static class SaveTestSnapshots
    {
        public static GameSaveSnapshot Create(
            int day = 3,
            int minuteOfDay = 7 * 60,
            int walletBalance = 25,
            InventorySnapshot inventory = null,
            int worldSeed = 12345,
            int? farmLastProcessedDay = null,
            int? livestockLastProcessedDay = null)
        {
            return new GameSaveSnapshot(
                GameSaveSnapshot.CurrentSchemaVersion,
                worldSeed,
                new GameClockSnapshot(day, minuteOfDay),
                new[]
                {
                    new CharacterEconomySnapshot(
                        DefaultMvpIds.Characters.Player,
                        inventory ?? new InventorySnapshot(
                            new[] { new ItemStack("potato", 2) }),
                        new WalletSnapshot(walletBalance))
                },
                new[]
                {
                    new ShopEconomySnapshot(
                        DefaultMvpIds.Shops.TownGeneral,
                        new InventorySnapshot(
                            new[] { new ItemStack("seed.potato", 4) }),
                        new WalletSnapshot(10000),
                        day,
                        restockAlgorithmVersion: 1)
                },
                new FarmSnapshot(
                    farmLastProcessedDay ?? day,
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
                    livestockLastProcessedDay ?? day,
                    new[]
                    {
                        new AnimalSnapshot(
                            "hen-1",
                            "chicken",
                            fedToday: true,
                            productReady: false)
                    }));
        }

        public static GameSaveSnapshot Capture(CozyTownServices services)
        {
            EconomyStateSnapshot economy = services.EconomyState.CaptureSnapshot();

            return new GameSaveSnapshot(
                GameSaveSnapshot.CurrentSchemaVersion,
                services.WorldSeed.Value,
                services.Time.Current,
                economy.Characters,
                economy.Shops,
                services.Farm.CaptureSnapshot(),
                services.Livestock.CaptureSnapshot());
        }

        public static void AssertEquivalent(GameSaveSnapshot expected, GameSaveSnapshot actual)
        {
            Assert.That(actual.SchemaVersion, Is.EqualTo(expected.SchemaVersion));
            Assert.That(actual.WorldSeed, Is.EqualTo(expected.WorldSeed));
            Assert.That(actual.Clock.Day, Is.EqualTo(expected.Clock.Day));
            Assert.That(actual.Clock.MinuteOfDay, Is.EqualTo(expected.Clock.MinuteOfDay));
            Assert.That(actual.Characters.Length, Is.EqualTo(expected.Characters.Length));
            for (int index = 0; index < expected.Characters.Length; index++)
            {
                Assert.That(
                    actual.Characters[index].CharacterId,
                    Is.EqualTo(expected.Characters[index].CharacterId));
                Assert.That(
                    actual.Characters[index].Wallet.Balance,
                    Is.EqualTo(expected.Characters[index].Wallet.Balance));
                Assert.That(
                    actual.Characters[index].Backpack.Items,
                    Is.EqualTo(expected.Characters[index].Backpack.Items));
            }

            Assert.That(actual.Shops.Length, Is.EqualTo(expected.Shops.Length));
            for (int index = 0; index < expected.Shops.Length; index++)
            {
                Assert.That(actual.Shops[index].ShopId, Is.EqualTo(expected.Shops[index].ShopId));
                Assert.That(
                    actual.Shops[index].Wallet.Balance,
                    Is.EqualTo(expected.Shops[index].Wallet.Balance));
                Assert.That(
                    actual.Shops[index].LastRestockedDay,
                    Is.EqualTo(expected.Shops[index].LastRestockedDay));
                Assert.That(
                    actual.Shops[index].RestockAlgorithmVersion,
                    Is.EqualTo(expected.Shops[index].RestockAlgorithmVersion));
                Assert.That(
                    actual.Shops[index].Stock.Items,
                    Is.EqualTo(expected.Shops[index].Stock.Items));
            }
            Assert.That(actual.Farm.LastProcessedDay, Is.EqualTo(expected.Farm.LastProcessedDay));
            Assert.That(actual.Farm.Plots, Is.EqualTo(expected.Farm.Plots));
            Assert.That(
                actual.Livestock.LastProcessedDay,
                Is.EqualTo(expected.Livestock.LastProcessedDay));
            Assert.That(actual.Livestock.Animals, Is.EqualTo(expected.Livestock.Animals));
        }
    }
}
