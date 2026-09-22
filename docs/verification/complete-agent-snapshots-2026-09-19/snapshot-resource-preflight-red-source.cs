using System;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Save
{
    public sealed class CompleteWorldSaveTests
    {
        [Test]
        public void InvalidCompleteResources_AreRejectedBeforeWritingTheClock()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            var snapshot = CompleteSnapshotStorageTests.CreateSnapshot(out _);
            var characters = snapshot.Characters;
            characters[0] = new CharacterEconomySnapshot(characters[0].CharacterId,
                new InventorySnapshot(new[] { new ItemStack("unknown-saved-item", 1) }), characters[0].Wallet);
            var invalid = new GameSaveSnapshot(4, snapshot.WorldSeed, snapshot.Clock, characters,
                snapshot.Shops, snapshot.Farm, snapshot.Livestock, snapshot.FractionalMinute, snapshot.CompleteWorld);
            var storage = new InMemorySaveStorage();
            Assert.That(storage.Save("main", invalid).IsSuccess, Is.True);
            var time = new TrackingTime(services.Time);
            var binding = new WorldSnapshotBinding();
            var adapter = new AcceptingWorldAdapter();
            binding.Attach(adapter);
            var save = new GameSaveCoordinator(services.WorldSeed, time, services.EconomyState,
                services.Farm, services.Livestock, storage, worldSnapshots: binding,
                timeFlow: services.WorldTimeFlow);

            var result = save.Load();

            Assert.That(result.ErrorCode, Is.EqualTo("save.restore_economy_failed"));
            Assert.That(time.RestoreCalls, Is.Zero);
            Assert.That(adapter.Committed, Is.False);
        }

        private sealed class AcceptingWorldAdapter : IWorldSnapshotAdapter, IPreparedWorldRestore
        {
            public bool Committed { get; private set; }
            public CompleteWorldSnapshot CaptureSnapshot(WorldTimeProgress progress, string configuration)
                => throw new InvalidOperationException();
            public IPreparedWorldRestore PrepareRestore(GameSaveSnapshot snapshot, WorldTimeProgress progress, string configuration) => this;
            public void Commit() => Committed = true;
        }

        private sealed class TrackingTime : ITimeService
        {
            private readonly ITimeService _inner;
            public TrackingTime(ITimeService inner) => _inner = inner;
            public int RestoreCalls { get; private set; }
            public GameClockSnapshot Current => _inner.Current;
            public OperationResult<GameClockSnapshot> AdvanceMinutes(int minutes) => _inner.AdvanceMinutes(minutes);
            public GameClockSnapshot SleepToNextDay() => _inner.SleepToNextDay();
            public OperationResult Restore(GameClockSnapshot snapshot) { RestoreCalls++; return _inner.Restore(snapshot); }
        }

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
