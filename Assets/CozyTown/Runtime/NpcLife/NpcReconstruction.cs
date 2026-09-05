namespace CozyTown.Runtime.NpcLife
{
    public sealed class NpcReconstruction
    {
        internal NpcReconstruction(
            string locationId, NpcScheduleTarget target, bool isHome, bool hasArrived)
        {
            LocationId = locationId;
            Target = target;
            IsHome = isHome;
            HasArrived = hasArrived;
        }

        public string LocationId { get; }

        public NpcScheduleTarget Target { get; }

        public bool IsHome { get; }

        public bool HasArrived { get; }
    }
}
