using System;
using System.Collections.Generic;
using UnityEngine;

namespace CozyTown.Unity.Town
{
    public enum TownRouteStatus
    {
        Travelling,
        Arrived,
        Blocked
    }

    public sealed class TownRouteFollower2D
    {
        private const float CollisionClearance = 0.001f;
        // World-unit tolerance for a collision-checked tail after consuming the requested budget.
        private const float ArrivalTolerance = 0.00001f;

        private readonly TownMap2D _map;
        private readonly float _radius;
        private readonly Vector2 _footOffset;
        private readonly Transform _worldRoot;
        private IReadOnlyList<Vector2> _waypoints = Array.Empty<Vector2>();
        private int _waypointIndex;
        private bool _hasReplanned;

        public TownRouteFollower2D(TownMap2D map, Vector2 start, float radius,
            Vector2 footOffset, Transform worldRoot)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _worldRoot = worldRoot ?? throw new ArgumentNullException(nameof(worldRoot));
            if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
            if (!IsFinite(start) || !IsFinite(footOffset))
            {
                throw new ArgumentException("Route positions and foot offsets must be finite.");
            }
            _radius = radius;
            _footOffset = footOffset;
            Position = start;
        }

        public Vector2 Position { get; private set; }
        public string TargetLocationId { get; private set; } = string.Empty;
        public TownRouteStatus Status { get; private set; } = TownRouteStatus.Arrived;
        public Vector2 FacingDirection { get; private set; } = Vector2.down;

        internal bool HasClearFooting
        {
            get
            {
                Physics2D.SyncTransforms();
                return IsFootClear(Position);
            }
        }

        public void SetDestination(string locationId)
        {
            if (!_map.TryGetLocation(locationId, out _))
            {
                throw new ArgumentException("Destination must identify a town location.", nameof(locationId));
            }
            if (string.Equals(TargetLocationId, locationId, StringComparison.Ordinal))
            {
                return;
            }

            TargetLocationId = locationId;
            _hasReplanned = false;
            PlanRoute();
        }

        public void Advance(float distance)
        {
            if (distance < 0f || float.IsNaN(distance) || float.IsInfinity(distance))
            {
                throw new ArgumentOutOfRangeException(nameof(distance));
            }

            Physics2D.SyncTransforms();
            while (distance > 0f && Status == TownRouteStatus.Travelling)
            {
                var delta = _waypoints[_waypointIndex] - Position;
                var remaining = delta.magnitude;
                if (remaining == 0f)
                {
                    CompleteWaypoint();
                    continue;
                }
                FacingDirection = delta / remaining;
                var requested = Mathf.Min(distance, remaining);
                var accepted = GetClearDistance(Position, FacingDirection, requested);
                Position += FacingDirection * accepted;
                distance -= accepted;
                if (accepted < requested)
                {
                    if (_hasReplanned)
                    {
                        Status = TownRouteStatus.Blocked;
                        return;
                    }

                    _hasReplanned = true;
                    PlanRoute();
                    continue;
                }
                if (requested < remaining
                    && (Vector2.Distance(Position, _waypoints[_waypointIndex]) > ArrivalTolerance
                        || !CanTraverse(Position, _waypoints[_waypointIndex])))
                {
                    break;
                }

                CompleteWaypoint();
            }
        }

        private void CompleteWaypoint()
        {
            Position = _waypoints[_waypointIndex];
            _waypointIndex++;
            if (_waypointIndex == _waypoints.Count)
            {
                Status = TownRouteStatus.Arrived;
            }
        }

        public void Retry()
        {
            if (!string.IsNullOrEmpty(TargetLocationId))
            {
                _hasReplanned = false;
                PlanRoute();
            }
        }

        public TownRouteFollower2D Clone() => (TownRouteFollower2D)MemberwiseClone();

        internal void Block() => Status = TownRouteStatus.Blocked;

        private void PlanRoute()
        {
            Physics2D.SyncTransforms();
            _waypointIndex = 1;
            if (!_map.TryFindRoute(Position, TargetLocationId, out _waypoints, CanTraverse))
            {
                Status = TownRouteStatus.Blocked;
                return;
            }

            Status = _waypoints.Count <= 1 ? TownRouteStatus.Arrived : TownRouteStatus.Travelling;
        }

        private bool CanTraverse(Vector2 from, Vector2 to)
        {
            var delta = to - from;
            var distance = delta.magnitude;
            return IsFootClear(from) && IsFootClear(to)
                && GetClearDistance(from, distance > 0f ? delta / distance : Vector2.zero, distance) >= distance;
        }

        private float GetClearDistance(Vector2 from, Vector2 direction, float distance)
        {
            if (!IsFootClear(from))
            {
                return 0f;
            }

            var clearDistance = distance;
            if (distance == 0f)
            {
                return clearDistance;
            }
            // Reserve both shapes' contact margins so overlap queries also accept the stop.
            var clearance = Mathf.Max(CollisionClearance, 2f * Physics2D.defaultContactOffset);
            foreach (var hit in Physics2D.CircleCastAll(
                from + _footOffset, _radius, direction, distance + clearance, Physics2D.AllLayers))
            {
                if (IsWorldObstacle(hit.collider))
                {
                    clearDistance = Mathf.Min(clearDistance,
                        Mathf.Max(0f, hit.distance - clearance));
                }
            }

            return clearDistance;
        }

        private bool IsFootClear(Vector2 position)
        {
            foreach (var collider in Physics2D.OverlapCircleAll(
                position + _footOffset, _radius, Physics2D.AllLayers))
            {
                if (IsWorldObstacle(collider))
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsWorldObstacle(Collider2D collider)
        {
            return collider != null && !collider.isTrigger
                && collider.transform.IsChildOf(_worldRoot)
                && (collider.attachedRigidbody == null
                    || collider.attachedRigidbody.bodyType == RigidbodyType2D.Static);
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }
    }
}
