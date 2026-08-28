using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Save
{
    public interface ISaveStorage
    {
        bool Exists(string slotId);

        OperationResult Save(string slotId, GameSaveSnapshot snapshot);

        OperationResult<GameSaveSnapshot> Load(string slotId);
    }
}
