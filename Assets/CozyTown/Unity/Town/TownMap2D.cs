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
            out IReadOnlyList<Vector2> waypoints)
        {
            waypoints = Array.Empty<Vector2>();
            if (!TryGetLocation(fromLocationId, out _) || !TryGetLocation(toLocationId, out _))
            {
                return false;
            }

            var predecessors = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [fromLocationId] = null
            };
            var frontier = new Queue<string>();
            frontier.Enqueue(fromLocationId);
            while (frontier.Count > 0 && !predecessors.ContainsKey(toLocationId))
            {
                var current = frontier.Dequeue();
                foreach (var road in roads)
                {
                    var adjacent = road.FromLocationId == current ? road.ToLocationId
                        : road.ToLocationId == current ? road.FromLocationId : null;
                    if (adjacent == null || predecessors.ContainsKey(adjacent))
                    {
                        continue;
                    }

                    predecessors.Add(adjacent, current);
                    frontier.Enqueue(adjacent);
                }
            }

            if (!predecessors.ContainsKey(toLocationId))
            {
                return false;
            }

            var result = new List<Vector2>();
            for (var current = toLocationId; current != null; current = predecessors[current])
            {
                TryGetLocation(current, out var position);
                result.Add(position);
            }

            result.Reverse();
            waypoints = result.AsReadOnly();
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
