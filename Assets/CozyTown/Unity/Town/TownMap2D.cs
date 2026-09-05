using System;
using System.Collections.Generic;
using UnityEngine;

namespace CozyTown.Unity.Town
{
    [DisallowMultipleComponent]
    public sealed class TownMap2D : MonoBehaviour
    {
        [SerializeField] private TownHome[] homes = Array.Empty<TownHome>();
        [SerializeField] private TownLocation[] locations = Array.Empty<TownLocation>();
        [SerializeField] private TownRoad[] roads = Array.Empty<TownRoad>();

        public IReadOnlyList<TownHome> Homes => Array.AsReadOnly(homes);

        public void Configure(TownHome[] townHomes, TownLocation[] townLocations, TownRoad[] townRoads)
        {
            if (townHomes == null) throw new ArgumentNullException(nameof(townHomes));
            if (townLocations == null) throw new ArgumentNullException(nameof(townLocations));
            if (townRoads == null) throw new ArgumentNullException(nameof(townRoads));

            var locationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var location in townLocations)
            {
                if (location == null || string.IsNullOrWhiteSpace(location.Id)
                    || !locationIds.Add(location.Id)
                    || float.IsNaN(location.Position.x) || float.IsInfinity(location.Position.x)
                    || float.IsNaN(location.Position.y) || float.IsInfinity(location.Position.y))
                {
                    throw new ArgumentException("Town locations require unique IDs and finite positions.", nameof(townLocations));
                }
            }

            var homeIds = new HashSet<string>(StringComparer.Ordinal);
            var npcIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var home in townHomes)
            {
                if (home == null || string.IsNullOrWhiteSpace(home.HomeId)
                    || string.IsNullOrWhiteSpace(home.NpcId)
                    || !homeIds.Add(home.HomeId) || !npcIds.Add(home.NpcId)
                    || !locationIds.Contains(home.DoorstepLocationId)
                    || !locationIds.Contains(home.EntryLocationId)
                    || home.DoorstepLocationId == home.EntryLocationId)
                {
                    throw new ArgumentException("Each NPC home requires a unique owner and distinct known door locations.", nameof(townHomes));
                }
            }

            foreach (var road in townRoads)
            {
                if (road == null || !locationIds.Contains(road.FromLocationId)
                    || !locationIds.Contains(road.ToLocationId)
                    || road.FromLocationId == road.ToLocationId)
                {
                    throw new ArgumentException("Town roads must connect two distinct known locations.", nameof(townRoads));
                }
            }

