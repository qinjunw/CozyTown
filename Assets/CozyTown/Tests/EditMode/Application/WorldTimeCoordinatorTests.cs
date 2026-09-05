using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using CozyTown.Tests.EditMode.Save;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class WorldTimeCoordinatorTests
    {
        [Test]
        public void AdvanceMinutes_AcrossMidnight_ChangesCalendarWithoutSettlingProduction()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(
                services.Time.Restore(new GameClockSnapshot(1, 1439)).IsSuccess,
                Is.True);
            IWorldTimeCoordinator worldTime = CreateWorldTime(services);
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);

            OperationResult<GameClockSnapshot> result = worldTime.AdvanceMinutes(10);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(2, 9)));
            Assert.That(worldTime.Current, Is.EqualTo(result.Value));
            SaveTestSnapshots.AssertEquivalent(
                new GameSaveSnapshot(
                    before.SchemaVersion,
                    before.WorldSeed,
                    new GameClockSnapshot(2, 9),
                    before.Characters,
                    before.Shops,
                    before.Farm,
                    before.Livestock),
                SaveTestSnapshots.Capture(services));
        }

        [Test]
        public void AdvanceMinutes_AcrossFiveAm_SettlesProductionAndShopForTheCalendarDay()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.Inventory.Add(DefaultMvpIds.Items.PotatoSeed, 1).IsSuccess, Is.True);
            Assert.That(services.Inventory.Add(DefaultMvpIds.Items.ChickenFeed, 1).IsSuccess, Is.True);
            Assert.That(services.Farm.Plant("plot.01", DefaultMvpIds.Items.PotatoSeed).IsSuccess, Is.True);
            Assert.That(services.Farm.Water("plot.01").IsSuccess, Is.True);
            Assert.That(services.Livestock.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess, Is.True);
            Assert.That(
                services.Time.Restore(new GameClockSnapshot(2, 299)).IsSuccess,
                Is.True);
            IWorldTimeCoordinator worldTime = CreateWorldTime(services);
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);

            OperationResult<GameClockSnapshot> result = worldTime.AdvanceMinutes(10);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(2, 309)));
            SaveTestSnapshots.AssertEquivalent(
                new GameSaveSnapshot(
                    before.SchemaVersion,
                    before.WorldSeed,
                    new GameClockSnapshot(2, 309),
                    before.Characters,
                    new[]
                    {
                        new ShopEconomySnapshot(
                            DefaultMvpIds.Shops.TownGeneral,
                            new InventorySnapshot(new[]
                            {
                                new ItemStack("seed.potato", 3),
                                new ItemStack("seed.carrot", 3),
                                new ItemStack("seed.tomato", 3),
                                new ItemStack("feed.chicken", 9),
                                new ItemStack("ingredient.salt", 7)
                            }),
                            before.Shops.Single().Wallet,
                            lastRestockedDay: 2,
                            restockAlgorithmVersion: 1)
                    },
                    new FarmSnapshot(
                        2,
                        before.Farm.Plots.Select(plot => plot.PlotId == "plot.01"
                            ? new FarmPlotSnapshot(
                                plot.PlotId,
                                DefaultMvpIds.Crops.Potato,
                                growthProgressDays: 1,
                                wateredToday: false,
                                status: FarmPlotStatus.Growing)
                            : plot).ToArray()),
                    new LivestockSnapshot(
                        2,
                        new[]
                        {
                            new AnimalSnapshot(
                                DefaultMvpIds.Livestock.Hen,
                                DefaultMvpIds.Livestock.ChickenSpecies,
                                fedToday: false,
                                productReady: true)
                        })),
                SaveTestSnapshots.Capture(services));
        }

        [Test]
        public void AdvanceMinutes_WhenRequestExceedsSevenDays_RejectsWithoutChangingTheWorld()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            IWorldTimeCoordinator worldTime = CreateWorldTime(services);
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);

            OperationResult<GameClockSnapshot> result = worldTime.AdvanceMinutes(10081);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("world_time.request_too_large"));
            SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
        }

        private static WorldTimeCoordinator CreateWorldTime(CozyTownServices services)
        {
            return new WorldTimeCoordinator(
                services.Time,
                services.Farm,
                services.Livestock,
                services.EconomyState,
                services.WorldSeed,
                new DeterministicShopStockReplacementPolicy(
                    DefaultMvpContent.CreateConfiguration().ShopRestockRules,
                    minimumDistinctItems: 4));
        }
    }
}
