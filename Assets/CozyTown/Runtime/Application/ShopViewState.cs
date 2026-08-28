using System;
using System.Collections.Generic;

namespace CozyTown.Runtime.Application
{
    [Serializable]
    public sealed class ShopLineItem
    {
        public ShopLineItem(
            string itemId,
            string displayName,
            int buyPrice,
            int sellPrice,
            int ownedQuantity)
        {
            ItemId = itemId;
            DisplayName = displayName;
            BuyPrice = buyPrice;
            SellPrice = sellPrice;
            OwnedQuantity = ownedQuantity;
        }

        public string ItemId { get; }

        public string DisplayName { get; }

        public int BuyPrice { get; }

        public int SellPrice { get; }

        public int OwnedQuantity { get; }
    }

    [Serializable]
    public sealed class ShopViewState
    {
        public ShopViewState(int balance, IEnumerable<ShopLineItem> items)
        {
            Balance = balance;
            ShopLineItem[] copy = items == null
                ? Array.Empty<ShopLineItem>()
                : new List<ShopLineItem>(items).ToArray();
            Items = Array.AsReadOnly(copy);
        }

        public int Balance { get; }

        public IReadOnlyList<ShopLineItem> Items { get; }
    }
}