            homes = (TownHome[])townHomes.Clone();
            locations = (TownLocation[])townLocations.Clone();
            roads = (TownRoad[])townRoads.Clone();
        }

        public bool TryGetHome(string npcId, out TownHome home)
        {
            home = Array.Find(homes, candidate => string.Equals(candidate.NpcId, npcId, StringComparison.Ordinal));
            return home != null;
        }

        public bool TryGetLocation(string locationId, out Vector2 position)
        {
            var location = Array.Find(locations, candidate => string.Equals(candidate.Id, locationId, StringComparison.Ordinal));
            position = location != null ? location.Position : default;
            return location != null;
        }

        public bool TryFindRoute(string fromLocationId, string toLocationId,
            out IReadOnlyList<Vector2> waypoints, Func<Vector2, Vector2, bool> canTraverse = null)
        {
            waypoints = Array.Empty<Vector2>();
            if (!TryGetLocation(fromLocationId, out var fromPosition)
                || (canTraverse != null && !canTraverse(fromPosition, fromPosition)))
            {
                return false;
            }

            var distances = new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [fromLocationId] = 0f
            };
            return FindRoute(fromPosition, toLocationId, distances, canTraverse, out waypoints);
        }

        public bool TryFindRoute(Vector2 fromPosition, string toLocationId,
            out IReadOnlyList<Vector2> waypoints, Func<Vector2, Vector2, bool> canTraverse = null)
        {
            var distances = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var location in locations)
            {
                if (location.Position == fromPosition
                    && (canTraverse == null || canTraverse(fromPosition, location.Position)))
                {
                    distances[location.Id] = 0f;
                }
            }

            if (distances.Count == 0)
            {
                foreach (var road in roads)
                {
                    TryGetLocation(road.FromLocationId, out var from);
                    TryGetLocation(road.ToLocationId, out var to);
                    var delta = to - from;
                    if (delta.sqrMagnitude == 0f)
                    {
                        continue;
                    }

                    var progress = Vector2.Dot(fromPosition - from, delta) / delta.sqrMagnitude;
                    if (progress < 0f || progress > 1f
                        || Vector2.SqrMagnitude(from + progress * delta - fromPosition) > 0.00000001f)
                    {
                        continue;
                    }

                    if (canTraverse == null || canTraverse(fromPosition, from))
                    {
                        distances[road.FromLocationId] = Vector2.Distance(fromPosition, from);
                    }
                    if (canTraverse == null || canTraverse(fromPosition, to))
                    {
                        distances[road.ToLocationId] = Vector2.Distance(fromPosition, to);
                    }
                }
            }

            return FindRoute(fromPosition, toLocationId, distances, canTraverse, out waypoints);
        }

        private bool FindRoute(Vector2 fromPosition, string toLocationId,
            Dictionary<string, float> distances, Func<Vector2, Vector2, bool> canTraverse,
            out IReadOnlyList<Vector2> waypoints)
        {
            waypoints = Array.Empty<Vector2>();
            if (!TryGetLocation(toLocationId, out _))
            {
                return false;
            }

            var predecessors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var origin in distances.Keys)
            {
                predecessors[origin] = null;
            }
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                string current = null;
                var shortestDistance = float.PositiveInfinity;
                foreach (var location in locations)
                {
                    if (!visited.Contains(location.Id)
                        && distances.TryGetValue(location.Id, out var distance)
                        && distance < shortestDistance)
                    {
                        current = location.Id;
                        shortestDistance = distance;
                    }
                }

                if (current == null)
                {
                    return false;
                }
                if (current == toLocationId)
                {
                    break;
                }

                visited.Add(current);
                TryGetLocation(current, out var currentPosition);
                foreach (var road in roads)
                {
                    var adjacent = road.FromLocationId == current ? road.ToLocationId
                        : road.ToLocationId == current ? road.FromLocationId : null;
                    if (adjacent == null || visited.Contains(adjacent))
                    {
                        continue;
                    }

                    TryGetLocation(adjacent, out var adjacentPosition);
                    if (canTraverse != null && !canTraverse(currentPosition, adjacentPosition))
                    {
                        continue;
                    }
                    var candidateDistance = shortestDistance + Vector2.Distance(currentPosition, adjacentPosition);
                    if (!distances.TryGetValue(adjacent, out var previousDistance)
                        || candidateDistance < previousDistance)
                    {
                        predecessors[adjacent] = current;
                        distances[adjacent] = candidateDistance;
                    }
                }
            }

            var result = new List<Vector2>();
            for (var current = toLocationId; current != null; current = predecessors[current])
            {
                TryGetLocation(current, out var position);
                result.Add(position);
            }

            result.Reverse();
            var continuousRoute = new List<Vector2> { fromPosition };
            foreach (var position in result)
            {
                if (position != continuousRoute[continuousRoute.Count - 1])
                {
                    continuousRoute.Add(position);
                }
            }
            waypoints = continuousRoute.AsReadOnly();
            return true;
        }
    }

    [Serializable]
    public sealed class TownLocation
    {
        [SerializeField] private string id;
        [SerializeField] private Vector2 position;

        public TownLocation(string id, Vector2 position)
        {
            this.id = id;
            this.position = position;
        }

        public string Id => id;
        public Vector2 Position => position;
    }

    [Serializable]
    public sealed class TownRoad
    {
        [SerializeField] private string fromLocationId;
        [SerializeField] private string toLocationId;

        public TownRoad(string fromLocationId, string toLocationId)
        {
            this.fromLocationId = fromLocationId;
            this.toLocationId = toLocationId;
        }

        public string FromLocationId => fromLocationId;
        public string ToLocationId => toLocationId;
    }

    [Serializable]
    public sealed class TownHome
    {
        [SerializeField] private string homeId;
        [SerializeField] private string npcId;
        [SerializeField] private string doorstepLocationId;
        [SerializeField] private string entryLocationId;

        public TownHome(string homeId, string npcId, string doorstepLocationId, string entryLocationId)
        {
            this.homeId = homeId;
            this.npcId = npcId;
            this.doorstepLocationId = doorstepLocationId;
            this.entryLocationId = entryLocationId;
        }

        public string HomeId => homeId;
        public string NpcId => npcId;
        public string DoorstepLocationId => doorstepLocationId;
        public string EntryLocationId => entryLocationId;
    }
}
