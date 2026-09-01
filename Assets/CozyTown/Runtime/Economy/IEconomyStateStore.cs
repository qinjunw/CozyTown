using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Economy
{
    public interface IEconomyStateStore
    {
        bool TryGetCharacter(
            string characterId,
            out CharacterEconomySnapshot snapshot);

        bool TryGetShop(
            string shopId,
            out ShopEconomySnapshot snapshot);

        OperationResult Commit(
            CharacterEconomySnapshot characterCandidate,
            ShopEconomySnapshot shopCandidate);

        OperationResult CommitShop(ShopEconomySnapshot shopCandidate);
    }
}
