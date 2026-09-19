using System.Runtime.Serialization;
using System;

namespace CozyTown.Runtime.Economy
{
    [DataContract]
    public sealed class CharacterTradeTerms
    {
        public CharacterTradeTerms(string sellerId, string buyerId, string itemId, int quantity, int totalPrice)
        {
            if (string.IsNullOrWhiteSpace(sellerId) || string.IsNullOrWhiteSpace(buyerId) || sellerId == buyerId
                || string.IsNullOrWhiteSpace(itemId) || quantity <= 0 || totalPrice < 0)
                throw new ArgumentException("Trade terms require two distinct characters, an item, positive quantity and nonnegative price.");
            SellerId = sellerId; BuyerId = buyerId; ItemId = itemId; Quantity = quantity; TotalPrice = totalPrice;
        }

        [field: DataMember(Name = "sellerId", IsRequired = true)]
        public string SellerId { get; }
        [field: DataMember(Name = "buyerId", IsRequired = true)]
        public string BuyerId { get; }
        [field: DataMember(Name = "itemId", IsRequired = true)]
        public string ItemId { get; }
        [field: DataMember(Name = "quantity", IsRequired = true)]
        public int Quantity { get; }
        [field: DataMember(Name = "totalPrice", IsRequired = true)]
        public int TotalPrice { get; }
    }
}
