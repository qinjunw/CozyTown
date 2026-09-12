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

        public GameSaveCoordinator(
            IWorldSeedState worldSeed,
            ITimeService time,
            IEconomyStateStore economyState,
            IFarmService farm,
            ILivestockService livestock,
            ISaveStorage storage,
            CharacterEconomySnapshot[] legacyNpcDefaults = null)
        {
            _worldSeed = worldSeed ?? throw new ArgumentNullException(nameof(worldSeed));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _economyState = economyState
                ?? throw new ArgumentNullException(nameof(economyState));
            _farm = farm ?? throw new ArgumentNullException(nameof(farm));
            _livestock = livestock ?? throw new ArgumentNullException(nameof(livestock));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _legacyNpcDefaults = legacyNpcDefaults?.ToArray() ?? Array.Empty<CharacterEconomySnapshot>();
        }

        public bool HasSave => _storage.Exists(MainSlotId);

        public OperationResult Save()
        {
            GameSaveSnapshot snapshot = CaptureSnapshot();
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

            GameSaveSnapshot before = CaptureSnapshot();
            var candidate = loaded.Value;
            if (_legacyNpcDefaults.Length > 0 && candidate.Characters.Length == 1
                && candidate.Characters[0].CharacterId == DefaultMvpIds.Characters.Player)
                candidate = new GameSaveSnapshot(candidate.SchemaVersion, candidate.WorldSeed, candidate.Clock,
                    candidate.Characters.Concat(_legacyNpcDefaults).ToArray(), candidate.Shops, candidate.Farm, candidate.Livestock);
            OperationResult restore = RestoreSnapshot(candidate);
            return restore.IsSuccess ? restore : RollBack(before, restore.ErrorCode);
        }

        private GameSaveSnapshot CaptureSnapshot()
        {
            EconomyStateSnapshot economy = _economyState.CaptureSnapshot();
            return new GameSaveSnapshot(
                GameSaveSnapshot.CurrentSchemaVersion,
                _worldSeed.Value,
                _time.Current,
                economy.Characters,
                economy.Shops,
                _farm.CaptureSnapshot(),
                _livestock.CaptureSnapshot());
        }

        private OperationResult RestoreSnapshot(GameSaveSnapshot snapshot)
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
