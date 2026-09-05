using CozyTown.Runtime.Content;
using CozyTown.Runtime.Economy;

namespace CozyTown.Runtime.Save
{
    internal static class LegacyV1MigrationDefaults
    {
        internal const int ShopStartingBalance = 10000;
        internal const int MinimumDistinctItems = 4;

        internal static ShopRestockRule[] CreateRestockRules()
        {
            return new[]
            {
                new ShopRestockRule(DefaultMvpIds.Items.PotatoSeed, 700, 3, 6),
                new ShopRestockRule(DefaultMvpIds.Items.CarrotSeed, 700, 3, 6),
                new ShopRestockRule(DefaultMvpIds.Items.TomatoSeed, 700, 3, 6),
                new ShopRestockRule(DefaultMvpIds.Items.ChickenFeed, 1000, 6, 12),
                new ShopRestockRule(DefaultMvpIds.Items.Salt, 750, 3, 8),
                new ShopRestockRule(DefaultMvpIds.Items.Flour, 750, 3, 8)
            };
        }
    }
}
