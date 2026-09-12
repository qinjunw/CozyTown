namespace CozyTown.Runtime.Economy
{
    public sealed class CharacterTradeResources
    {
        internal CharacterTradeResources(CharacterTradeTerms terms, int ownedQuantity, int balance)
        {
            Terms = terms; OwnedQuantity = ownedQuantity; Balance = balance;
        }

        public CharacterTradeTerms Terms { get; }
        public int OwnedQuantity { get; }
        public int Balance { get; }
    }
}
