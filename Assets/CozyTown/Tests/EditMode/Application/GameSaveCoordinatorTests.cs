using System;
using System.Collections.Generic;
using System.IO;
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
    public sealed class GameSaveCoordinatorTests
    {
        [Test]
        public void JsonSaveThenFreshLoad_RestoresEquivalentPersistentState()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "CozyTown.Tests",
                Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(directory, "main.json");
                CozyTownServices source = CozyTownCompositionRoot.CreateDefault();
                Assert.That(
                    source.Inventory.Add(DefaultMvpIds.Items.Potato, 2).IsSuccess,
                    Is.True);
                Assert.That(source.Wallet.Debit(40).IsSuccess, Is.True);
                GameSaveSnapshot expected = SaveTestSnapshots.Capture(source);
                var storage = new JsonFileSaveStorage(path);
                var sourceCoordinator = CreateCoordinator(source, storage);
                Assert.That(sourceCoordinator.Save().IsSuccess, Is.True);
                CozyTownServices restored = CozyTownCompositionRoot.CreateDefault();
                var restoredCoordinator = CreateCoordinator(restored, storage);

                OperationResult result = restoredCoordinator.Load();

                Assert.That(result.IsSuccess, Is.True);
                SaveTestSnapshots.AssertEquivalent(expected, SaveTestSnapshots.Capture(restored));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public void SaveThenLoad_RestoresEveryPersistentModuleToSavedState()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            var storage = new InMemorySaveStorage();
            var coordinator = CreateCoordinator(services, storage);
            Assert.That(
                services.Inventory.Add(DefaultMvpIds.Items.Potato, 2).IsSuccess,
                Is.True);
            Assert.That(services.Wallet.Debit(40).IsSuccess, Is.True);
            GameSaveSnapshot expected = SaveTestSnapshots.Capture(services);
            Assert.That(coordinator.Save().IsSuccess, Is.True);

            Assert.That(
                services.Inventory.Remove(DefaultMvpIds.Items.Potato, 1).IsSuccess,
                Is.True);
            Assert.That(services.Wallet.Credit(75).IsSuccess, Is.True);
            Assert.That(services.WorldSeed.Restore(999).IsSuccess, Is.True);
            Assert.That(services.Time.AdvanceMinutes(30).IsSuccess, Is.True);

            OperationResult result = coordinator.Load();

            Assert.That(result.IsSuccess, Is.True);
            SaveTestSnapshots.AssertEquivalent(expected, SaveTestSnapshots.Capture(services));
        }

        [Test]
        public void Load_WhenSnapshotContainsUnknownItem_RollsBackEarlierModuleRestores()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);
            CharacterEconomySnapshot player = before.Characters[0];
            GameSaveSnapshot invalid = new GameSaveSnapshot(
                GameSaveSnapshot.CurrentSchemaVersion,
                before.WorldSeed,
                new GameClockSnapshot(1, 10 * 60),
                new[]
                {
                    new CharacterEconomySnapshot(
                        player.CharacterId,
                        new InventorySnapshot(
                            new[] { new ItemStack("unknown-item", 1) }),
                        new WalletSnapshot(1))
                },
                before.Shops,
                services.Farm.CaptureSnapshot(),
                services.Livestock.CaptureSnapshot());
            var storage = new InMemorySaveStorage();
            Assert.That(storage.Save(JsonFileSaveStorage.MainSlotId, invalid).IsSuccess, Is.True);
            var coordinator = CreateCoordinator(services, storage);

            OperationResult result = coordinator.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.restore_economy_failed"));
            SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
        }

        [Test]
        public void Load_WhenSnapshotHasDuplicateCharacterIds_LeavesRuntimeUnchanged()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);
            var invalid = new GameSaveSnapshot(
                before.SchemaVersion,
                worldSeed: 777,
                before.Clock,
                new[] { before.Characters[0], before.Characters[0] },
                before.Shops,
                before.Farm,
                before.Livestock);
            var coordinator = CreateCoordinator(
                services,
                new FixedLoadSaveStorage(invalid));

            OperationResult result = coordinator.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.payload_invalid"));
            SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
        }

        [Test]
        public void Load_WhenShopDateDoesNotMatchClock_LeavesRuntimeUnchanged()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);
            ShopEconomySnapshot shop = before.Shops[0];
            var invalid = new GameSaveSnapshot(
                before.SchemaVersion,
                worldSeed: 777,
                before.Clock,
                before.Characters,
                new[]
                {
                    new ShopEconomySnapshot(
                        shop.ShopId,
                        shop.Stock,
                        shop.Wallet,
                        before.Clock.Day + 1,
                        shop.RestockAlgorithmVersion)
                },
                before.Farm,
                before.Livestock);
            var coordinator = CreateCoordinator(
                services,
                new FixedLoadSaveStorage(invalid));

            OperationResult result = coordinator.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.payload_invalid"));
            SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
        }

        [Test]
        public void Load_WhenLastModuleMutatesThenFails_RollsBackAllFiveModules()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            GameSaveSnapshot before = SaveTestSnapshots.Capture(services);
            FarmPlotSnapshot[] targetPlots =
                (FarmPlotSnapshot[])before.Farm.Plots.Clone();
            targetPlots[0] = new FarmPlotSnapshot(
                targetPlots[0].PlotId,
                DefaultMvpIds.Crops.Potato,
                growthProgressDays: 0,
                wateredToday: true,
                status: FarmPlotStatus.Growing);
            var target = new GameSaveSnapshot(
                GameSaveSnapshot.CurrentSchemaVersion,
                worldSeed: 777,
                new GameClockSnapshot(1, 10 * 60),
                new[]
                {
                    new CharacterEconomySnapshot(
                        DefaultMvpIds.Characters.Player,
                        new InventorySnapshot(
                            new[] { new ItemStack(DefaultMvpIds.Items.Potato, 1) }),
                        new WalletSnapshot(1))
                },
                before.Shops,
                new FarmSnapshot(1, targetPlots),
                new LivestockSnapshot(
                    1,
                    new[]
                    {
                        new AnimalSnapshot(
                            DefaultMvpIds.Livestock.Hen,
                            DefaultMvpIds.Livestock.ChickenSpecies,
                            fedToday: true,
                            productReady: false)
                    }));
            var storage = new InMemorySaveStorage();
            Assert.That(storage.Save(JsonFileSaveStorage.MainSlotId, target).IsSuccess, Is.True);
            var worldSeed = new TrackingWorldSeedState(services.WorldSeed);
            var time = new TrackingTimeService(services.Time);
            var economy = new TrackingEconomyStateStore(services.EconomyState);
            var farm = new TrackingFarmService(services.Farm);
            var livestock = new FailFirstRestoreLivestockService(services.Livestock);
            var coordinator = new GameSaveCoordinator(
                worldSeed,
                time,
                economy,
                farm,
                livestock,
                storage);

            OperationResult result = coordinator.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.restore_livestock_failed"));
            Assert.That(worldSeed.RestoreCallCount, Is.EqualTo(2));
            Assert.That(time.RestoreCallCount, Is.EqualTo(2));
            Assert.That(economy.RestoreCallCount, Is.EqualTo(2));
            Assert.That(farm.RestoreCallCount, Is.EqualTo(2));
            Assert.That(livestock.RestoreCallCount, Is.EqualTo(2));
            SaveTestSnapshots.AssertEquivalent(before, SaveTestSnapshots.Capture(services));
        }

        [Test]
        public void Save_WhenModuleDaysAreMisaligned_DoesNotReplaceExistingSlot()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            var storage = new InMemorySaveStorage();
            var coordinator = CreateCoordinator(services, storage);
            Assert.That(coordinator.Save().IsSuccess, Is.True);
            GameSaveSnapshot original = storage.Load(JsonFileSaveStorage.MainSlotId).Value;
            Assert.That(services.Time.AdvanceMinutes(24 * 60).IsSuccess, Is.True);

            OperationResult result = coordinator.Save();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.state_misaligned"));
            SaveTestSnapshots.AssertEquivalent(
                original,
                storage.Load(JsonFileSaveStorage.MainSlotId).Value);
        }

        private static GameSaveCoordinator CreateCoordinator(
            CozyTownServices services,
            ISaveStorage storage)
        {
            return new GameSaveCoordinator(
                services.WorldSeed,
                services.Time,
                services.EconomyState,
                services.Farm,
                services.Livestock,
                storage);
        }

        private sealed class TrackingTimeService : ITimeService
        {
            private readonly ITimeService _inner;

            public TrackingTimeService(ITimeService inner)
            {
                _inner = inner;
            }

            public int RestoreCallCount { get; private set; }

            public GameClockSnapshot Current => _inner.Current;

            public OperationResult<GameClockSnapshot> AdvanceMinutes(int minutes) =>
                _inner.AdvanceMinutes(minutes);

            public GameClockSnapshot SleepToNextDay() => _inner.SleepToNextDay();

            public OperationResult Restore(GameClockSnapshot snapshot)
            {
                RestoreCallCount++;
                return _inner.Restore(snapshot);
            }
        }

        private sealed class FixedLoadSaveStorage : ISaveStorage
        {
            private readonly GameSaveSnapshot _snapshot;

            public FixedLoadSaveStorage(GameSaveSnapshot snapshot)
            {
                _snapshot = snapshot;
            }

            public bool Exists(string slotId) => true;

            public OperationResult Save(string slotId, GameSaveSnapshot snapshot) =>
                OperationResult.Failure("test.save_not_supported");

            public OperationResult<GameSaveSnapshot> Load(string slotId) =>
                OperationResult<GameSaveSnapshot>.Success(_snapshot);
        }

        private sealed class TrackingWorldSeedState : IWorldSeedState
        {
            private readonly IWorldSeedState _inner;

            public TrackingWorldSeedState(IWorldSeedState inner)
            {
                _inner = inner;
            }

            public int RestoreCallCount { get; private set; }

            public int Value => _inner.Value;

            public OperationResult Restore(int worldSeed)
            {
                RestoreCallCount++;
                return _inner.Restore(worldSeed);
            }
        }

        private sealed class TrackingEconomyStateStore : IEconomyStateStore
        {
            private readonly IEconomyStateStore _inner;

            public TrackingEconomyStateStore(IEconomyStateStore inner)
            {
                _inner = inner;
            }

            public int RestoreCallCount { get; private set; }

            public bool TryGetCharacter(
                string characterId,
                out CharacterEconomySnapshot snapshot) =>
                _inner.TryGetCharacter(characterId, out snapshot);

            public bool TryGetShop(string shopId, out ShopEconomySnapshot snapshot) =>
                _inner.TryGetShop(shopId, out snapshot);

            public EconomyStateSnapshot CaptureSnapshot() => _inner.CaptureSnapshot();

            public OperationResult Restore(EconomyStateSnapshot snapshot)
            {
                RestoreCallCount++;
                return _inner.Restore(snapshot);
            }

            public OperationResult Commit(
                CharacterEconomySnapshot characterCandidate,
                ShopEconomySnapshot shopCandidate) =>
                _inner.Commit(characterCandidate, shopCandidate);

            public OperationResult CommitShop(ShopEconomySnapshot shopCandidate) =>
                _inner.CommitShop(shopCandidate);

            public OperationResult CommitCharacter(
                CharacterEconomySnapshot characterCandidate) =>
                _inner.CommitCharacter(characterCandidate);
        }

        private sealed class TrackingFarmService : IFarmService
        {
            private readonly IFarmService _inner;

            public TrackingFarmService(IFarmService inner)
            {
                _inner = inner;
            }

            public int RestoreCallCount { get; private set; }

            public IReadOnlyCollection<FarmPlotSnapshot> Plots => _inner.Plots;

            public OperationResult Plant(string plotId, string seedItemId) =>
                _inner.Plant(plotId, seedItemId);

            public OperationResult Water(string plotId) => _inner.Water(plotId);

            public OperationResult AdvanceDay(int newDay) => _inner.AdvanceDay(newDay);

            public OperationResult Harvest(string plotId) => _inner.Harvest(plotId);

            public FarmSnapshot CaptureSnapshot() => _inner.CaptureSnapshot();

            public OperationResult Restore(FarmSnapshot snapshot)
            {
                RestoreCallCount++;
                return _inner.Restore(snapshot);
            }
        }

        private sealed class FailFirstRestoreLivestockService : ILivestockService
        {
            private readonly ILivestockService _inner;

            public FailFirstRestoreLivestockService(ILivestockService inner)
            {
                _inner = inner;
            }

            public int RestoreCallCount { get; private set; }

            public IReadOnlyCollection<AnimalSnapshot> Animals => _inner.Animals;

            public OperationResult Feed(string animalId) => _inner.Feed(animalId);

            public OperationResult AdvanceDay(int newDay) => _inner.AdvanceDay(newDay);

            public OperationResult CollectProduct(string animalId) => _inner.CollectProduct(animalId);

            public LivestockSnapshot CaptureSnapshot() => _inner.CaptureSnapshot();

            public OperationResult Restore(LivestockSnapshot snapshot)
            {
                RestoreCallCount++;
                OperationResult result = _inner.Restore(snapshot);
                return RestoreCallCount == 1 && result.IsSuccess
                    ? OperationResult.Failure("test.restore_failed")
                    : result;
            }
        }
    }
}
