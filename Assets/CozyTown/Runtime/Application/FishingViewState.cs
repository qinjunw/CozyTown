using System;
using System.Collections.Generic;

namespace CozyTown.Runtime.Application
{
    [Serializable]
    public sealed class FishingEntryView
    {
        public FishingEntryView(
            string fishId,
            string itemId,
            string displayName,
            int minRollInclusive,
            int maxRollExclusive,
            int ownedQuantity)
        {
            FishId = fishId;
            ItemId = itemId;
            DisplayName = displayName;
            MinRollInclusive = minRollInclusive;
            MaxRollExclusive = maxRollExclusive;
            OwnedQuantity = ownedQuantity;
        }

        public string FishId { get; }
        public string ItemId { get; }
        public string DisplayName { get; }
        public int MinRollInclusive { get; }
        public int MaxRollExclusive { get; }
        public int OwnedQuantity { get; }
    }

    [Serializable]
    public sealed class FishingViewState
    {
        public FishingViewState(IEnumerable<FishingEntryView> entries)
        {
            FishingEntryView[] copy = entries == null
                ? Array.Empty<FishingEntryView>()
                : new List<FishingEntryView>(entries).ToArray();
            Entries = Array.AsReadOnly(copy);
        }

        public IReadOnlyList<FishingEntryView> Entries { get; }
    }
}
