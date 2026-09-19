using System;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Save
{
    public sealed class CompleteWorldSaveTests
    {
        [Test]
        public void RejectedWorldPreparation_KeepsTheRunningClockAndDoesNotCommit()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            services.DaytimeClock.AdvanceElapsed(5.125);
            var before = services.WorldTimeFlow.Current;
            var adapter = new RejectingWorldAdapter();
            services.WorldSnapshots.Attach(adapter);

            var result = services.GameSave.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.world_prepare_failed"));
            Assert.That(adapter.Prepared, Is.True);
            Assert.That(services.WorldTimeFlow.Current.TotalMinutes, Is.EqualTo(before.TotalMinutes));
            Assert.That(services.WorldTimeFlow.Current.RebuildVersion, Is.EqualTo(before.RebuildVersion));
            Assert.That(services.WorldTimeFlow.State, Is.EqualTo(WorldTimeFlowState.Ready));
        }

        private sealed class RejectingWorldAdapter : IWorldSnapshotAdapter
        {
            public bool Prepared { get; private set; }
            public CompleteWorldSnapshot CaptureSnapshot(WorldTimeProgress progress, string configuration)
                => throw new InvalidOperationException();
            public IPreparedWorldRestore PrepareRestore(GameSaveSnapshot snapshot, WorldTimeProgress progress, string configuration)
            {
                Prepared = true;
                throw new ArgumentException("Invalid resident route.");
            }
        }
        [Test]
        public void RequiredWorldWithoutAnAdapter_RejectsSaveAndKeepsThePreviousSlot()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            services.WorldTime.AdvanceMinutes(10);
            services.WorldSnapshots.Require();

            var result = services.GameSave.Save();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.world_unbound"));
            Assert.That(services.SaveStorage.Load("main").Value.Clock.MinuteOfDay, Is.EqualTo(360));
            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(370));
        }
    }
}
