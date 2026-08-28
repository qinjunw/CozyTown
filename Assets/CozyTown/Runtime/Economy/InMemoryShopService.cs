using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Economy
{
    public sealed class InMemoryShopService : IShopService
    {
        private readonly Dictionary<string, ShopOffer> _offers;
        private readonly IWallet _wallet;
        private readonly IInventory _inventory;

        public InMemoryShopService(
            IEnumerable<ShopOffer> offers,
            IWallet wallet,
            IInventory inventory)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _offers = (offers ?? Array.Empty<ShopOffer>())
                .Where(IsValidOffer)
                .GroupBy(offer => offer.ItemId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            Offers = _offers.Values.OrderBy(offer => offer.ItemId, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyCollection<ShopOffer> Offers { get; }

        public OperationResult<ShopReceipt> Buy(string itemId, int quantity)
        {
            if (!TryGetTotal(itemId, quantity, true, out ShopOffer offer, out int total, out string error))
            {
                return OperationResult<ShopReceipt>.Failure(error);
            }

            OperationResult debit = _wallet.Debit(total);
            if (!debit.IsSuccess)
            {
                return OperationResult<ShopReceipt>.Failure(debit.ErrorCode);
            }

            OperationResult add = _inventory.Add(itemId, quantity);
            if (!add.IsSuccess)
            {
                _wallet.Credit(total);
                return OperationResult<ShopReceipt>.Failure(add.ErrorCode);
            }

            return OperationResult<ShopReceipt>.Success(
                new ShopReceipt(offer.ItemId, quantity, total, true));
        }

        public OperationResult<ShopReceipt> Sell(string itemId, int quantity)
        {
            if (!TryGetTotal(itemId, quantity, false, out ShopOffer offer, out int total, out string error))
            {
                return OperationResult<ShopReceipt>.Failure(error);
            }

            OperationResult remove = _inventory.Remove(itemId, quantity);
            if (!remove.IsSuccess)
            {
                return OperationResult<ShopReceipt>.Failure(remove.ErrorCode);
            }

            OperationResult credit = _wallet.Credit(total);
            if (!credit.IsSuccess)
            {
                _inventory.Add(itemId, quantity);
                return OperationResult<ShopReceipt>.Failure(credit.ErrorCode);
            }

            return OperationResult<ShopReceipt>.Success(
                new ShopReceipt(offer.ItemId, quantity, total, false));
        }

        private bool TryGetTotal(
            string itemId,
            int quantity,
            bool isPurchase,
            out ShopOffer offer,
            out int total,
            out string error)
        {
            offer = null;
            total = 0;
            error = string.Empty;

            if (quantity <= 0)
            {
                error = "shop.quantity_invalid";
                return false;
            }

            if (!_offers.TryGetValue(itemId ?? string.Empty, out offer))
            {
                error = "shop.offer_missing";
                return false;
            }

            int unitPrice = isPurchase ? offer.BuyPrice : offer.SellPrice;
            if (unitPrice <= 0)
            {
                error = isPurchase ? "shop.item_not_for_sale" : "shop.item_not_accepted";
                return false;
            }

            long calculated = (long)unitPrice * quantity;
            if (calculated > int.MaxValue)
            {
                error = "shop.total_overflow";
                return false;
            }

            total = (int)calculated;
            return true;
        }

        private static bool IsValidOffer(ShopOffer offer)
        {
            return offer != null
                && !string.IsNullOrWhiteSpace(offer.ItemId)
                && offer.BuyPrice >= 0
                && offer.SellPrice >= 0;
        }
    }
}
