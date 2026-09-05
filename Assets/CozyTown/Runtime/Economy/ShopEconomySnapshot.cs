using System;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Economy
{
    [Serializable]
    public sealed class ShopEconomySnapshot
    {
        private readonly InventorySnapshot _stock;

        public ShopEconomySnapshot(
            string shopId,
            InventorySnapshot stock,
            WalletSnapshot wallet,
            int lastRestockedDay,
            int restockAlgorithmVersion)
        {
            ShopId = shopId;
            _stock = Copy(stock);
            Wallet = wallet;
            LastRestockedDay = lastRestockedDay;
            RestockAlgorithmVersion = restockAlgorithmVersion;
        }

        public string ShopId { get; }

        public InventorySnapshot Stock => Copy(_stock);

        public WalletSnapshot Wallet { get; }

        public int LastRestockedDay { get; }

        public int RestockAlgorithmVersion { get; }

        private static InventorySnapshot Copy(InventorySnapshot snapshot)
        {
            return snapshot == null ? null : new InventorySnapshot(snapshot.Items);
        }
    }
}
