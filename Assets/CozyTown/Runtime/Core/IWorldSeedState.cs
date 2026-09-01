namespace CozyTown.Runtime.Core
{
    public interface IWorldSeedState
    {
        int Value { get; }

        OperationResult Restore(int worldSeed);
    }
}
