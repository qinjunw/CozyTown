using System.Collections.Generic;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Fishing
{
    public interface IFishingService
    {
        IReadOnlyCollection<FishingEntry> Entries { get; }

        OperationResult<FishingCatch> Catch(int roll);
    }
}
