using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Application
{
    public interface IGameSaveCoordinator
    {
        bool HasSave { get; }

        OperationResult Save();

        OperationResult Load();
    }
}
