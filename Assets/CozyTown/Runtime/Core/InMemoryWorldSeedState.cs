namespace CozyTown.Runtime.Core
{
    public sealed class InMemoryWorldSeedState : IWorldSeedState
    {
        public InMemoryWorldSeedState(int worldSeed)
        {
            Value = worldSeed;
        }

        public int Value { get; private set; }

        public OperationResult Restore(int worldSeed)
        {
            Value = worldSeed;
            return OperationResult.Success();
        }
    }
}
