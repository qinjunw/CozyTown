using System.Collections.Generic;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Economy
{
    public interface IShopService
    {
        IReadOnlyCollection<ShopOffer> Offers { get; }

        OperationResult<ShopReceipt> Buy(string itemId, int quantity);

        OperationResult<ShopReceipt> Sell(string itemId, int quantity);
    }
}
