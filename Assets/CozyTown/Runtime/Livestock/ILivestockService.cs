using System.Collections.Generic;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Livestock
{
    public interface ILivestockService
    {
        IReadOnlyCollection<AnimalSnapshot> Animals { get; }

        OperationResult Feed(string animalId);

        OperationResult AdvanceDay(int newDay);

        OperationResult CollectProduct(string animalId);

        LivestockSnapshot CaptureSnapshot();

        OperationResult Restore(LivestockSnapshot snapshot);
    }
}
