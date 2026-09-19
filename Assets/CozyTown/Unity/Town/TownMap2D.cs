using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

        public string CaptureConfiguration()
        {
            var result = new StringBuilder();
            AppendConfiguration(result, "town-map-route-v1", Physics2D.defaultContactOffset, homes.Length);
            foreach (var home in homes)
                AppendConfiguration(result, home.HomeId, home.NpcId, home.DoorstepLocationId, home.EntryLocationId);
            AppendConfiguration(result, locations.Length);
            foreach (var location in locations)
                AppendConfiguration(result, location.Id, location.Position.x, location.Position.y);
            AppendConfiguration(result, roads.Length);
            foreach (var road in roads) AppendConfiguration(result, road.FromLocationId, road.ToLocationId);
            foreach (var collider in GetComponentsInChildren<Collider2D>(true))
            {
                if (collider.isTrigger || (collider.attachedRigidbody != null
                    && collider.attachedRigidbody.bodyType != RigidbodyType2D.Static)) continue;
                AppendConfiguration(result, collider.GetType().FullName, collider.enabled,
                    collider.gameObject.activeInHierarchy, collider.offset.x, collider.offset.y);
                var matrix = collider.transform.localToWorldMatrix;
                for (int i = 0; i < 16; i++) AppendConfiguration(result, matrix[i]);
                switch (collider)
                {
                    case BoxCollider2D box:
                        AppendConfiguration(result, box.size.x, box.size.y, box.edgeRadius);
                        break;
                    case CircleCollider2D circle:
                        AppendConfiguration(result, circle.radius);
                        break;
                    case CapsuleCollider2D capsule:
                        AppendConfiguration(result, capsule.size.x, capsule.size.y, (int)capsule.direction);
                        break;
                    case PolygonCollider2D polygon:
                        AppendConfiguration(result, polygon.pathCount);
                        for (int i = 0; i < polygon.pathCount; i++) AppendPoints(polygon.GetPath(i));
                        break;
                    case EdgeCollider2D edge:
                        AppendConfiguration(result, edge.edgeRadius);
                        AppendPoints(edge.points);
                        break;
                    default:
                        throw new InvalidOperationException("Snapshot configuration does not support this world collider: "
                            + collider.GetType().Name);
                }
            }
            return result.ToString();

            void AppendPoints(Vector2[] points)
            {
                AppendConfiguration(result, points.Length);
                foreach (var point in points) AppendConfiguration(result, point.x, point.y);
            }
        }

        internal static void AppendConfiguration(StringBuilder result, params object[] values)
        {
            foreach (object value in values)
            {
                string text = value is float single ? single.ToString("R", CultureInfo.InvariantCulture)
                    : value is double number ? number.ToString("R", CultureInfo.InvariantCulture)
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                result.Append(text.Length).Append(':').Append(text);
            }
        }

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

        internal bool ContainsRoadSegment(Vector2 from, Vector2 to)
        {
            foreach (var road in roads)
            {
                TryGetLocation(road.FromLocationId, out var start);
                TryGetLocation(road.ToLocationId, out var end);
                if (IsOnSegment(from, start, end) && IsOnSegment(to, start, end)) return true;
            }
            return false;
        }

        internal static bool IsOnSegment(Vector2 point, Vector2 from, Vector2 to)
        {
            var segment = to - from;
            if (segment.sqrMagnitude == 0) return Vector2.SqrMagnitude(point - from) <= 0.00000001f;
            float progress = Vector2.Dot(point - from, segment) / segment.sqrMagnitude;
            return progress >= -0.00001f && progress <= 1.00001f
                && Vector2.SqrMagnitude(from + Mathf.Clamp01(progress) * segment - point) <= 0.00000001f;
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
