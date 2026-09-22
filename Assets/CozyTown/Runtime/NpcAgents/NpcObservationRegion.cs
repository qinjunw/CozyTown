using System;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcObservationRegion
    {
        public NpcObservationRegion(string id, string name, double minX, double minY, double maxX, double maxY,
            string spaceId = "outdoors")
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(spaceId))
                throw new ArgumentException("Observation regions require an ID, name and space.");
            if (!Finite(minX) || !Finite(minY) || !Finite(maxX) || !Finite(maxY) || minX >= maxX || minY >= maxY)
                throw new ArgumentException("Observation regions require finite increasing bounds.");
            Id = id; Name = name; MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; SpaceId = spaceId;
        }

        public string Id { get; }
        public string Name { get; }
        public string SpaceId { get; }
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public bool Contains(double x, double y, string spaceId)
            => SpaceId == spaceId && x >= MinX && x < MaxX && y >= MinY && y < MaxY;
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
