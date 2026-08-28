using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class DayTransitionCoordinatorTests
    {
        [Test]
        public void SleepToNextDay_WhenModulesAreAligned_AdvancesEveryModuleToSameDayOnce()
        {
            TransitionFixture fixture = CreateFixture();
            var coordinator = new DayTransitionCoordinator(
                fixture.Time,
                fixture.Farm,
                fixture.Livestock);

            OperationResult<GameClockSnapshot> result = coordinator.SleepToNextDay();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Day, Is.EqualTo(2));
            Assert.That(fixture.Time.Current.Day, Is.EqualTo(2));
            Assert.That(fixture.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            Assert.That(fixture.Livestock.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            Assert.That(fixture.Farm.Plots.Single().GrowthProgressDays, Is.EqualTo(1));
            Assert.That(fixture.Livestock.Animals.Single().ProductReady, Is.True);
        }

        [Test]
        public void SleepToNextDay_WhenFarmAlreadyProcessedTarget_RejectsDuplicateWithoutMutation()
        {
            TransitionFixture fixture = CreateFixture();
            Assert.That(fixture.Farm.AdvanceDay(2).IsSuccess, Is.True);
            FarmSnapshot farmBefore = fixture.Farm.CaptureSnapshot();
            LivestockSnapshot livestockBefore = fixture.Livestock.CaptureSnapshot();
            var coordinator = new DayTransitionCoordinator(
                fixture.Time,
                fixture.Farm,
                fixture.Livestock);

            OperationResult<GameClockSnapshot> result = coordinator.SleepToNextDay();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("day_transition.state_misaligned"));
            Assert.That(fixture.Time.Current.Day, Is.EqualTo(1));
            AssertFarmEquals(farmBefore, fixture.Farm.CaptureSnapshot());
            AssertLivestockEquals(livestockBefore, fixture.Livestock.CaptureSnapshot());
        }

        [Test]
        public void SleepToNextDay_AtMaximumDay_ReturnsFailureWithoutCallingDomainAdvances()
        {
            TransitionFixture fixture = CreateFixture(startingDay: int.MaxValue);
            var coordinator = new DayTransitionCoordinator(
                fixture.Time,
                fixture.Farm,
                fixture.Livestock);

            OperationResult<GameClockSnapshot> result = coordinator.SleepToNextDay();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("day_transition.day_overflow"));
            Assert.That(fixture.Time.Current.Day, Is.EqualTo(int.MaxValue));
            Assert.That(fixture.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(int.MaxValue));
            Assert.That(fixture.Livestock.CaptureSnapshot().LastProcessedDay, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void SleepToNextDay_WhenFarmFailsAfterMutation_RollsBackAllThreeSnapshots()
        {
            TransitionFixture fixture = CreateFixture();
            GameClockSnapshot clockBefore = fixture.Time.Current;
            FarmSnapshot farmBefore = fixture.Farm.CaptureSnapshot();
            LivestockSnapshot livestockBefore = fixture.Livestock.CaptureSnapshot();
            var failingFarm = new FailAfterAdvanceFarmService(fixture.Farm);
            var trackingLivestock = new TrackingLivestockService(fixture.Livestock);
            var coordinator = new DayTransitionCoordinator(
                fixture.Time,
                failingFarm,
                trackingLivestock);

            OperationResult<GameClockSnapshot> result = coordinator.SleepToNextDay();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("day_transition.farm_failed"));
            Assert.That(failingFarm.AdvanceCallCount, Is.EqualTo(1));
            Assert.That(trackingLivestock.AdvanceCallCount, Is.Zero);
            Assert.That(failingFarm.RestoreCallCount, Is.EqualTo(1));
            Assert.That(trackingLivestock.RestoreCallCount, Is.EqualTo(1));
            Assert.That(fixture.Time.Current, Is.EqualTo(clockBefore));
            AssertFarmEquals(farmBefore, fixture.Farm.CaptureSnapshot());
            AssertLivestockEquals(livestockBefore, fixture.Livestock.CaptureSnapshot());
        }

        [Test]
        public void SleepToNextDay_WhenLivestockFailsAfterFarmSucceeds_RollsBackAllThreeSnapshots()
        {
            TransitionFixture fixture = CreateFixture();
            GameClockSnapshot clockBefore = fixture.Time.Current;
            FarmSnapshot farmBefore = fixture.Farm.CaptureSnapshot();
            LivestockSnapshot livestockBefore = fixture.Livestock.CaptureSnapshot();
            var trackingFarm = new TrackingFarmService(fixture.Farm);
            var failingLivestock = new FailAfterAdvanceLivestockService(fixture.Livestock);
            var coordinator = new DayTransitionCoordinator(
                fixture.Time,
                trackingFarm,
                failingLivestock);

            OperationResult<GameClockSnapshot> result = coordinator.SleepToNextDay();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("day_transition.livestock_failed"));
            Assert.That(trackingFarm.AdvanceCallCount, Is.EqualTo(1));
            Assert.That(failingLivestock.AdvanceCallCount, Is.EqualTo(1));
            Assert.That(trackingFarm.RestoreCallCount, Is.EqualTo(1));
            Assert.That(failingLivestock.RestoreCallCount, Is.EqualTo(1));
            Assert.That(fixture.Time.Current, Is.EqualTo(clockBefore));
            AssertFarmEquals(farmBefore, fixture.Farm.CaptureSnapshot());
            AssertLivestockEquals(livestockBefore, fixture.Livestock.CaptureSnapshot());
        }

        [Test]
        public void SleepToNextDay_WhenOneRollbackFails_ReturnsDiagnosticAndAttemptsOtherRollbacks()
        {
            TransitionFixture fixture = CreateFixture();
            var restoreFailingFarm = new FailRestoreFarmService(fixture.Farm);
            var failingLivestock = new FailAfterAdvanceLivestockService(fixture.Livestock);
            var coordinator = new DayTransitionCoordinator(
                fixture.Time,
                restoreFailingFarm,
                failingLivestock);

            OperationResult<GameClockSnapshot> result = coordinator.SleepToNextDay();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("day_transition.rollback_farm_failed"));
            Assert.That(restoreFailingFarm.RestoreCallCount, Is.EqualTo(1));
            Assert.That(failingLivestock.RestoreCallCount, Is.EqualTo(1));
            Assert.That(fixture.Time.Current.Day, Is.EqualTo(1));
            Assert.That(fixture.Livestock.CaptureSnapshot().LastProcessedDay, Is.EqualTo(1));
        }

        private static TransitionFixture CreateFixture(int startingDay = 1)
        {
            var inventory = new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("seed", "Seed", ItemCategory.Seed, 99),
                    new ItemDefinition("crop", "Crop", ItemCategory.Crop, 99),
                    new ItemDefinition("feed", "Feed", ItemCategory.Feed, 99),
                    new ItemDefinition("egg", "Egg", ItemCategory.AnimalProduct, 99)
                },
                8);
            Assert.That(inventory.Add("seed", 1).IsSuccess, Is.True);
            Assert.That(inventory.Add("feed", 1).IsSuccess, Is.True);
            var time = new InMemoryTimeService(startingDay, 20 * 60);
            var farm = new InMemoryFarmService(
                new[] { "plot" },
                new[] { new CropDefinition("crop-definition", "seed", "crop", 2, 1) },
                inventory,
                startingDay);
            var livestock = new InMemoryLivestockService(
                new[] { new AnimalSnapshot("hen", "chicken", false, false) },
                new[] { new AnimalDefinition("chicken", "feed", "egg", 1) },
                inventory,
                startingDay);
            Assert.That(farm.Plant("plot", "seed").IsSuccess, Is.True);
            Assert.That(farm.Water("plot").IsSuccess, Is.True);
            Assert.That(livestock.Feed("hen").IsSuccess, Is.True);
            return new TransitionFixture(time, farm, livestock);
        }

        private static void AssertFarmEquals(FarmSnapshot expected, FarmSnapshot actual)
        {
            Assert.That(actual.LastProcessedDay, Is.EqualTo(expected.LastProcessedDay));
            Assert.That(actual.Plots.Length, Is.EqualTo(expected.Plots.Length));
            for (int index = 0; index < expected.Plots.Length; index++)
            {
                Assert.That(actual.Plots[index].PlotId, Is.EqualTo(expected.Plots[index].PlotId));
                Assert.That(actual.Plots[index].CropId, Is.EqualTo(expected.Plots[index].CropId));
                Assert.That(actual.Plots[index].GrowthProgressDays, Is.EqualTo(expected.Plots[index].GrowthProgressDays));
                Assert.That(actual.Plots[index].WateredToday, Is.EqualTo(expected.Plots[index].WateredToday));
                Assert.That(actual.Plots[index].Status, Is.EqualTo(expected.Plots[index].Status));
            }
        }

        private static void AssertLivestockEquals(
            LivestockSnapshot expected,
            LivestockSnapshot actual)
        {
            Assert.That(actual.LastProcessedDay, Is.EqualTo(expected.LastProcessedDay));
            Assert.That(actual.Animals.Length, Is.EqualTo(expected.Animals.Length));
            for (int index = 0; index < expected.Animals.Length; index++)
            {
                Assert.That(actual.Animals[index].AnimalId, Is.EqualTo(expected.Animals[index].AnimalId));
                Assert.That(actual.Animals[index].SpeciesId, Is.EqualTo(expected.Animals[index].SpeciesId));
                Assert.That(actual.Animals[index].FedToday, Is.EqualTo(expected.Animals[index].FedToday));
                Assert.That(actual.Animals[index].ProductReady, Is.EqualTo(expected.Animals[index].ProductReady));
            }
        }

        private sealed class TransitionFixture
        {
            public TransitionFixture(
                ITimeService time,
                IFarmService farm,
                ILivestockService livestock)
            {
                Time = time;
                Farm = farm;
                Livestock = livestock;
            }

            public ITimeService Time { get; }
            public IFarmService Farm { get; }
            public ILivestockService Livestock { get; }
        }

        private class TrackingFarmService : IFarmService
        {
            protected readonly IFarmService Inner;

            public TrackingFarmService(IFarmService inner)
            {
                Inner = inner;
            }

            public int AdvanceCallCount { get; protected set; }
            public int RestoreCallCount { get; protected set; }
            public IReadOnlyCollection<FarmPlotSnapshot> Plots => Inner.Plots;
            public OperationResult Plant(string plotId, string seedItemId) => Inner.Plant(plotId, seedItemId);
            public OperationResult Water(string plotId) => Inner.Water(plotId);

            public virtual OperationResult AdvanceDay(int newDay)
            {
                AdvanceCallCount++;
                return Inner.AdvanceDay(newDay);
            }

            public OperationResult Harvest(string plotId) => Inner.Harvest(plotId);
            public FarmSnapshot CaptureSnapshot() => Inner.CaptureSnapshot();

            public virtual OperationResult Restore(FarmSnapshot snapshot)
            {
                RestoreCallCount++;
                return Inner.Restore(snapshot);
            }
        }

        private sealed class FailAfterAdvanceFarmService : TrackingFarmService
        {
            public FailAfterAdvanceFarmService(IFarmService inner)
                : base(inner)
            {
            }

            public override OperationResult AdvanceDay(int newDay)
            {
                base.AdvanceDay(newDay);
                return OperationResult.Failure("injected.farm_failure");
            }
        }

        private sealed class FailRestoreFarmService : TrackingFarmService
        {
            public FailRestoreFarmService(IFarmService inner)
                : base(inner)
            {
            }

            public override OperationResult Restore(FarmSnapshot snapshot)
            {
                RestoreCallCount++;
                return OperationResult.Failure("injected.restore_failure");
            }
        }

        private class TrackingLivestockService : ILivestockService
        {
            protected readonly ILivestockService Inner;

            public TrackingLivestockService(ILivestockService inner)
            {
                Inner = inner;
            }

            public int AdvanceCallCount { get; protected set; }
            public int RestoreCallCount { get; private set; }
            public IReadOnlyCollection<AnimalSnapshot> Animals => Inner.Animals;
            public OperationResult Feed(string animalId) => Inner.Feed(animalId);

            public virtual OperationResult AdvanceDay(int newDay)
            {
                AdvanceCallCount++;
                return Inner.AdvanceDay(newDay);
            }

            public OperationResult CollectProduct(string animalId) => Inner.CollectProduct(animalId);
            public LivestockSnapshot CaptureSnapshot() => Inner.CaptureSnapshot();

            public OperationResult Restore(LivestockSnapshot snapshot)
            {
                RestoreCallCount++;
                return Inner.Restore(snapshot);
            }
        }

        private sealed class FailAfterAdvanceLivestockService : TrackingLivestockService
        {
            public FailAfterAdvanceLivestockService(ILivestockService inner)
                : base(inner)
            {
            }

            public override OperationResult AdvanceDay(int newDay)
            {
                base.AdvanceDay(newDay);
                return OperationResult.Failure("injected.livestock_failure");
            }
        }
    }
}
