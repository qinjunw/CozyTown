using System;

namespace CozyTown.Runtime.Economy
{
    [Serializable]
    public sealed class ShopOffer
    {
        public ShopOffer(string itemId, int buyPrice, int sellPrice)
        {
            ItemId = itemId;
            BuyPrice = buyPrice;
            SellPrice = sellPrice;
        }

        public string ItemId { get; }

        public int BuyPrice { get; }

        public int SellPrice { get; }
    }

    [Serializable]
    public readonly struct ShopReceipt
    {
        public ShopReceipt(string itemId, int quantity, int totalPrice, bool isPurchase)
        {
            ItemId = itemId;
            Quantity = quantity;
            TotalPrice = totalPrice;
            IsPurchase = isPurchase;
        }

        public string ItemId { get; }

        public int Quantity { get; }

        public int TotalPrice { get; }

        public bool IsPurchase { get; }
    }
}
