using System;

namespace CozyTown.Runtime.Economy
{
    [Serializable]
    public sealed class ShopRestockRule
    {
        public ShopRestockRule(
            string itemId,
            int appearancePermille,
            int minQuantity,
            int maxQuantity)
        {
            ItemId = itemId;
            AppearancePermille = appearancePermille;
            MinQuantity = minQuantity;
            MaxQuantity = maxQuantity;
        }

        public string ItemId { get; }

        public int AppearancePermille { get; }

        public int MinQuantity { get; }

        public int MaxQuantity { get; }
    }
}
