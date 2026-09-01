using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Economy
{
    public interface IShopStockReplacementPolicy
    {
        OperationResult<ShopEconomySnapshot> CreateCandidate(
            int worldSeed,
            ShopEconomySnapshot current,
            int targetDay);
    }
}
