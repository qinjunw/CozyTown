using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Application
{
    public sealed class CharacterResourceTrading
    {
        private readonly IEconomyStateStore _store;
        private readonly ItemDefinition[] _items;
        private readonly int _capacity;

        public CharacterResourceTrading(IEconomyStateStore store, IEnumerable<ItemDefinition> items, int capacity)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _items = items?.ToArray() ?? throw new ArgumentNullException(nameof(items));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public bool IsNeeded(CharacterTradeTerms terms)
            => terms != null && _store.TryGetCharacter(terms.BuyerId, out var buyer)
                && Quantity(buyer.Backpack, terms.ItemId) < terms.Quantity;

        public CharacterTradeResources Inspect(CharacterTradeTerms terms, string characterId)
        {
            if (terms == null || (characterId != terms.SellerId && characterId != terms.BuyerId)
                || !_store.TryGetCharacter(characterId, out var character)) return null;
            return new CharacterTradeResources(terms, characterId, Quantity(character.Backpack, terms.ItemId), character.Wallet.Balance);
        }

        public OperationResult Exchange(CharacterTradeTerms terms)
        {
            if (terms == null) return OperationResult.Failure("resource.terms_missing");
            if (!_store.TryGetCharacter(terms.SellerId, out var seller) || !_store.TryGetCharacter(terms.BuyerId, out var buyer))
                return OperationResult.Failure("economy.character_unknown");
            if (buyer.Wallet.Balance < terms.TotalPrice) return OperationResult.Failure("wallet.insufficient_funds");
            if (seller.Wallet.Balance > int.MaxValue - terms.TotalPrice) return OperationResult.Failure("wallet.balance_overflow");
            var sellerInventory = new InMemoryInventory(_items, _capacity);
            var buyerInventory = new InMemoryInventory(_items, _capacity);
            var result = sellerInventory.Restore(seller.Backpack);
            if (!result.IsSuccess) return result;
            result = buyerInventory.Restore(buyer.Backpack);
            if (!result.IsSuccess) return result;
            result = sellerInventory.Remove(terms.ItemId, terms.Quantity);
            if (!result.IsSuccess) return result;
            result = buyerInventory.Add(terms.ItemId, terms.Quantity);
            if (!result.IsSuccess) return result;
            return _store.CommitCharacters(
                new CharacterEconomySnapshot(seller.CharacterId, sellerInventory.CaptureSnapshot(), new WalletSnapshot(seller.Wallet.Balance + terms.TotalPrice)),
                new CharacterEconomySnapshot(buyer.CharacterId, buyerInventory.CaptureSnapshot(), new WalletSnapshot(buyer.Wallet.Balance - terms.TotalPrice)));
        }

        private static int Quantity(InventorySnapshot snapshot, string itemId)
            => snapshot.Items.Where(item => item.ItemId == itemId).Select(item => item.Quantity).FirstOrDefault();
    }
}
