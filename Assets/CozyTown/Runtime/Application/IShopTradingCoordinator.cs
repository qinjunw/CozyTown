using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;

namespace CozyTown.Runtime.Application
{
    public interface IShopTradingCoordinator
    {
        ShopViewState GetCurrentState();

        OperationResult<ShopReceipt> Buy(string itemId, int quantity);

        OperationResult<ShopReceipt> Sell(string itemId, int quantity);
    }
}
