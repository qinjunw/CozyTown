using System;
using System.Collections.Generic;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Application
{
    public sealed class CharacterShopTradingCoordinator : ICharacterShopTradingCoordinator
    {
        private readonly ItemDefinition[] _catalog;
        private readonly IReadOnlyDictionary<string, ShopOffer> _offers;
        private readonly int _backpackCapacitySlots;
        private readonly IEconomyStateStore _stateStore;

        public CharacterShopTradingCoordinator(
            IEnumerable<ItemDefinition> items,
            IEnumerable<ShopOffer> offers,
            int backpackCapacitySlots,
            IEconomyStateStore stateStore)
        {
            if (backpackCapacitySlots <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(backpackCapacitySlots));
            }

            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _catalog = BuildCatalog(items);
            _offers = BuildOffers(offers, _catalog);
            _backpackCapacitySlots = backpackCapacitySlots;
        }

        public OperationResult<ShopReceipt> Buy(
            string shopId,
            string characterId,
            string itemId,
            int quantity)
        {
            if (quantity <= 0)
            {
                return OperationResult<ShopReceipt>.Failure("shop.quantity_invalid");
            }

            if (!_offers.TryGetValue(itemId ?? string.Empty, out ShopOffer offer))
            {
                return OperationResult<ShopReceipt>.Failure("shop.offer_missing");
            }

            if (offer.BuyPrice <= 0)
            {
                return OperationResult<ShopReceipt>.Failure("shop.item_not_for_sale");
            }

            long calculatedTotal = (long)offer.BuyPrice * quantity;
            if (calculatedTotal > int.MaxValue)
            {
                return OperationResult<ShopReceipt>.Failure("shop.total_overflow");
            }

            if (!_stateStore.TryGetCharacter(characterId, out CharacterEconomySnapshot character))
            {
                return OperationResult<ShopReceipt>.Failure("economy.character_unknown");
            }

            if (!_stateStore.TryGetShop(shopId, out ShopEconomySnapshot shop))
            {
                return OperationResult<ShopReceipt>.Failure("economy.shop_unknown");
            }

            int total = (int)calculatedTotal;
            if (character.Wallet.Balance < total)
            {
                return OperationResult<ShopReceipt>.Failure("wallet.insufficient_funds");
            }

            if (shop.Wallet.Balance > int.MaxValue - total)
            {
                return OperationResult<ShopReceipt>.Failure("wallet.balance_overflow");
            }

            if (!TryRemove(
                    shop.Stock,
                    itemId,
                    quantity,
                    out InventorySnapshot shopStockCandidate))
            {
                return OperationResult<ShopReceipt>.Failure(
                    "inventory.insufficient_quantity");
            }

            var backpackCandidate = new InMemoryInventory(
                _catalog,
                _backpackCapacitySlots);
            OperationResult restore = backpackCandidate.Restore(character.Backpack);
            if (!restore.IsSuccess)
            {
                return OperationResult<ShopReceipt>.Failure(restore.ErrorCode);
            }

            OperationResult add = backpackCandidate.Add(itemId, quantity);
            if (!add.IsSuccess)
            {
                return OperationResult<ShopReceipt>.Failure(add.ErrorCode);
            }

            var characterCandidate = new CharacterEconomySnapshot(
                character.CharacterId,
                backpackCandidate.CaptureSnapshot(),
                new WalletSnapshot(character.Wallet.Balance - total));
            var shopCandidate = new ShopEconomySnapshot(
                shop.ShopId,
                shopStockCandidate,
                new WalletSnapshot(shop.Wallet.Balance + total),
                shop.LastRestockedDay,
                shop.RestockAlgorithmVersion);
            OperationResult commit = _stateStore.Commit(characterCandidate, shopCandidate);
            if (!commit.IsSuccess)
            {
                return OperationResult<ShopReceipt>.Failure(commit.ErrorCode);
            }

            return OperationResult<ShopReceipt>.Success(
                new ShopReceipt(itemId, quantity, total, true));
        }

        private static ItemDefinition[] BuildCatalog(IEnumerable<ItemDefinition> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var catalog = new List<ItemDefinition>();
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item == null
                    || string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.DisplayName)
                    || item.MaxStack <= 0
                    || !itemIds.Add(item.Id))
                {
                    throw new ArgumentException(
                        "Item definitions must be valid and have unique IDs.",
                        nameof(items));
                }

                catalog.Add(item);
            }

            return catalog.ToArray();
        }

        private static IReadOnlyDictionary<string, ShopOffer> BuildOffers(
            IEnumerable<ShopOffer> offers,
            IEnumerable<ItemDefinition> catalog)
        {
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in catalog)
            {
                itemIds.Add(item.Id);
            }

            var byItemId = new Dictionary<string, ShopOffer>(StringComparer.Ordinal);
            foreach (ShopOffer offer in offers ?? Array.Empty<ShopOffer>())
            {
                if (offer == null
                    || string.IsNullOrWhiteSpace(offer.ItemId)
                    || !itemIds.Contains(offer.ItemId)
                    || !byItemId.TryAdd(offer.ItemId, offer))
                {
                    throw new ArgumentException(
                        "Shop offers must be unique and reference item definitions.",
                        nameof(offers));
                }
            }

            return byItemId;
        }

        private static bool TryRemove(
            InventorySnapshot stock,
            string itemId,
            int quantity,
            out InventorySnapshot candidate)
        {
            var items = new List<ItemStack>();
            bool found = false;
            foreach (ItemStack stack in stock.Items)
            {
                if (!string.Equals(stack.ItemId, itemId, StringComparison.Ordinal))
                {
                    items.Add(stack);
                    continue;
                }

                if (stack.Quantity < quantity)
                {
                    candidate = null;
                    return false;
                }

                found = true;
                int remainder = stack.Quantity - quantity;
                if (remainder > 0)
                {
                    items.Add(new ItemStack(stack.ItemId, remainder));
                }
            }

            candidate = found ? new InventorySnapshot(items.ToArray()) : null;
            return found;
        }
    }
}
