using System;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public sealed class GameSaveCoordinator : IGameSaveCoordinator
    {
        private const string MainSlotId = "main";

        private readonly ITimeService _time;
        private readonly IInventory _inventory;
        private readonly IWallet _wallet;
        private readonly IFarmService _farm;
        private readonly ILivestockService _livestock;
        private readonly ISaveStorage _storage;

        public GameSaveCoordinator(
            ITimeService time,
            IInventory inventory,
            IWallet wallet,
            IFarmService farm,
            ILivestockService livestock,
            ISaveStorage storage)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _farm = farm ?? throw new ArgumentNullException(nameof(farm));
            _livestock = livestock ?? throw new ArgumentNullException(nameof(livestock));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public bool HasSave => _storage.Exists(MainSlotId);

        public OperationResult Save()
        {
            GameSaveSnapshot snapshot = CaptureSnapshot();
            if (snapshot.Farm == null
                || snapshot.Livestock == null
                || snapshot.Farm.LastProcessedDay != snapshot.Clock.Day
                || snapshot.Livestock.LastProcessedDay != snapshot.Clock.Day)
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
            OperationResult restore = RestoreSnapshot(loaded.Value);
            return restore.IsSuccess ? restore : RollBack(before, restore.ErrorCode);
        }

        private GameSaveSnapshot CaptureSnapshot()
        {
            return new GameSaveSnapshot(
                GameSaveSnapshot.CurrentSchemaVersion,
                _time.Current,
                _inventory.CaptureSnapshot(),
                _wallet.CaptureSnapshot(),
                _farm.CaptureSnapshot(),
                _livestock.CaptureSnapshot());
        }

        private OperationResult RestoreSnapshot(GameSaveSnapshot snapshot)
        {
            OperationResult timeRestore = _time.Restore(snapshot.Clock);
            if (!timeRestore.IsSuccess)
            {
                return OperationResult.Failure("save.restore_time_failed");
            }

            OperationResult walletRestore = _wallet.Restore(snapshot.Wallet);
            if (!walletRestore.IsSuccess)
            {
                return OperationResult.Failure("save.restore_wallet_failed");
            }

            OperationResult inventoryRestore = _inventory.Restore(snapshot.Inventory);
            if (!inventoryRestore.IsSuccess)
            {
                return OperationResult.Failure("save.restore_inventory_failed");
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
            OperationResult timeRestore = _time.Restore(snapshot.Clock);
            OperationResult walletRestore = _wallet.Restore(snapshot.Wallet);
            OperationResult inventoryRestore = _inventory.Restore(snapshot.Inventory);
            OperationResult farmRestore = _farm.Restore(snapshot.Farm);
            OperationResult livestockRestore = _livestock.Restore(snapshot.Livestock);

            int failureCount = (timeRestore.IsSuccess ? 0 : 1)
                + (walletRestore.IsSuccess ? 0 : 1)
                + (inventoryRestore.IsSuccess ? 0 : 1)
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

            if (!timeRestore.IsSuccess)
            {
                return OperationResult.Failure("save.rollback_time_failed");
            }

            if (!walletRestore.IsSuccess)
            {
                return OperationResult.Failure("save.rollback_wallet_failed");
            }

            if (!inventoryRestore.IsSuccess)
            {
                return OperationResult.Failure("save.rollback_inventory_failed");
            }

            if (!farmRestore.IsSuccess)
            {
                return OperationResult.Failure("save.rollback_farm_failed");
            }

            return OperationResult.Failure("save.rollback_livestock_failed");
        }
    }
}
