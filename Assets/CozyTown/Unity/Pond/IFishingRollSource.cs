using UnityEngine;

namespace CozyTown.Unity.Pond
{
    public interface IFishingRollSource
    {
        int NextRoll();
    }

    public sealed class UnityRandomFishingRollSource : IFishingRollSource
    {
        public int NextRoll() => Random.Range(0, 100);
    }
}
