using System;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
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
        [SerializeField] private Vector2 morningWorkFacing;
        [SerializeField] private Vector2 afternoonWorkFacing;
        [SerializeField] private int[] times = new int[6];
        [SerializeField] private float unitsPerSecond = 2f;
        [SerializeField] private SpriteRenderer visual;
        private NpcDailySchedule _schedule;
        private Journey _journey;
        private bool _warnedNoLegalPosition;

        public string NpcId => npcId;
        public Vector2 Position => transform.position;
        public string TargetLocationId { get; private set; }
        public bool IsHome { get; private set; }
        public TownRouteStatus Status { get; private set; }
        public Vector2 FacingDirection => _journey != null
            && _journey.Activity == NpcActivity.Working
            && _journey.Follower.Status == TownRouteStatus.Arrived
            && _journey.WorkFacing != Vector2.zero
                ? _journey.WorkFacing
                : _journey?.Follower.FacingDirection ?? Vector2.down;

        internal sealed class Journey
        {
            internal TownRouteFollower2D Follower;
            internal NpcActivity Activity;
            internal Vector2 WorkFacing;
            internal double AnimationSeconds;
            internal bool IsWalking;
            internal bool IsRebuild;
            internal bool NoLegalPosition;
        }

        public void Configure(TownMap2D townMap, NpcDailySchedule schedule,
            SpriteRenderer renderer, float speed = 2f,
            Vector2 morningFacing = default(Vector2), Vector2 afternoonFacing = default(Vector2))
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
            morningWorkFacing = morningFacing;
            afternoonWorkFacing = afternoonFacing;
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
            if (!follower.HasClearFooting)
            {
                foreach (string fallbackId in new[] { outsideId, entryId, morningId, restId, afternoonId })
                {
                    map.TryGetLocation(fallbackId, out Vector2 fallbackPosition);
                    var fallback = new TownRouteFollower2D(map, fallbackPosition, 0.3f, Vector2.zero, map.transform);
                    if (!fallback.HasClearFooting) continue;
                    follower = fallback;
                    break;
                }
            }
            bool noLegalPosition = !follower.HasClearFooting;
            if (noLegalPosition)
                follower = new TownRouteFollower2D(map, Position, 0.3f, Vector2.zero, map.transform);
            follower.SetDestination(state.Target.TargetLocationId);
            if (noLegalPosition) follower.Block();
            return new Journey { Follower = follower, Activity = state.Target.ExpectedActivity,
                WorkFacing = WorkFacingAt(minuteOfDay),
                IsRebuild = true, NoLegalPosition = noLegalPosition };
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
            var candidate = new Journey { Follower = _journey.Follower.Clone(), Activity = _journey.Activity,
                NoLegalPosition = _journey.NoLegalPosition };
            if (candidate.NoLegalPosition)
            {
                SetTarget(candidate, (int)(Math.Floor(toMinute) % 1440));
                candidate.Follower.Block();
                return candidate;
            }
            double cursor = fromMinute;
            while (cursor < toMinute)
            {
                int minuteOfDay = (int)(Math.Floor(cursor) % 1440);
                SetTarget(candidate, minuteOfDay);
                double nextBoundary = Math.Floor(cursor) + _schedule.MinutesUntilNextBoundary(minuteOfDay);
                double end = Math.Min(toMinute, nextBoundary);
                double acceptedSeconds = (end - cursor) * WorldTimeProgress.EffectiveSecondsPerGameMinute;
                candidate.Follower.Advance((float)(acceptedSeconds * unitsPerSecond));
                candidate.AnimationSeconds = acceptedSeconds;
                candidate.IsWalking = candidate.Follower.Status == TownRouteStatus.Travelling;
                cursor = end;
            }
            SetTarget(candidate, (int)(Math.Floor(toMinute) % 1440));
            return candidate;
        }

        private void SetTarget(Journey journey, int minuteOfDay)
        {
            var target = _schedule.Query(minuteOfDay);
            journey.Activity = target.ExpectedActivity;
            journey.WorkFacing = WorkFacingAt(minuteOfDay);
            journey.Follower.SetDestination(target.TargetLocationId);
        }

        private Vector2 WorkFacingAt(int minuteOfDay)
        {
            int elapsed = (minuteOfDay - _schedule.AfternoonStartMinute + 1440) % 1440;
            int duration = (_schedule.ReturnStartMinute - _schedule.AfternoonStartMinute + 1440) % 1440;
            return elapsed < duration ? afternoonWorkFacing : morningWorkFacing;
        }

        internal void Commit(Journey candidate)
        {
            _journey = candidate;
            transform.position = new Vector3(candidate.Follower.Position.x, candidate.Follower.Position.y, transform.position.z);
            TargetLocationId = candidate.Follower.TargetLocationId;
            Status = candidate.Follower.Status;
            IsHome = !candidate.NoLegalPosition
                && candidate.Activity == NpcActivity.Home && Status == TownRouteStatus.Arrived;
            // Only the committed final segment drives visible animation; preparation stays off-screen.
            GetComponent<CozyTownNpcSpriteAnimator>()?.Apply(FacingDirection,
                !candidate.NoLegalPosition && candidate.IsWalking && Status == TownRouteStatus.Travelling,
                candidate.AnimationSeconds, candidate.IsRebuild);
            SetPresence(isActiveAndEnabled && !IsHome);
            if (candidate.NoLegalPosition && !_warnedNoLegalPosition)
                Debug.LogWarning($"Resident '{npcId}' has no clear reconstruction location; "
                    + "body and interaction are hidden until a later rebuild finds one.", this);
            _warnedNoLegalPosition = candidate.NoLegalPosition;
        }

        internal void CancelInteraction() => GetComponent<CozyTownNpcDebugPresenter>()?.CancelInteraction();

        private void OnEnable()
        {
            if (_journey != null) SetPresence(!IsHome);
        }

        private void OnDisable() => SetPresence(false);

        private void SetPresence(bool present)
        {
            present = present && (_journey == null || !_journey.NoLegalPosition);
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
