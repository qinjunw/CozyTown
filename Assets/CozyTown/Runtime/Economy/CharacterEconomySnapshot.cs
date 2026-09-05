using System;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Economy
{
    [Serializable]
    public sealed class CharacterEconomySnapshot
    {
        private readonly InventorySnapshot _backpack;

        public CharacterEconomySnapshot(
            string characterId,
            InventorySnapshot backpack,
            WalletSnapshot wallet)
        {
            CharacterId = characterId;
            _backpack = Copy(backpack);
            Wallet = wallet;
        }

        public string CharacterId { get; }

        public InventorySnapshot Backpack => Copy(_backpack);

        public WalletSnapshot Wallet { get; }

        private static InventorySnapshot Copy(InventorySnapshot snapshot)
        {
            return snapshot == null ? null : new InventorySnapshot(snapshot.Items);
        }
    }
}
