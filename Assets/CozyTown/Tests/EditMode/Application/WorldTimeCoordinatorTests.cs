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

        [Test]
        public void AdvanceMinutes_WhenSecondSettlementFails_LeavesTheWholeRequestUncommitted()
        {
            CozyTownServices services = CreateWorldBeforeMorning(worldSeed: 3);
            CozyTownServices firstBoundaryOnly = CreateWorldBeforeMorning(worldSeed: 3);
            var restock = new DeterministicShopStockReplacementPolicy(
                new[]
                {
                    new ShopRestockRule("seed.potato", 1000, 1, 1),
                    new ShopRestockRule("unknown-item", 500, 1, 1)
                },
                minimumDistinctItems: 1);
            IWorldTimeCoordinator worldTime = CreateWorldTime(services, restock);
            IWorldTimeCoordinator firstOnly = CreateWorldTime(firstBoundaryOnly, restock);
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);

            OperationResult<GameClockSnapshot> firstResult = firstOnly.AdvanceMinutes(10);

            Assert.That(firstResult.IsSuccess, Is.True, firstResult.ErrorCode);
            Assert.That(firstResult.Value, Is.EqualTo(new GameClockSnapshot(2, 309)));
            Assert.That(firstBoundaryOnly.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            Assert.That(firstBoundaryOnly.Livestock.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            Assert.That(firstBoundaryOnly.EconomyState.TryGetShop(
                DefaultMvpIds.Shops.TownGeneral, out ShopEconomySnapshot firstShop), Is.True);
            Assert.That(firstShop.LastRestockedDay, Is.EqualTo(2));
            Assert.That(firstShop.Stock.Items, Is.EqualTo(new[] { new ItemStack("seed.potato", 1) }));

            OperationResult<GameClockSnapshot> result = worldTime.AdvanceMinutes(1450);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("economy.shop_invalid"));
            SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
        }

        [Test]
        public void AdvanceMinutes_AcrossSeveralMornings_MatchesPartitionedAdvancesAndKeepsPendingProducts()
        {
            CozyTownServices services = CreateWorldBeforeMorning(DefaultMvpContent.DefaultWorldSeed);
            CozyTownServices partitioned = CreateWorldBeforeMorning(DefaultMvpContent.DefaultWorldSeed);
            IWorldTimeCoordinator worldTime = CreateWorldTime(services);
            IWorldTimeCoordinator stepwise = CreateWorldTime(partitioned);

            OperationResult<GameClockSnapshot> result = worldTime.AdvanceMinutes(2890);
            Assert.That(stepwise.AdvanceMinutes(10).IsSuccess, Is.True);
            Assert.That(stepwise.AdvanceMinutes(1440).IsSuccess, Is.True);
            Assert.That(stepwise.AdvanceMinutes(1440).IsSuccess, Is.True);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(4, 309)));
            GameSaveSnapshot actual = SaveTestSnapshots.Capture(services);
            SaveTestSnapshots.AssertEquivalent(SaveTestSnapshots.Capture(partitioned), actual);
            Assert.That(actual.Farm.LastProcessedDay, Is.EqualTo(4));
            FarmPlotSnapshot plot = actual.Farm.Plots.Single(value => value.PlotId == "plot.01");
            Assert.That(plot.GrowthProgressDays, Is.EqualTo(1));
            Assert.That(plot.Status, Is.EqualTo(FarmPlotStatus.Growing));
            Assert.That(plot.WateredToday, Is.False);
            Assert.That(actual.Livestock.LastProcessedDay, Is.EqualTo(4));
            Assert.That(actual.Livestock.Animals.Single().FedToday, Is.False);
            Assert.That(actual.Livestock.Animals.Single().ProductReady, Is.True);
            Assert.That(actual.Shops.Single().LastRestockedDay, Is.EqualTo(4));
        }

        [Test]
        public void AdvanceMinutes_AtSettledFiveAmWithZeroMinutes_DoesNotSettleAgain()
        {
            CozyTownServices services = CreateWorldBeforeMorning(DefaultMvpContent.DefaultWorldSeed);
            IWorldTimeCoordinator worldTime = CreateWorldTime(services);
            Assert.That(worldTime.AdvanceMinutes(1).IsSuccess, Is.True);
            Assert.That(services.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            Assert.That(services.Farm.Water("plot.01").IsSuccess, Is.True);
            Assert.That(services.Livestock.CollectProduct(DefaultMvpIds.Livestock.Hen).IsSuccess, Is.True);
            Assert.That(services.Inventory.Add(DefaultMvpIds.Items.ChickenFeed, 1).IsSuccess, Is.True);
            Assert.That(services.Livestock.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess, Is.True);
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);

            OperationResult<GameClockSnapshot> result = worldTime.AdvanceMinutes(0);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(2, 300)));
            SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
        }

        [Test]
        public void AdvanceMinutes_WithoutWateringOrFeeding_OnlyAdvancesSettlementMarkersAndShop()
        {
            CozyTownServices services = CreateWorldBeforeMorning(
                DefaultMvpContent.DefaultWorldSeed, prepareProduction: false);
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
                    new FarmSnapshot(2, before.Farm.Plots),
                    new LivestockSnapshot(2, before.Livestock.Animals)),
                SaveTestSnapshots.Capture(services));
        }

        [TestCase(-1, "time.minutes_negative")]
        [TestCase(int.MinValue, "time.minutes_negative")]
        [TestCase(int.MaxValue, "world_time.request_too_large")]
        public void AdvanceMinutes_WithInvalidRequest_LeavesTheWorldUnchanged(int gameMinutes, string errorCode)
        {
            CozyTownServices services = CreateWorldBeforeMorning(DefaultMvpContent.DefaultWorldSeed);
            IWorldTimeCoordinator worldTime = CreateWorldTime(services);
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);

            OperationResult<GameClockSnapshot> result = worldTime.AdvanceMinutes(gameMinutes);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(errorCode));
            SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
        }

        [Test]
        public void AdvanceMinutes_WithExactlySevenDays_MatchesSevenDailyRequests()
        {
            CozyTownServices services = CreateWorldBeforeMorning(DefaultMvpContent.DefaultWorldSeed);
            CozyTownServices partitioned = CreateWorldBeforeMorning(DefaultMvpContent.DefaultWorldSeed);
            IWorldTimeCoordinator worldTime = CreateWorldTime(services);
            IWorldTimeCoordinator stepwise = CreateWorldTime(partitioned);

            OperationResult<GameClockSnapshot> result = worldTime.AdvanceMinutes(10080);
            for (int day = 0; day < 7; day++)
            {
                Assert.That(stepwise.AdvanceMinutes(1440).IsSuccess, Is.True);
            }

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(9, 299)));
            GameSaveSnapshot actual = SaveTestSnapshots.Capture(services);
            SaveTestSnapshots.AssertEquivalent(SaveTestSnapshots.Capture(partitioned), actual);
            Assert.That(actual.Farm.LastProcessedDay, Is.EqualTo(8));
            Assert.That(actual.Livestock.LastProcessedDay, Is.EqualTo(8));
            Assert.That(actual.Shops.Single().LastRestockedDay, Is.EqualTo(8));
        }

        [TestCase(1438, true)]
        [TestCase(1439, false)]
        public void AdvanceMinutes_AtMaximumCalendarDay_PreservesTheWorldUnlessTheMinuteFits(
            int startingMinute, bool shouldSucceed)
        {
            CozyTownServices services = CreateWorldBeforeMorning(DefaultMvpContent.DefaultWorldSeed);
            Assert.That(services.Farm.Restore(new FarmSnapshot(
                int.MaxValue, services.Farm.CaptureSnapshot().Plots)).IsSuccess, Is.True);
            Assert.That(services.Livestock.Restore(new LivestockSnapshot(
                int.MaxValue, services.Livestock.CaptureSnapshot().Animals)).IsSuccess, Is.True);
            Assert.That(services.EconomyState.TryGetShop(
                DefaultMvpIds.Shops.TownGeneral, out ShopEconomySnapshot shop), Is.True);
            Assert.That(services.EconomyState.CommitShop(new ShopEconomySnapshot(
                shop.ShopId, shop.Stock, shop.Wallet, int.MaxValue, shop.RestockAlgorithmVersion)).IsSuccess,
                Is.True);
            Assert.That(services.Time.Restore(new GameClockSnapshot(int.MaxValue, startingMinute)).IsSuccess,
                Is.True);
            IWorldTimeCoordinator worldTime = CreateWorldTime(services);
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);

            OperationResult<GameClockSnapshot> result = worldTime.AdvanceMinutes(1);

            Assert.That(result.IsSuccess, Is.EqualTo(shouldSucceed), result.ErrorCode);
            if (!shouldSucceed)
            {
                Assert.That(result.ErrorCode, Is.EqualTo("time.day_overflow"));
                SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
                return;
            }

            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(int.MaxValue, 1439)));
            SaveTestSnapshots.AssertEquivalent(
                new GameSaveSnapshot(
                    before.SchemaVersion,
                    before.WorldSeed,
                    new GameClockSnapshot(int.MaxValue, 1439),
                    before.Characters,
                    before.Shops,
                    before.Farm,
                    before.Livestock),
                SaveTestSnapshots.Capture(services));
        }

        private static CozyTownServices CreateWorldBeforeMorning(int worldSeed, bool prepareProduction = true)
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.WorldSeed.Restore(worldSeed).IsSuccess, Is.True);
            Assert.That(services.Inventory.Add(DefaultMvpIds.Items.PotatoSeed, 1).IsSuccess, Is.True);
            Assert.That(services.Farm.Plant("plot.01", DefaultMvpIds.Items.PotatoSeed).IsSuccess, Is.True);
            if (prepareProduction)
            {
                Assert.That(services.Inventory.Add(DefaultMvpIds.Items.ChickenFeed, 1).IsSuccess, Is.True);
                Assert.That(services.Farm.Water("plot.01").IsSuccess, Is.True);
                Assert.That(services.Livestock.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess, Is.True);
            }
            Assert.That(services.Time.Restore(new GameClockSnapshot(2, 299)).IsSuccess, Is.True);
            return services;
        }

        private static WorldTimeCoordinator CreateWorldTime(
            CozyTownServices services,
            IShopStockReplacementPolicy restock = null)
        {
            return new WorldTimeCoordinator(
                services.Time,
                services.Farm,
                services.Livestock,
                services.EconomyState,
                services.WorldSeed,
                restock ?? new DeterministicShopStockReplacementPolicy(
                    DefaultMvpContent.CreateConfiguration().ShopRestockRules,
                    minimumDistinctItems: 4));
        }
    }
}
