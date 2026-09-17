using System;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using CozyTown.Tests.EditMode.Save;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class WorldTimeRecoveryTests
    {
        [TestCase("elapsed", 361, 0.5)]
        [TestCase("direct", 361, 0.5)]
        [TestCase("sleep", 420, 0)]
        public void CriticalAdvanceFailure_CommitsTimeAndBlocksMutationsUntilLoad(
            string advance, int committedMinute, double committedFraction)
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            var saved = services.SaveStorage.Load(JsonFileSaveStorage.MainSlotId).Value;
            int followingCriticalCalls = 0, presentationCalls = 0;
            WorldTimeProgress observed = default;
            Action<WorldTimeProgress> failure = _ => throw new InvalidOperationException("controlled failure");
            services.WorldTimeFlow.Changed += failure;
            services.WorldTimeFlow.Changed += progress =>
            {
                followingCriticalCalls++;
                observed = progress;
            };
            services.WorldTimeFlow.PresentationChanged += _ => presentationCalls++;

            var result = Advance(services, advance);

            AssertFailure(result, "world_time.synchronization_failed");
            Assert.That(services.WorldTimeFlow.State, Is.EqualTo(WorldTimeFlowState.RecoveryRequired));
            Assert.That(followingCriticalCalls, Is.EqualTo(1));
            Assert.That(presentationCalls, Is.Zero);
            AssertProgress(services, committedMinute, committedFraction);
            Assert.That(observed.TotalMinutes, Is.EqualTo(services.WorldTimeFlow.Current.TotalMinutes));
            Assert.That(observed.AdvanceFromTotalMinutes,
                Is.EqualTo(advance == "sleep" ? 360 : 360.5));
            Assert.That(services.WorldTimeFlow.NotificationFailures, Has.Count.EqualTo(1));
            Assert.That(services.WorldTimeFlow.NotificationFailures[0], Does.StartWith("synchronization:"));
            var committed = SaveTestSnapshots.Capture(services);

            AssertFailure(services.WorldTime.AdvanceMinutes(1), "world_time.not_ready");
            AssertFailure(services.DaytimeClock.AdvanceElapsed(0.5), "world_time.not_ready");
            AssertFailure(services.Sleep.SleepForMinutes(60), "world_time.not_ready");
            AssertFailure(services.DayTransition.SleepToNextDay(), "world_time.not_ready");
            AssertFailure(services.GameSave.Save(), "world_time.not_ready");
            SaveTestSnapshots.AssertEquivalent(committed, SaveTestSnapshots.Capture(services));
            SaveTestSnapshots.AssertEquivalent(saved, services.SaveStorage.Load(JsonFileSaveStorage.MainSlotId).Value);
            Assert.That(followingCriticalCalls, Is.EqualTo(1));
            Assert.That(presentationCalls, Is.Zero);

            services.WorldTimeFlow.Changed -= failure;
            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(services.WorldTimeFlow.State, Is.EqualTo(WorldTimeFlowState.Ready));
            Assert.That(services.WorldTimeFlow.NotificationFailures, Is.Empty);
            Assert.That(followingCriticalCalls, Is.EqualTo(2));
            Assert.That(presentationCalls, Is.EqualTo(1));
            AssertProgress(services, 360, 0);
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            AssertProgress(services, 360, 0.5);
        }

        [TestCase("elapsed", 361, 0.5, 362, 0)]
        [TestCase("direct", 361, 0.5, 362, 0)]
        [TestCase("sleep", 420, 0, 420, 0.5)]
        public void PresentationAdvanceFailure_PreservesCommittedFractionAndAllowsFurtherAdvance(
            string advance, int committedMinute, double committedFraction, int nextMinute, double nextFraction)
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            int criticalCalls = 0, followingPresentationCalls = 0;
            services.WorldTimeFlow.Changed += _ => criticalCalls++;
            Action<WorldTimeProgress> failure = _ => throw new InvalidOperationException("controlled failure");
            services.WorldTimeFlow.PresentationChanged += failure;
            services.WorldTimeFlow.PresentationChanged += _ => followingPresentationCalls++;

            var result = Advance(services, advance);

            AssertFailure(result, "world_time.presentation_failed");
            Assert.That(services.WorldTimeFlow.State, Is.EqualTo(WorldTimeFlowState.Ready));
            Assert.That(criticalCalls, Is.EqualTo(1));
            Assert.That(followingPresentationCalls, Is.EqualTo(1));
            AssertProgress(services, committedMinute, committedFraction);
            Assert.That(services.WorldTimeFlow.NotificationFailures, Has.Count.EqualTo(1));
            Assert.That(services.WorldTimeFlow.NotificationFailures[0], Does.StartWith("presentation:"));
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);

            services.WorldTimeFlow.PresentationChanged -= failure;
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            AssertProgress(services, nextMinute, nextFraction);
            Assert.That(services.WorldTimeFlow.NotificationFailures, Is.Empty);
            Assert.That(criticalCalls, Is.EqualTo(2));
            Assert.That(followingPresentationCalls, Is.EqualTo(2));
        }

        [Test]
        public void MissingSaveDuringRecovery_PreservesSuspensionUntilAValidSnapshotIsLoaded()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            var valid = SaveTestSnapshots.Capture(services);
            Action<WorldTimeProgress> failure = _ => throw new InvalidOperationException("controlled failure");
            services.WorldTimeFlow.Changed += failure;
            AssertFailure(services.DaytimeClock.AdvanceElapsed(0.25), "world_time.synchronization_failed");
            services.WorldTimeFlow.Changed -= failure;
            long version = services.WorldTimeFlow.Current.RebuildVersion;

            AssertFailure(services.GameSave.Load(), "save.slot_missing");

            Assert.That(services.WorldTimeFlow.State, Is.EqualTo(WorldTimeFlowState.RecoveryRequired));
            Assert.That(services.WorldTimeFlow.Current.RebuildVersion, Is.EqualTo(version));
            AssertProgress(services, 360, 0.5);
            AssertFailure(services.DaytimeClock.AdvanceElapsed(0.25), "world_time.not_ready");
            Assert.That(services.SaveStorage.Save(JsonFileSaveStorage.MainSlotId, valid).IsSuccess, Is.True);
            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(services.WorldTimeFlow.State, Is.EqualTo(WorldTimeFlowState.Ready));
            Assert.That(services.WorldTimeFlow.Current.RebuildVersion, Is.GreaterThan(version));
            AssertProgress(services, 360, 0);
        }

        [Test]
        public void RejectedSnapshotDuringRecovery_RollsBackAuthorityWithoutClearingSuspension()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            var valid = services.SaveStorage.Load(JsonFileSaveStorage.MainSlotId).Value;
            Action<WorldTimeProgress> failure = _ => throw new InvalidOperationException("controlled failure");
            services.WorldTimeFlow.Changed += failure;
            AssertFailure(services.DaytimeClock.AdvanceElapsed(0.75), "world_time.synchronization_failed");
            services.WorldTimeFlow.Changed -= failure;
            var beforeLoad = SaveTestSnapshots.Capture(services);
            long version = services.WorldTimeFlow.Current.RebuildVersion;
            var player = valid.Characters[0];
            var rejected = new GameSaveSnapshot(valid.SchemaVersion, 777, new GameClockSnapshot(1, 600),
                new[] { new CharacterEconomySnapshot(player.CharacterId,
                    new InventorySnapshot(new[] { new ItemStack("unknown-item", 1) }), new WalletSnapshot(1)) },
                valid.Shops, valid.Farm, valid.Livestock);
            Assert.That(services.SaveStorage.Save(JsonFileSaveStorage.MainSlotId, rejected).IsSuccess, Is.True);

            AssertFailure(services.GameSave.Load(), "save.restore_economy_failed");

            SaveTestSnapshots.AssertEquivalent(beforeLoad, SaveTestSnapshots.Capture(services));
            Assert.That(services.WorldTimeFlow.State, Is.EqualTo(WorldTimeFlowState.RecoveryRequired));
            Assert.That(services.WorldTimeFlow.Current.RebuildVersion, Is.EqualTo(version));
            AssertProgress(services, 361, 0.5);
            AssertFailure(services.WorldTime.AdvanceMinutes(1), "world_time.not_ready");
            AssertFailure(services.GameSave.Save(), "world_time.not_ready");
            Assert.That(services.SaveStorage.Save(JsonFileSaveStorage.MainSlotId, valid).IsSuccess, Is.True);
            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(services.WorldTimeFlow.State, Is.EqualTo(WorldTimeFlowState.Ready));
            AssertProgress(services, 360, 0);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LoadNotification_ReentrantApplicationMutationsAreRejected(bool presentation)
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            var saved = services.SaveStorage.Load(JsonFileSaveStorage.MainSlotId).Value;
            Assert.That(services.WorldTime.AdvanceMinutes(60).IsSuccess, Is.True);
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            OperationResult nestedLoad = default, nestedSave = default;
            OperationResult<GameClockSnapshot> elapsed = default, direct = default, sleep = default;
            WorldTimeFlowState observedState = WorldTimeFlowState.Ready;
            int calls = 0;
            Action<WorldTimeProgress> nestedOperations = _ =>
            {
                calls++;
                if (calls > 1) return;
                observedState = services.WorldTimeFlow.State;
                nestedLoad = services.GameSave.Load();
                nestedSave = services.GameSave.Save();
                elapsed = services.DaytimeClock.AdvanceElapsed(0.5);
                direct = services.WorldTime.AdvanceMinutes(1);
                sleep = services.Sleep.SleepForMinutes(60);
            };
            if (presentation) services.WorldTimeFlow.PresentationChanged += nestedOperations;
            else services.WorldTimeFlow.Changed += nestedOperations;

            var result = services.GameSave.Load();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(observedState, Is.EqualTo(WorldTimeFlowState.Publishing));
            AssertFailure(nestedLoad, "world_time.not_ready");
            AssertFailure(nestedSave, "world_time.not_ready");
            AssertFailure(elapsed, "world_time.not_ready");
            AssertFailure(direct, "world_time.not_ready");
            AssertFailure(sleep, "world_time.not_ready");
            Assert.That(services.WorldTimeFlow.State, Is.EqualTo(WorldTimeFlowState.Ready));
            AssertProgress(services, 360, 0);
            SaveTestSnapshots.AssertEquivalent(saved, SaveTestSnapshots.Capture(services));
            SaveTestSnapshots.AssertEquivalent(saved, services.SaveStorage.Load(JsonFileSaveStorage.MainSlotId).Value);
        }

        [Test]
        public void LoadPresentationFailure_ReportsCommittedRestoreAndClearsElapsedRemainder()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            var saved = services.SaveStorage.Load(JsonFileSaveStorage.MainSlotId).Value;
            Assert.That(services.WorldTime.AdvanceMinutes(60).IsSuccess, Is.True);
            Assert.That(services.Wallet.Credit(75).IsSuccess, Is.True);
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            Action<WorldTimeProgress> failure = _ => throw new InvalidOperationException("controlled failure");
            services.WorldTimeFlow.PresentationChanged += failure;

            AssertFailure(services.GameSave.Load(), "save.loaded_presentation_failed");

            SaveTestSnapshots.AssertEquivalent(saved, SaveTestSnapshots.Capture(services));
            Assert.That(services.WorldTimeFlow.State, Is.EqualTo(WorldTimeFlowState.Ready));
            AssertProgress(services, 360, 0);
            services.WorldTimeFlow.PresentationChanged -= failure;
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            AssertProgress(services, 360, 0.5);
        }

        private static OperationResult<GameClockSnapshot> Advance(CozyTownServices services, string operation)
        {
            switch (operation)
            {
                case "elapsed": return services.DaytimeClock.AdvanceElapsed(0.5);
                case "direct": return services.WorldTime.AdvanceMinutes(1);
                case "sleep": return services.Sleep.SleepForMinutes(60);
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        private static void AssertProgress(CozyTownServices services, int minute, double fraction)
        {
            Assert.That(services.Time.Current.Day, Is.EqualTo(1));
            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(minute));
            Assert.That(services.WorldTimeFlow.Current.Clock, Is.EqualTo(services.Time.Current));
            Assert.That(services.WorldTimeFlow.Current.FractionalMinute, Is.EqualTo(fraction).Within(1e-8));
        }

        private static void AssertFailure(OperationResult result, string code)
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(code));
        }

        private static void AssertFailure<T>(OperationResult<T> result, string code)
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(code));
        }
    }
}
