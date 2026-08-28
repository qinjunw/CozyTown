using System;
using System.Collections.Generic;

namespace CozyTown.Runtime.Application
{
    [Serializable]
    public sealed class AnimalView
    {
        public AnimalView(
            string animalId,
            string speciesId,
            string feedItemId,
            string feedDisplayName,
            int ownedFeedQuantity,
            string productItemId,
            string productDisplayName,
            int productQuantity,
            bool fedToday,
            bool productReady)
        {
            AnimalId = animalId;
            SpeciesId = speciesId;
            FeedItemId = feedItemId;
            FeedDisplayName = feedDisplayName;
            OwnedFeedQuantity = ownedFeedQuantity;
            ProductItemId = productItemId;
            ProductDisplayName = productDisplayName;
            ProductQuantity = productQuantity;
            FedToday = fedToday;
            ProductReady = productReady;
        }

        public string AnimalId { get; }
        public string SpeciesId { get; }
        public string FeedItemId { get; }
        public string FeedDisplayName { get; }
        public int OwnedFeedQuantity { get; }
        public string ProductItemId { get; }
        public string ProductDisplayName { get; }
        public int ProductQuantity { get; }
        public bool FedToday { get; }
        public bool ProductReady { get; }
    }

    [Serializable]
    public sealed class LivestockViewState
    {
        public LivestockViewState(IEnumerable<AnimalView> animals)
        {
            AnimalView[] copy = animals == null
                ? Array.Empty<AnimalView>()
                : new List<AnimalView>(animals).ToArray();
            Animals = Array.AsReadOnly(copy);
        }

        public IReadOnlyList<AnimalView> Animals { get; }
    }
}
