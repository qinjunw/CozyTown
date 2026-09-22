using System;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public sealed class GameSaveCoordinator : IGameSaveCoordinator
    {
        private const string MainSlotId = "main";

        private readonly IWorldSeedState _worldSeed;
        private readonly ITimeService _time;
        private readonly IEconomyStateStore _economyState;
        private readonly IFarmService _farm;
        private readonly ILivestockService _livestock;
        private readonly ISaveStorage _storage;
        private readonly CharacterEconomySnapshot[] _legacyNpcDefaults;
        private readonly WorldSnapshotBinding _worldSnapshots;
        private readonly IWorldTimeFlow _timeFlow;
        private readonly string _contentConfiguration;
        private int _sourceSchemaVersion = GameSaveSnapshot.CurrentSchemaVersion;
        public double LoadedFractionalMinute { get; private set; }

        public GameSaveCoordinator(
            IWorldSeedState worldSeed,
            ITimeService time,
            IEconomyStateStore economyState,
            IFarmService farm,
            ILivestockService livestock,
            ISaveStorage storage,
            CharacterEconomySnapshot[] legacyNpcDefaults = null,
            WorldSnapshotBinding worldSnapshots = null,
            IWorldTimeFlow timeFlow = null, string contentConfiguration = null)
        {
            _worldSeed = worldSeed ?? throw new ArgumentNullException(nameof(worldSeed));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _economyState = economyState
                ?? throw new ArgumentNullException(nameof(economyState));
            _farm = farm ?? throw new ArgumentNullException(nameof(farm));
            _livestock = livestock ?? throw new ArgumentNullException(nameof(livestock));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _legacyNpcDefaults = legacyNpcDefaults?.ToArray() ?? Array.Empty<CharacterEconomySnapshot>();
            _worldSnapshots = worldSnapshots;
            _timeFlow = timeFlow;
            _contentConfiguration = contentConfiguration;
        }

        public bool HasSave => _storage.Exists(MainSlotId);

        public OperationResult Save()
        {
            GameSaveSnapshot snapshot = CaptureSnapshot();
            if (_worldSnapshots?.IsRequired == true)
            {
                if (_worldSnapshots.Adapter == null || _timeFlow == null)
                    return OperationResult.Failure("save.world_unbound");
                try
                {
                    var progress = _timeFlow.Current;
                    if (progress.Clock.Day != snapshot.Clock.Day || progress.Clock.MinuteOfDay != snapshot.Clock.MinuteOfDay)
                        return OperationResult.Failure("save.state_misaligned");
                    var world = _worldSnapshots.Adapter.CaptureSnapshot(progress, _contentConfiguration);
                    snapshot = new GameSaveSnapshot(GameSaveSnapshot.CurrentSchemaVersion,
                        snapshot.WorldSeed, snapshot.Clock, snapshot.Characters, snapshot.Shops, snapshot.Farm, snapshot.Livestock,
                        progress.FractionalMinute, world, _sourceSchemaVersion);
                }
                catch (ArgumentException) { return OperationResult.Failure("save.world_capture_failed"); }
                catch (InvalidOperationException) { return OperationResult.Failure("save.world_capture_failed"); }
            }
            if (snapshot.Farm == null
                || snapshot.Livestock == null
                || !DailySettlementSchedule.IsValidProgress(
                    snapshot.Clock,
                    snapshot.Farm.LastProcessedDay)
                || snapshot.Livestock.LastProcessedDay != snapshot.Farm.LastProcessedDay
                || ShopsAreMisaligned(snapshot.Shops, snapshot.Farm.LastProcessedDay))
            {
                return OperationResult.Failure("save.state_misaligned");
            }

            return _storage.Save(MainSlotId, snapshot);
        }

        public OperationResult Load()
        {
            OperationResult<GameSaveSnapshot> loaded =
                _storage.Load(MainSlotId);
            if (!loaded.IsSuccess)
            {
                return OperationResult.Failure(loaded.ErrorCode);
            }

            OperationResult validation = GameSaveSnapshotValidator.Validate(loaded.Value);
            if (!validation.IsSuccess)
            {
                return validation;
            }

            var candidate = loaded.Value;
            if (candidate.SchemaVersion == GameSaveSnapshot.CurrentSchemaVersion && _worldSnapshots?.Adapter == null)
                return OperationResult.Failure("save.world_unbound");
            if (candidate.SchemaVersion <= GameSaveSnapshot.LegacySchemaVersion
                && _legacyNpcDefaults.Length > 0 && candidate.Characters.Length == 1
                && candidate.Characters[0].CharacterId == DefaultMvpIds.Characters.Player)
                candidate = new GameSaveSnapshot(candidate.SchemaVersion, candidate.WorldSeed, candidate.Clock,
                    candidate.Characters.Concat(_legacyNpcDefaults).ToArray(), candidate.Shops, candidate.Farm, candidate.Livestock,
                    sourceSchemaVersion: candidate.SourceSchemaVersion);
            IPreparedWorldRestore prepared = null;
            Action preparedResources = null;
            if (_worldSnapshots?.IsRequired == true)
            {
                if (_worldSnapshots.Adapter == null) return OperationResult.Failure("save.world_unbound");
                try
                {
                    var resources = PrepareResources(candidate);
                    if (!resources.IsSuccess) return OperationResult.Failure(resources.ErrorCode);
                    preparedResources = resources.Value;
                    var progress = new WorldTimeProgress(candidate.Clock, candidate.FractionalMinute, true,
                        (_timeFlow?.Current.RebuildVersion ?? 0) + 1);
                    prepared = _worldSnapshots.Adapter.PrepareRestore(candidate, progress, _contentConfiguration);
                    if (prepared == null) return OperationResult.Failure("save.world_prepare_failed");
                }
                catch (Exception) { return OperationResult.Failure("save.world_prepare_failed"); }
            }
            GameSaveSnapshot before = CaptureSnapshot();
            OperationResult restore = RestoreSnapshot(candidate, preparedResources);
            if (!restore.IsSuccess) return RollBack(before, restore.ErrorCode);
            prepared?.Commit();
            LoadedFractionalMinute = candidate.FractionalMinute;
            _sourceSchemaVersion = candidate.SourceSchemaVersion;
            return restore;
        }

        private GameSaveSnapshot CaptureSnapshot()
        {
            EconomyStateSnapshot economy = _economyState.CaptureSnapshot();
            return new GameSaveSnapshot(
                GameSaveSnapshot.LegacySchemaVersion,
                _worldSeed.Value,
                _time.Current,
                economy.Characters,
                economy.Shops,
                _farm.CaptureSnapshot(),
                _livestock.CaptureSnapshot());
        }

        private OperationResult<Action> PrepareResources(GameSaveSnapshot snapshot)
        {
            if (!(_economyState is InMemoryEconomyStateStore economy)
                || !(_farm is InMemoryFarmService farm) || !(_livestock is InMemoryLivestockService livestock))
                return OperationResult<Action>.Failure("save.resource_adapters_unsupported");
            var preparedEconomy = economy.PrepareRestore(new EconomyStateSnapshot(snapshot.Characters, snapshot.Shops));
            if (!preparedEconomy.IsSuccess) return OperationResult<Action>.Failure("save.restore_economy_failed");
            var preparedFarm = farm.PrepareRestore(snapshot.Farm);
            if (!preparedFarm.IsSuccess) return OperationResult<Action>.Failure("save.restore_farm_failed");
            var preparedLivestock = livestock.PrepareRestore(snapshot.Livestock);
            if (!preparedLivestock.IsSuccess) return OperationResult<Action>.Failure("save.restore_livestock_failed");
            return OperationResult<Action>.Success(() =>
            {
                preparedEconomy.Value();
                preparedFarm.Value();
                preparedLivestock.Value();
            });
        }

        private OperationResult RestoreSnapshot(GameSaveSnapshot snapshot, Action preparedResources = null)
        {
            OperationResult worldSeedRestore = _worldSeed.Restore(snapshot.WorldSeed);
            if (!worldSeedRestore.IsSuccess)
            {
                return OperationResult.Failure("save.restore_world_seed_failed");
            }

            OperationResult timeRestore = _time.Restore(snapshot.Clock);
            if (!timeRestore.IsSuccess)
            {
                return OperationResult.Failure("save.restore_time_failed");
            }

            if (preparedResources != null)
            {
                preparedResources();
                return OperationResult.Success();
            }

            OperationResult economyRestore = _economyState.Restore(
                new EconomyStateSnapshot(snapshot.Characters, snapshot.Shops));
            if (!economyRestore.IsSuccess)
            {
                return OperationResult.Failure("save.restore_economy_failed");
            }

            OperationResult farmRestore = _farm.Restore(snapshot.Farm);
            if (!farmRestore.IsSuccess)
            {
                return OperationResult.Failure("save.restore_farm_failed");
            }

            OperationResult livestockRestore = _livestock.Restore(snapshot.Livestock);
            return livestockRestore.IsSuccess
                ? OperationResult.Success()
                : OperationResult.Failure("save.restore_livestock_failed");
        }

        private OperationResult RollBack(GameSaveSnapshot snapshot, string originalError)
        {
            OperationResult worldSeedRestore = _worldSeed.Restore(snapshot.WorldSeed);
            OperationResult timeRestore = _time.Restore(snapshot.Clock);
            OperationResult economyRestore = _economyState.Restore(
                new EconomyStateSnapshot(snapshot.Characters, snapshot.Shops));
            OperationResult farmRestore = _farm.Restore(snapshot.Farm);
            OperationResult livestockRestore = _livestock.Restore(snapshot.Livestock);

            int failureCount = (worldSeedRestore.IsSuccess ? 0 : 1)
                + (timeRestore.IsSuccess ? 0 : 1)
                + (economyRestore.IsSuccess ? 0 : 1)
                + (farmRestore.IsSuccess ? 0 : 1)
                + (livestockRestore.IsSuccess ? 0 : 1);
            if (failureCount == 0)
            {
                return OperationResult.Failure(originalError);
            }

            if (failureCount > 1)
            {
                return OperationResult.Failure("save.rollback_multiple_failed");
            }

            if (!worldSeedRestore.IsSuccess)
            {
                return OperationResult.Failure("save.rollback_world_seed_failed");
            }

            if (!timeRestore.IsSuccess)
            {
                return OperationResult.Failure("save.rollback_time_failed");
            }

            if (!economyRestore.IsSuccess)
            {
                return OperationResult.Failure("save.rollback_economy_failed");
            }

            if (!farmRestore.IsSuccess)
            {
                return OperationResult.Failure("save.rollback_farm_failed");
            }

            return OperationResult.Failure("save.rollback_livestock_failed");
        }

        private static bool ShopsAreMisaligned(
            ShopEconomySnapshot[] shops,
            int completedDay)
        {
            foreach (ShopEconomySnapshot shop in shops)
            {
                if (shop == null || shop.LastRestockedDay != completedDay)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
