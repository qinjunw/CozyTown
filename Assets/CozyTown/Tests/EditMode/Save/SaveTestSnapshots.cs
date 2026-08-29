using CozyTown.Runtime.Core;
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
            int? farmLastProcessedDay = null,
            int? livestockLastProcessedDay = null)
        {
            return new GameSaveSnapshot(
                GameSaveSnapshot.CurrentSchemaVersion,
                new GameClockSnapshot(day, minuteOfDay),
                inventory ?? new InventorySnapshot(new[] { new ItemStack("potato", 2) }),
                new WalletSnapshot(walletBalance),
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
            return new GameSaveSnapshot(
                GameSaveSnapshot.CurrentSchemaVersion,
                services.Time.Current,
                services.Inventory.CaptureSnapshot(),
                services.Wallet.CaptureSnapshot(),
                services.Farm.CaptureSnapshot(),
                services.Livestock.CaptureSnapshot());
        }

        public static void AssertEquivalent(GameSaveSnapshot expected, GameSaveSnapshot actual)
        {
            Assert.That(actual.SchemaVersion, Is.EqualTo(expected.SchemaVersion));
            Assert.That(actual.Clock.Day, Is.EqualTo(expected.Clock.Day));
            Assert.That(actual.Clock.MinuteOfDay, Is.EqualTo(expected.Clock.MinuteOfDay));
            Assert.That(actual.Wallet.Balance, Is.EqualTo(expected.Wallet.Balance));
            Assert.That(actual.Inventory.Items, Is.EqualTo(expected.Inventory.Items));
            Assert.That(actual.Farm.LastProcessedDay, Is.EqualTo(expected.Farm.LastProcessedDay));
            Assert.That(actual.Farm.Plots, Is.EqualTo(expected.Farm.Plots));
            Assert.That(
                actual.Livestock.LastProcessedDay,
                Is.EqualTo(expected.Livestock.LastProcessedDay));
            Assert.That(actual.Livestock.Animals, Is.EqualTo(expected.Livestock.Animals));
        }
    }
}
