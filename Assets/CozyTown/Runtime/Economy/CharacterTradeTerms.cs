using System;

namespace CozyTown.Runtime.Economy
{
    public sealed class CharacterTradeTerms
    {
        public CharacterTradeTerms(string sellerId, string buyerId, string itemId, int quantity, int totalPrice)
        {
            if (string.IsNullOrWhiteSpace(sellerId) || string.IsNullOrWhiteSpace(buyerId) || sellerId == buyerId
                || string.IsNullOrWhiteSpace(itemId) || quantity <= 0 || totalPrice < 0)
                throw new ArgumentException("Trade terms require two distinct characters, an item, positive quantity and nonnegative price.");
            SellerId = sellerId; BuyerId = buyerId; ItemId = itemId; Quantity = quantity; TotalPrice = totalPrice;
        }

        public string SellerId { get; }
        public string BuyerId { get; }
        public string ItemId { get; }
        public int Quantity { get; }
        public int TotalPrice { get; }
    }
}
