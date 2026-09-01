using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;

namespace CozyTown.Runtime.Application
{
    public interface ICharacterShopTradingCoordinator
    {
        OperationResult<ShopReceipt> Buy(
            string shopId,
            string characterId,
            string itemId,
            int quantity);

        OperationResult<ShopReceipt> Sell(
            string shopId,
            string characterId,
            string itemId,
            int quantity);
    }
}
