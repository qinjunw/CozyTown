using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using CozyTown.Tests.EditMode.Save;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class DaytimeClockCoordinatorTests
    {
        [Test]
        public void AdvanceElapsed_AfterFiveSeconds_AdvancesTenMinutes()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            IDaytimeClock clock = CreateClock(services);

            OperationResult<GameClockSnapshot> result = clock.AdvanceElapsed(5);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(1, 370)));
            Assert.That(clock.Current, Is.EqualTo(result.Value));
        }

        [TestCase(4.9, 0.1, 1)]
        [TestCase(0, 0.1, 50)]
        public void AdvanceElapsed_WhenFiveSecondsArriveInParts_AdvancesTenMinutes(
            double initialSeconds,
            double secondsPerFrame,
            int frameCount)
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            IDaytimeClock clock = CreateClock(services);
            Assert.That(clock.AdvanceElapsed(initialSeconds).IsSuccess, Is.True);
            for (int frame = 0; frame < frameCount - 1; frame++)
            {
                Assert.That(clock.AdvanceElapsed(secondsPerFrame).IsSuccess, Is.True);
            }
            Assert.That(clock.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));

            OperationResult<GameClockSnapshot> result =
                clock.AdvanceElapsed(secondsPerFrame);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(1, 370)));
            Assert.That(clock.Current, Is.EqualTo(result.Value));
        }

        [Test]
        public void AdvanceElapsed_WhenSeveralTicksArriveTogether_PreservesRemainingTime()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            IDaytimeClock clock = CreateClock(services);

            OperationResult<GameClockSnapshot> first = clock.AdvanceElapsed(12.5);
            OperationResult<GameClockSnapshot> second = clock.AdvanceElapsed(2.5);

            Assert.That(first.IsSuccess, Is.True, first.ErrorCode);
            Assert.That(first.Value, Is.EqualTo(new GameClockSnapshot(1, 380)));
            Assert.That(second.IsSuccess, Is.True, second.ErrorCode);
            Assert.That(second.Value, Is.EqualTo(new GameClockSnapshot(1, 390)));
            Assert.That(clock.Current, Is.EqualTo(second.Value));
        }

        [TestCase(1, 1435, 5)]
        [TestCase(1, 360, double.MaxValue)]
        [TestCase(int.MaxValue, 1435, 5)]
        public void AdvanceElapsed_AtDayEnd_StopsAt2359WithoutDailySettlement(
            int day,
            int startingMinute,
            double seconds)
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(
                services.Time.Restore(new GameClockSnapshot(day, startingMinute)).IsSuccess,
                Is.True);
            IDaytimeClock clock = CreateClock(services);
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);

            OperationResult<GameClockSnapshot> result = clock.AdvanceElapsed(seconds);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(day, 1439)));
            Assert.That(clock.Current, Is.EqualTo(result.Value));
            SaveTestSnapshots.AssertEquivalent(
                new GameSaveSnapshot(
                    before.SchemaVersion,
                    before.WorldSeed,
                    new GameClockSnapshot(day, 1439),
                    before.Characters,
                    before.Shops,
                    before.Farm,
                    before.Livestock),
                SaveTestSnapshots.Capture(services));
            Assert.That(clock.AdvanceElapsed(double.MaxValue).IsSuccess, Is.True);
            Assert.That(clock.Current, Is.EqualTo(new GameClockSnapshot(day, 1439)));
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void AdvanceElapsed_WhenSecondsAreInvalid_RejectsWithoutLosingPartialTick(
            double seconds)
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            IDaytimeClock clock = CreateClock(services);
            Assert.That(clock.AdvanceElapsed(4.9).IsSuccess, Is.True);

            OperationResult<GameClockSnapshot> result = clock.AdvanceElapsed(seconds);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("time.elapsed_invalid"));
            Assert.That(clock.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));
            Assert.That(clock.AdvanceElapsed(0.1).IsSuccess, Is.True);
            Assert.That(clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));
        }

        [Test]
        public void AdvanceElapsed_WhenZeroSecondsPass_PreservesPartialTick()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            IDaytimeClock clock = CreateClock(services);
            Assert.That(clock.AdvanceElapsed(4.9).IsSuccess, Is.True);

            OperationResult<GameClockSnapshot> result = clock.AdvanceElapsed(0);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(1, 360)));
            Assert.That(clock.AdvanceElapsed(0.1).IsSuccess, Is.True);
            Assert.That(clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SleepToNextDay_ResetsElapsedOnlyAfterSuccessfulSettlement(
            bool settlementSucceeds)
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            if (!settlementSucceeds)
            {
                Assert.That(services.Farm.AdvanceDay(2).IsSuccess, Is.True);
            }
            DaytimeClockCoordinator clock = CreateClock(services);
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);
            Assert.That(clock.AdvanceElapsed(4.9).IsSuccess, Is.True);

            OperationResult<GameClockSnapshot> result = clock.SleepToNextDay();

            Assert.That(result.IsSuccess, Is.EqualTo(settlementSucceeds), result.ErrorCode);
            if (settlementSucceeds)
            {
                Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(2, 360)));
                Assert.That(clock.Current, Is.EqualTo(result.Value));
                Assert.That(services.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
                Assert.That(services.Livestock.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
                Assert.That(
                    services.EconomyState.TryGetShop(
                        DefaultMvpIds.Shops.TownGeneral,
                        out ShopEconomySnapshot shop),
                    Is.True);
                Assert.That(shop.LastRestockedDay, Is.EqualTo(2));
            }
            else
            {
                Assert.That(result.ErrorCode, Is.EqualTo("day_transition.state_misaligned"));
                SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
            }

            Assert.That(clock.AdvanceElapsed(0.1).IsSuccess, Is.True);
            Assert.That(
                clock.Current,
                Is.EqualTo(settlementSucceeds
                    ? new GameClockSnapshot(2, 360)
                    : new GameClockSnapshot(1, 370)));
            if (settlementSucceeds)
            {
                Assert.That(clock.AdvanceElapsed(4.9).IsSuccess, Is.True);
                Assert.That(clock.Current, Is.EqualTo(new GameClockSnapshot(2, 370)));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Load_ResetsElapsedOnlyAfterSuccessfulRestore(bool restoreSucceeds)
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            DaytimeClockCoordinator clock = CreateClock(services);
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);
            if (restoreSucceeds)
            {
                Assert.That(clock.Save().IsSuccess, Is.True);
            }
            else
            {
                CharacterEconomySnapshot player = before.Characters[0];
                var invalid = new GameSaveSnapshot(
                    before.SchemaVersion,
                    worldSeed: 777,
                    new GameClockSnapshot(1, 600),
                    new[]
                    {
                        new CharacterEconomySnapshot(
                            player.CharacterId,
                            new InventorySnapshot(new[] { new ItemStack("unknown-item", 1) }),
                            new WalletSnapshot(1))
                    },
                    before.Shops,
                    before.Farm,
                    before.Livestock);
                Assert.That(
                    services.SaveStorage.Save(JsonFileSaveStorage.MainSlotId, invalid).IsSuccess,
                    Is.True);
            }
            Assert.That(clock.AdvanceElapsed(4.9).IsSuccess, Is.True);

            OperationResult result = clock.Load();

            Assert.That(result.IsSuccess, Is.EqualTo(restoreSucceeds), result.ErrorCode);
            if (!restoreSucceeds)
            {
                Assert.That(result.ErrorCode, Is.EqualTo("save.restore_economy_failed"));
            }
            SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
            Assert.That(clock.AdvanceElapsed(0.1).IsSuccess, Is.True);
            Assert.That(
                clock.Current,
                Is.EqualTo(restoreSucceeds
                    ? new GameClockSnapshot(1, 360)
                    : new GameClockSnapshot(1, 370)));
            if (restoreSucceeds)
            {
                Assert.That(clock.AdvanceElapsed(4.9).IsSuccess, Is.True);
                Assert.That(clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Save_PreservesElapsedTime(bool saveSucceeds)
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            if (!saveSucceeds)
            {
                Assert.That(services.Farm.AdvanceDay(2).IsSuccess, Is.True);
            }
            DaytimeClockCoordinator clock = CreateClock(services);
            Assert.That(clock.HasSave, Is.False);
            Assert.That(clock.AdvanceElapsed(4.9).IsSuccess, Is.True);

            OperationResult result = clock.Save();

            Assert.That(result.IsSuccess, Is.EqualTo(saveSucceeds), result.ErrorCode);
            Assert.That(clock.HasSave, Is.EqualTo(saveSucceeds));
            Assert.That(clock.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));
            Assert.That(clock.AdvanceElapsed(0.1).IsSuccess, Is.True);
            Assert.That(clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));
        }

        private static DaytimeClockCoordinator CreateClock(CozyTownServices services)
        {
            var dayTransition = new DayTransitionCoordinator(
                services.Time,
                services.Farm,
                services.Livestock,
                services.EconomyState,
                new DeterministicShopStockReplacementPolicy(
                    DefaultMvpContent.CreateConfiguration().ShopRestockRules,
                    minimumDistinctItems: 4),
                services.WorldSeed,
                DefaultMvpIds.Shops.TownGeneral);
            var gameSave = new GameSaveCoordinator(
                services.WorldSeed,
                services.Time,
                services.EconomyState,
                services.Farm,
                services.Livestock,
                services.SaveStorage);
            return new DaytimeClockCoordinator(services.Time, dayTransition, gameSave);
        }
    }
}
