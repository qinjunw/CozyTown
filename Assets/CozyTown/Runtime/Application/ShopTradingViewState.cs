using System;
using System.Collections.Generic;

namespace CozyTown.Runtime.Application
{
    [Serializable]
    public sealed class ShopTradingLineItem
    {
        public ShopTradingLineItem(
            string itemId,
            string displayName,
            int unitPrice,
            int quantity)
        {
            ItemId = itemId;
            DisplayName = displayName;
            UnitPrice = unitPrice;
            Quantity = quantity;
        }

        public string ItemId { get; }

        public string DisplayName { get; }

        public int UnitPrice { get; }

        public int Quantity { get; }
    }

    [Serializable]
    public sealed class ShopTradingViewState
    {
        public ShopTradingViewState(
            int characterBalance,
            int shopBalance,
            IEnumerable<ShopTradingLineItem> purchaseItems,
            IEnumerable<ShopTradingLineItem> saleItems)
        {
            CharacterBalance = characterBalance;
            ShopBalance = shopBalance;
            PurchaseItems = Copy(purchaseItems);
            SaleItems = Copy(saleItems);
        }

        public int CharacterBalance { get; }

        public int ShopBalance { get; }

        public IReadOnlyList<ShopTradingLineItem> PurchaseItems { get; }

        public IReadOnlyList<ShopTradingLineItem> SaleItems { get; }

        private static IReadOnlyList<ShopTradingLineItem> Copy(
            IEnumerable<ShopTradingLineItem> items)
        {
            ShopTradingLineItem[] copy = items == null
                ? Array.Empty<ShopTradingLineItem>()
                : new List<ShopTradingLineItem>(items).ToArray();
            return Array.AsReadOnly(copy);
        }
    }
}
