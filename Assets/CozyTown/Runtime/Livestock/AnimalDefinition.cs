using System;

namespace CozyTown.Runtime.Livestock
{
    [Serializable]
    public sealed class AnimalDefinition
    {
        public AnimalDefinition(
            string speciesId,
            string feedItemId,
            string productItemId,
            int productQuantity)
        {
            SpeciesId = speciesId;
            FeedItemId = feedItemId;
            ProductItemId = productItemId;
            ProductQuantity = productQuantity;
        }

        public string SpeciesId { get; }

        public string FeedItemId { get; }

        public string ProductItemId { get; }

        public int ProductQuantity { get; }
    }
}
