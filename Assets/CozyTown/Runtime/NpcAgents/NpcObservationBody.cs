using System;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcObservationBody
    {
        public NpcObservationBody(string npcId, double x, double y, string spaceId = "outdoors", bool observable = true)
        {
            if (string.IsNullOrWhiteSpace(npcId) || string.IsNullOrWhiteSpace(spaceId)
                || !NpcObservationRegion.Finite(x) || !NpcObservationRegion.Finite(y))
                throw new ArgumentException("An observation body requires an ID, space and finite position.");
            NpcId = npcId; X = x; Y = y; SpaceId = spaceId; Observable = observable;
        }
        public string NpcId { get; }
        public double X { get; }
        public double Y { get; }
        public string SpaceId { get; }
        public bool Observable { get; }
    }
}
