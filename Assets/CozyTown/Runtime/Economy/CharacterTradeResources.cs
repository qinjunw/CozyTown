using System;

namespace CozyTown.Runtime.Economy
{
    public sealed class CharacterTradeResources
    {
        internal CharacterTradeResources(CharacterTradeTerms terms, string characterId, int ownedQuantity, int balance)
        {
            Terms = terms; OwnedQuantity = ownedQuantity; Balance = balance;
            Role = characterId == terms.BuyerId ? "buyer" : "seller";
        }

        public CharacterTradeTerms Terms { get; }
        public int OwnedQuantity { get; }
        public int Balance { get; }
        public string Role { get; }
        public int MissingCoins => Role == "buyer" ? Math.Max(0, Terms.TotalPrice - Balance) : 0;
        public int MissingQuantity => Role == "seller" ? Math.Max(0, Terms.Quantity - OwnedQuantity) : 0;
        public bool CanMeetKnownTerms => MissingCoins == 0 && MissingQuantity == 0;
    }
}
