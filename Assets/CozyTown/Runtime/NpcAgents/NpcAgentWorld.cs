using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcAgentWorld
    {
        public const int MaximumActivityDurationGameMinutes = 1440;
        private const int MaxPendingEvents = 16;
        private readonly Dictionary<string, Resident> _residents = new Dictionary<string, Resident>(StringComparer.Ordinal);
        private readonly Func<string, string, bool> _canVisit;
        private WorldTimeProgress _current;
        private bool _hasTime;
        private Guid _worldRunId;

        private sealed class Resident
        {
            internal NpcDailySchedule Schedule;
            internal long Revision;
            internal NpcActivityRequest Activity;
            internal double ActivityStart;
            internal readonly List<NpcAgentEvent> Events = new List<NpcAgentEvent>();
        }

        public NpcAgentWorld(IEnumerable<NpcDailySchedule> schedules, Func<string, string, bool> canVisit = null)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            _canVisit = canVisit;
            foreach (var schedule in schedules)
            {
                if (schedule == null) throw new ArgumentException("Resident schedules cannot contain null.", nameof(schedules));
                _residents.Add(schedule.NpcId, new Resident { Schedule = schedule });
            }
        }

        public double TotalMinutes => _current.TotalMinutes;

        public void Observe(WorldTimeProgress progress)
        {
            if (progress.Clock.Day < 1 || progress.Clock.MinuteOfDay < 0 || progress.Clock.MinuteOfDay >= 1440
                || double.IsNaN(progress.FractionalMinute) || progress.FractionalMinute < 0 || progress.FractionalMinute >= 1)
                throw new ArgumentOutOfRangeException(nameof(progress), "World time requires a valid day, minute and fraction.");
            bool rebuild = !_hasTime || progress.RebuildVersion != _current.RebuildVersion;
            if (!rebuild && progress.TotalMinutes < _current.TotalMinutes)
                throw new ArgumentException("Time can move backwards only when the world is rebuilt.", nameof(progress));
            if (rebuild) _worldRunId = Guid.NewGuid();
            foreach (var resident in _residents.Values)
            {
                if (rebuild)
                {
                    resident.Events.Clear();
                    resident.Revision = 1;
                    resident.Activity = null;
                    Queue(resident, NpcAgentEventKind.WorldRebuilt, progress.TotalMinutes);
                    continue;
                }

                var before = resident.Schedule.Query(_current.Clock.MinuteOfDay);
                var after = resident.Schedule.Query(progress.Clock.MinuteOfDay);
                bool changed = before.TargetLocationId != after.TargetLocationId
                    || before.ExpectedActivity != after.ExpectedActivity;
                if (changed) Queue(resident, NpcAgentEventKind.ScheduleChanged, progress.TotalMinutes);
                if (progress.Clock.Day != _current.Clock.Day)
                {
                    changed = true;
                    Queue(resident, NpcAgentEventKind.DayChanged, progress.TotalMinutes);
                }
                if (resident.Activity != null && progress.TotalMinutes >= resident.Activity.ExpiresAtTotalMinutes)
                {
                    changed = true;
                    Queue(resident, NpcAgentEventKind.ActivityExpired, resident.Activity.ExpiresAtTotalMinutes);
                    resident.Activity = null;
                }
                if (changed) resident.Revision++;
            }
            _current = progress;
            _hasTime = true;
        }

        public NpcAgentSnapshot GetState(string npcId)
        {
            var resident = RequireResident(npcId);
            return new NpcAgentSnapshot(npcId, _worldRunId, resident.Revision,
                TargetAt(resident, TotalMinutes), resident.Activity);
        }

        public IReadOnlyList<NpcAgentEvent> TakeEvents(string npcId)
        {
            var resident = RequireResident(npcId);
            var events = Array.AsReadOnly(resident.Events.ToArray());
            resident.Events.Clear();
            return events;
        }

        public IReadOnlyList<string> GetKnownLocationIds(string npcId)
        {
            var schedule = RequireResident(npcId).Schedule;
            return Array.AsReadOnly(new[] { schedule.HomeOutsideLocationId, schedule.HomeEntranceLocationId,
                schedule.MorningWorkLocationId, schedule.RestLocationId, schedule.AfternoonWorkLocationId }.Distinct().ToArray());
        }

        public OperationResult<NpcLocationDetails> InspectLocation(string npcId, string locationId)
        {
            var resident = RequireResident(npcId);
            if (!IsScheduledLocation(resident.Schedule, locationId))
                return OperationResult<NpcLocationDetails>.Failure("agent.location_unknown");
            return OperationResult<NpcLocationDetails>.Success(new NpcLocationDetails(locationId,
                _canVisit == null || _canVisit(npcId, locationId)));
        }

        public OperationResult SubmitActivity(NpcActivityRequest request)
        {
            if (request == null) return OperationResult.Failure("agent.request_invalid");
            var validation = ValidateCommand(request.NpcId, request.WorldRunId, request.ExpectedRevision, out var resident);
            if (!validation.IsSuccess) return validation;
            if (resident.Activity != null) return OperationResult.Failure("agent.busy");
            if (request.Activity != NpcActivity.Working && request.Activity != NpcActivity.Resting)
                return OperationResult.Failure("agent.activity_invalid");
            double duration = request.ExpiresAtTotalMinutes - TotalMinutes;
            if (double.IsNaN(duration) || duration <= 0 || duration > MaximumActivityDurationGameMinutes)
                return OperationResult.Failure("agent.deadline_invalid");
            if (string.IsNullOrWhiteSpace(request.TargetLocationId)
                || !(_canVisit != null
                    ? _canVisit(request.NpcId, request.TargetLocationId)
                    : IsScheduledLocation(resident.Schedule, request.TargetLocationId)))
                return OperationResult.Failure("agent.target_unavailable");

            resident.Activity = request;
            resident.ActivityStart = TotalMinutes;
            resident.Revision++;
            Queue(resident, NpcAgentEventKind.ActivityAccepted, TotalMinutes);
            return OperationResult.Success();
        }

        public OperationResult CancelActivity(string npcId, Guid worldRunId, long expectedRevision)
        {
            var validation = ValidateCommand(npcId, worldRunId, expectedRevision, out var resident);
            if (!validation.IsSuccess) return validation;
            if (resident.Activity == null) return OperationResult.Failure("agent.no_activity");
            resident.Activity = null;
            resident.Revision++;
            Queue(resident, NpcAgentEventKind.ActivityCancelled, TotalMinutes);
            return OperationResult.Success();
        }

        public NpcScheduleTarget GetTargetAt(string npcId, double totalMinutes)
        {
            RequireTime(totalMinutes);
            return TargetAt(RequireResident(npcId), totalMinutes);
        }

        public double NextBoundaryAfter(string npcId, double totalMinutes)
        {
            RequireTime(totalMinutes);
            var resident = RequireResident(npcId);
            double next = Math.Floor(totalMinutes)
                + resident.Schedule.MinutesUntilNextBoundary((int)(Math.Floor(totalMinutes) % 1440));
            if (resident.Activity != null)
            {
                if (resident.ActivityStart > totalMinutes) next = Math.Min(next, resident.ActivityStart);
                if (resident.Activity.ExpiresAtTotalMinutes > totalMinutes)
                    next = Math.Min(next, resident.Activity.ExpiresAtTotalMinutes);
            }
            return next;
        }

        private OperationResult ValidateCommand(string npcId, Guid worldRunId, long revision, out Resident resident)
        {
            resident = null;
            if (!_hasTime) return OperationResult.Failure("agent.world_unbound");
            if (!_residents.TryGetValue(npcId ?? string.Empty, out resident))
                return OperationResult.Failure("agent.npc_unknown");
            if (worldRunId != _worldRunId) return OperationResult.Failure("agent.world_stale");
            return revision == resident.Revision
                ? OperationResult.Success()
                : OperationResult.Failure("agent.decision_stale");
        }

        private static NpcScheduleTarget TargetAt(Resident resident, double totalMinutes)
        {
            var activity = resident.Activity;
            return activity != null && totalMinutes >= resident.ActivityStart && totalMinutes < activity.ExpiresAtTotalMinutes
                ? new NpcScheduleTarget(activity.TargetLocationId, activity.Activity)
                : resident.Schedule.Query((int)(Math.Floor(totalMinutes) % 1440));
        }

        private static bool IsScheduledLocation(NpcDailySchedule schedule, string locationId)
        {
            return locationId == schedule.HomeOutsideLocationId || locationId == schedule.HomeEntranceLocationId
                || locationId == schedule.MorningWorkLocationId || locationId == schedule.RestLocationId
                || locationId == schedule.AfternoonWorkLocationId;
        }

        private static void RequireTime(double totalMinutes)
        {
            if (double.IsNaN(totalMinutes) || double.IsInfinity(totalMinutes) || totalMinutes < 0)
                throw new ArgumentOutOfRangeException(nameof(totalMinutes));
        }

        private Resident RequireResident(string npcId)
        {
            if (!_hasTime) throw new InvalidOperationException("Observe world time before querying residents.");
            if (!_residents.TryGetValue(npcId ?? string.Empty, out var resident))
                throw new ArgumentException("The NPC is not registered in this world.", nameof(npcId));
            return resident;
        }

        private static void Queue(Resident resident, NpcAgentEventKind kind, double totalMinutes)
        {
            // These are decision notifications; retain the latest occurrence of each kind, not a history log.
            resident.Events.RemoveAll(item => item.Kind == kind);
            if (resident.Events.Count == MaxPendingEvents) resident.Events.RemoveAt(0);
            resident.Events.Add(new NpcAgentEvent(kind, totalMinutes));
        }
    }
}
