using System;
using System.Collections.Generic;
using System.Linq;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcLocalObservation
    {
        internal NpcLocalObservation(string observerId, Guid worldRunId, double observedAt, NpcObservationBody body,
            string regionId, IEnumerable<string> nearbyEntityIds, IEnumerable<NpcObservationFact> facts,
            double radius, bool nearbyComplete, string listenerId = null)
        {
            ObserverId = observerId; WorldRunId = worldRunId; ObservedAtTotalMinutes = observedAt;
            X = body.X; Y = body.Y; SpaceId = body.SpaceId; RegionId = regionId;
            NearbyEntityIds = Array.AsReadOnly(nearbyEntityIds.ToArray());
            Facts = Array.AsReadOnly(facts.ToArray());
            Radius = radius; NearbyComplete = nearbyComplete;
            ListenerId = listenerId;
        }
        public string ObserverId { get; }
        public Guid WorldRunId { get; }
        public double ObservedAtTotalMinutes { get; }
        public double X { get; }
        public double Y { get; }
        public string SpaceId { get; }
        public string RegionId { get; }
        public IReadOnlyList<string> NearbyEntityIds { get; }
        public IReadOnlyList<NpcObservationFact> Facts { get; }
        public double Radius { get; }
        public bool NearbyComplete { get; }
        public string CoverageDomain => "registered_entities_same_region_within_radius";
        public string ListenerId { get; }

        internal bool HasSameFacts(NpcLocalObservation current, bool allowPositionChange = false)
        {
            if (current == null || ObserverId != current.ObserverId || WorldRunId != current.WorldRunId
                || ((!allowPositionChange || RegionId == null) && (X != current.X || Y != current.Y))
                || SpaceId != current.SpaceId || RegionId != current.RegionId
                || Radius != current.Radius || NearbyComplete != current.NearbyComplete || ListenerId != current.ListenerId
                || !NearbyEntityIds.SequenceEqual(current.NearbyEntityIds) || Facts.Count != current.Facts.Count) return false;
            for (int i = 0; i < Facts.Count; i++)
            {
                var a = Facts[i]; var b = current.Facts[i];
                if (a.FactId != b.FactId || a.EntityId != b.EntityId || a.Predicate != b.Predicate || a.Value != b.Value
                    || a.ValueType != b.ValueType || a.Unit != b.Unit || a.Knowledge != b.Knowledge || a.Source != b.Source
                    || a.ObserverId != b.ObserverId || a.Scope != b.Scope || a.SpeakerId != b.SpeakerId || a.CanExpress != b.CanExpress
                    || ((a.Knowledge == "statement" || a.Knowledge == "receipt") && a.ObservedAtTotalMinutes != b.ObservedAtTotalMinutes)) return false;
            }
            return true;
        }
    }
}
