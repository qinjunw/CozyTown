using System;

namespace CozyTown.Runtime.Fishing
{
    [Serializable]
    public sealed class FishingEntry
    {
        public FishingEntry(string fishId, string itemId, int minRollInclusive, int maxRollExclusive)
        {
            FishId = fishId;
            ItemId = itemId;
            MinRollInclusive = minRollInclusive;
            MaxRollExclusive = maxRollExclusive;
        }

        public string FishId { get; }

        public string ItemId { get; }

        public int MinRollInclusive { get; }

        public int MaxRollExclusive { get; }
    }

    [Serializable]
    public readonly struct FishingCatch
    {
        public FishingCatch(string fishId, string itemId)
        {
            FishId = fishId;
            ItemId = itemId;
        }

        public string FishId { get; }

        public string ItemId { get; }
    }
}
