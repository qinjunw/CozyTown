using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Application
{
    public sealed class ShopTradingCoordinator : IShopTradingCoordinator
    {
        private readonly IReadOnlyDictionary<string, string> _displayNames;
        private readonly IShopService _shop;
        private readonly IWallet _wallet;
        private readonly IInventory _inventory;

        public ShopTradingCoordinator(
            IEnumerable<ItemDefinition> items,
            IShopService shop,
            IWallet wallet,
            IInventory inventory)
        {
            _shop = shop ?? throw new ArgumentNullException(nameof(shop));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _displayNames = BuildDisplayNames(items);

            if (_shop.Offers.Any(offer =>
                    offer == null
                    || !_displayNames.ContainsKey(offer.ItemId ?? string.Empty)))
            {
                throw new ArgumentException(
                    "Every shop offer must reference an item definition.",
                    nameof(items));
            }
        }

        public ShopViewState GetCurrentState()
        {
            ShopLineItem[] lines = _shop.Offers
                .OrderBy(offer => offer.ItemId, StringComparer.Ordinal)
                .Select(offer => new ShopLineItem(
                    offer.ItemId,
                    _displayNames[offer.ItemId],
                    offer.BuyPrice,
                    offer.SellPrice,
                    _inventory.Count(offer.ItemId)))
                .ToArray();
            return new ShopViewState(_wallet.Balance, lines);
        }

        public OperationResult<ShopReceipt> Buy(string itemId, int quantity)
        {
            return _shop.Buy(itemId, quantity);
        }

        public OperationResult<ShopReceipt> Sell(string itemId, int quantity)
        {
            return _shop.Sell(itemId, quantity);
        }

        private static IReadOnlyDictionary<string, string> BuildDisplayNames(
            IEnumerable<ItemDefinition> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item == null
                    || string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.DisplayName)
                    || !displayNames.TryAdd(item.Id, item.DisplayName))
                {
                    throw new ArgumentException(
                        "Item definitions must have unique IDs and display names.",
                        nameof(items));
                }
            }

            return displayNames;
        }
    }
}
