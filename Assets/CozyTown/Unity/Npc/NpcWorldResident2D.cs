using System;
using CozyTown.Runtime.NpcLife;
using CozyTown.Unity.Town;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    [DisallowMultipleComponent]
    public sealed class NpcWorldResident2D : MonoBehaviour
    {
        [SerializeField] private TownMap2D map;
        [SerializeField] private string npcId;
        [SerializeField] private string homeId;
        [SerializeField] private string outsideId;
        [SerializeField] private string entryId;
        [SerializeField] private string morningId;
        [SerializeField] private string restId;
        [SerializeField] private string afternoonId;
        [SerializeField] private int[] times = new int[6];
        [SerializeField] private float unitsPerSecond = 2f;
        [SerializeField] private SpriteRenderer visual;
        private NpcDailySchedule _schedule;
        private Journey _journey;

        public string NpcId => npcId;
        public Vector2 Position => transform.position;
        public string TargetLocationId { get; private set; }
        public bool IsHome { get; private set; }
        public TownRouteStatus Status { get; private set; }
        public Vector2 FacingDirection => _journey?.Follower.FacingDirection ?? Vector2.down;

        internal sealed class Journey
        {
            internal TownRouteFollower2D Follower;
            internal NpcActivity Activity;
        }

        public void Configure(TownMap2D townMap, NpcDailySchedule schedule,
            SpriteRenderer renderer, float speed = 2f)
        {
            map = townMap ?? throw new ArgumentNullException(nameof(townMap));
            visual = renderer ?? throw new ArgumentNullException(nameof(renderer));
            npcId = schedule.NpcId;
            homeId = schedule.HomeId;
            outsideId = schedule.HomeOutsideLocationId;
            entryId = schedule.HomeEntranceLocationId;
            morningId = schedule.MorningWorkLocationId;
            restId = schedule.RestLocationId;
            afternoonId = schedule.AfternoonWorkLocationId;
            times = new[] { schedule.DepartureMinute, schedule.MorningArrivalDeadlineMinute,
                schedule.RestStartMinute, schedule.AfternoonStartMinute,
                schedule.ReturnStartMinute, schedule.HomeArrivalDeadlineMinute };
            unitsPerSecond = speed;
            _schedule = null;
        }

        internal Journey Reconstruct(int minuteOfDay)
        {
            if (_schedule == null) ValidateConfiguration();
            var state = _schedule.Rebuild(minuteOfDay);
            map.TryGetLocation(state.LocationId, out Vector2 position);
            var follower = new TownRouteFollower2D(map, position, 0.3f, Vector2.zero, map.transform);
            follower.SetDestination(state.Target.TargetLocationId);
            return new Journey { Follower = follower, Activity = state.Target.ExpectedActivity };
        }

        internal void ValidateConfiguration()
        {
            if (map == null || visual == null || times == null || times.Length != 6)
                throw new InvalidOperationException("Resident requires a map, visual, and six daily phase boundaries.");
            _schedule = new NpcDailySchedule(npcId, homeId, outsideId, entryId,
                morningId, restId, afternoonId, times[0], times[1], times[2],
                times[3], times[4], times[5]);
            if (unitsPerSecond <= 0 || float.IsNaN(unitsPerSecond) || float.IsInfinity(unitsPerSecond))
                throw new InvalidOperationException("Resident walking speed must be finite and positive.");
            if (!map.TryGetHome(npcId, out var home) || home.HomeId != homeId
                || home.DoorstepLocationId != outsideId || home.EntryLocationId != entryId)
                throw new InvalidOperationException("Resident home ownership and door locations must match the town map.");
            foreach (string destination in new[] { outsideId, entryId, morningId, restId, afternoonId })
            {
                if (!map.TryFindRoute(outsideId, destination, out _))
                    throw new InvalidOperationException("Every resident destination must be connected to the owned doorstep.");
            }
        }

        internal Journey Advance(double fromMinute, double toMinute)
        {
            var candidate = new Journey { Follower = _journey.Follower.Clone(), Activity = _journey.Activity };
            double cursor = fromMinute;
            while (cursor < toMinute)
            {
                int minuteOfDay = (int)(Math.Floor(cursor) % 1440);
                SetTarget(candidate, minuteOfDay);
                double nextBoundary = Math.Floor(cursor) + _schedule.MinutesUntilNextBoundary(minuteOfDay);
                double end = Math.Min(toMinute, nextBoundary);
                candidate.Follower.Advance((float)((end - cursor) * unitsPerSecond * 0.5));
                cursor = end;
            }
            SetTarget(candidate, (int)(Math.Floor(toMinute) % 1440));
            return candidate;
        }

        private void SetTarget(Journey journey, int minuteOfDay)
        {
            var target = _schedule.Query(minuteOfDay);
            journey.Activity = target.ExpectedActivity;
            journey.Follower.SetDestination(target.TargetLocationId);
        }

        internal void Commit(Journey candidate)
        {
            _journey = candidate;
            transform.position = new Vector3(candidate.Follower.Position.x, candidate.Follower.Position.y, transform.position.z);
            TargetLocationId = candidate.Follower.TargetLocationId;
            Status = candidate.Follower.Status;
            IsHome = candidate.Activity == NpcActivity.Home && Status == TownRouteStatus.Arrived;
            SetPresence(isActiveAndEnabled && !IsHome);
        }

        internal void CancelInteraction() => GetComponent<CozyTownNpcDebugPresenter>()?.CancelInteraction();

        private void OnEnable()
        {
            if (_journey != null) SetPresence(!IsHome);
        }

        private void OnDisable() => SetPresence(false);

        private void SetPresence(bool present)
        {
            if (visual != null) visual.enabled = present;
            foreach (var collider in GetComponents<Collider2D>()) collider.enabled = present;
            var point = GetComponent<TownInteractionPoint2D>();
            if (point != null)
            {
                point.enabled = present;
                if (point.PromptAnchor != transform) point.PromptAnchor.gameObject.SetActive(present);
            }
            var presenter = GetComponent<CozyTownNpcDebugPresenter>();
            if (presenter != null) presenter.enabled = present;
        }
    }
}
