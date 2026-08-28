using System;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Save
{
    [Serializable]
    public sealed class GameSaveSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public GameSaveSnapshot(
            int schemaVersion,
            GameClockSnapshot clock,
            InventorySnapshot inventory,
            WalletSnapshot wallet,
            FarmSnapshot farm,
            LivestockSnapshot livestock)
        {
            SchemaVersion = schemaVersion;
            Clock = clock;
            Inventory = inventory;
            Wallet = wallet;
            Farm = farm;
            Livestock = livestock;
        }

        public int SchemaVersion { get; }

        public GameClockSnapshot Clock { get; }

        public InventorySnapshot Inventory { get; }

        public WalletSnapshot Wallet { get; }

        public FarmSnapshot Farm { get; }

        public LivestockSnapshot Livestock { get; }
    }
}
