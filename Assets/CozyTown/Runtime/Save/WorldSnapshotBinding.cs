using System;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Save
{
    public interface IWorldSnapshotAdapter
    {
        CompleteWorldSnapshot CaptureSnapshot(WorldTimeProgress progress, string contentConfiguration);
        IPreparedWorldRestore PrepareRestore(GameSaveSnapshot snapshot, WorldTimeProgress progress, string contentConfiguration);
    }

    public interface IPreparedWorldRestore { void Commit(); }

    public sealed class WorldSnapshotBinding
    {
        public bool IsRequired { get; private set; }
        public IWorldSnapshotAdapter Adapter { get; private set; }
        public void Require() => IsRequired = true;
        public void Attach(IWorldSnapshotAdapter adapter)
        {
            Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            Require();
        }
        public void Detach(IWorldSnapshotAdapter adapter)
        {
            if (ReferenceEquals(Adapter, adapter)) Adapter = null;
        }
    }
}
