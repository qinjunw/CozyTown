namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcLocationDetails
    {
        internal NpcLocationDetails(string locationId, bool isReachable)
        {
            LocationId = locationId;
            IsReachable = isReachable;
        }

        public string LocationId { get; }
        public bool IsReachable { get; }
    }
}
