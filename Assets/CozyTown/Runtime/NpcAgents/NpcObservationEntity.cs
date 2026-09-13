using System;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcObservationEntity
    {
        public NpcObservationEntity(string id, string name, double x, double y, string kind, string spaceId = "outdoors",
            string interactionId = "none", string resourceItemId = null)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kind)
                || string.IsNullOrWhiteSpace(spaceId) || !NpcObservationRegion.Finite(x) || !NpcObservationRegion.Finite(y))
                throw new ArgumentException("Observation entities require identity, semantics and a finite position.");
            Id = id; Name = name; X = x; Y = y; Kind = kind; SpaceId = spaceId;
            InteractionId = interactionId; ResourceItemId = resourceItemId;
        }
        public string Id { get; }
        public string Name { get; }
        public double X { get; }
        public double Y { get; }
        public string Kind { get; }
        public string SpaceId { get; }
        public string InteractionId { get; }
        public string ResourceItemId { get; }
    }
}
