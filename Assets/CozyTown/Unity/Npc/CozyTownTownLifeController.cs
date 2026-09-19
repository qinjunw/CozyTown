using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Player;
using CozyTown.Unity.Time;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    [DisallowMultipleComponent]
    public sealed class CozyTownTownLifeController : MonoBehaviour, IWorldSnapshotAdapter
    {
        [SerializeField] private NpcWorldResident2D[] residents = Array.Empty<NpcWorldResident2D>();
        private IWorldTimeFlow _timeFlow;
        private WorldTimeProgress _last;
        private bool _hasState;
        private NpcAgentWorld _agents;
        private NpcDecisionScheduler _decisions;
        private NpcMeetingBoard _meetings;
        private CharacterResourceTrading _resources;
        private NpcMeetingDialogueView _meetingView;
        private TownLocalObservation2D _observation;
        private bool _configuringDecisions;
        private bool _tickingDecisions;
        private WorldSnapshotBinding _snapshotBinding;
        private PlayerMovement2D _snapshotPlayer;
        private PlayerModalInputGate2D _snapshotGate;
        private DaytimeClockDriver _snapshotClockDriver;
        private Func<double> _snapshotRealSeconds;
        private NpcMeetingPlan[] _configuredMeetingPlans;
        private WorldTimeProgress? _preparedPublication;

        public void ConfigureSnapshots(WorldSnapshotBinding binding, PlayerMovement2D player,
            PlayerModalInputGate2D gate, DaytimeClockDriver clockDriver = null, Func<double> realSeconds = null)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (gate == null || gate.gameObject != player.gameObject)
                throw new ArgumentException("Snapshot input gate must belong to the configured player.", nameof(gate));
            if (_configuringDecisions || _tickingDecisions)
                throw new InvalidOperationException("Complete the current decision operation before binding snapshots.");
            player.CaptureInitialSnapshot();
            _snapshotBinding?.Detach(this);
            _snapshotBinding = binding;
            _snapshotPlayer = player;
            _snapshotGate = gate;
            _snapshotClockDriver = clockDriver;
            _snapshotRealSeconds = realSeconds ?? (() => UnityEngine.Time.realtimeSinceStartupAsDouble);
            binding.Attach(this);
        }

        public CompleteWorldSnapshot CaptureSnapshot(WorldTimeProgress progress, string contentConfiguration)
        {
            RequireSnapshotBoundary();
            if (!IsWorldReady || _last.TotalMinutes != progress.TotalMinutes
                || _agents.TotalMinutes != progress.TotalMinutes || _last.RebuildVersion != progress.RebuildVersion)
                throw new InvalidOperationException("Capture requires a completed update at one world time.");
            var bodies = residents.Select(resident => resident.CaptureSnapshot()).ToArray();
            for (int i = 0; i < bodies.Length; i++) residents[i].ValidateSnapshot(bodies[i], _agents, progress.TotalMinutes);
            var player = _snapshotPlayer.CaptureSnapshot();
            PlayerMovement2D.ValidateSnapshot(player);
            return new CompleteWorldSnapshot(contentConfiguration, CaptureBodyConfiguration(), _agents.CaptureSnapshot(),
                _meetings != null, _meetings?.CaptureSnapshot(), _decisions != null,
                _decisions?.CaptureSnapshot(_snapshotRealSeconds()), bodies, player);
        }

        public IPreparedWorldRestore PrepareRestore(GameSaveSnapshot snapshot,
            WorldTimeProgress progress, string contentConfiguration)
        {
            RequireSnapshotBoundary();
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            bool legacy = snapshot.SchemaVersion < GameSaveSnapshot.CurrentSchemaVersion;
            var actors = residents.ToDictionary(resident => resident.NpcId, StringComparer.Ordinal);
            var bodyCandidates = new NpcWorldResident2D.Journey[residents.Length];
            NpcAgentWorld agents;
            NpcMeetingBoard meetings;
            PlayerBodySnapshot player;
            NpcDecisionSchedulerSnapshot decisions = null;
            if (legacy)
            {
                agents = new NpcAgentWorld(residents.Select(resident => resident.Schedule),
                    (npcId, locationId) => actors[npcId].CanVisit(locationId));
                agents.Observe(progress);
                meetings = _configuredMeetingPlans == null ? null
                    : new NpcMeetingBoard(agents, _configuredMeetingPlans, MeetingPresence, _resources);
                _decisions?.ValidateWorldBinding(agents);
                for (int i = 0; i < residents.Length; i++)
                    bodyCandidates[i] = residents[i].Reconstruct(progress.Clock.MinuteOfDay);
                player = _snapshotPlayer.CaptureInitialSnapshot();
            }
            else
            {
                var complete = snapshot.CompleteWorld;
                if (complete == null || complete.ContentConfiguration != contentConfiguration
                    || complete.BodyConfiguration != CaptureBodyConfiguration()
                    || complete.DecisionsEnabled != (_decisions != null) || complete.MeetingsEnabled != (_meetings != null)
                    || complete.DecisionsEnabled != (complete.Decisions != null)
                    || complete.MeetingsEnabled != (complete.Meetings != null)
                    || complete.Residents == null || complete.Residents.Count != residents.Length)
                    throw new ArgumentException("Saved world configuration and enabled systems must match this session.", nameof(snapshot));
                var restored = _agents.PrepareRestore(complete.World, progress,
                    (npcId, locationId) => actors[npcId].IsKnownLocation(locationId));
                if (!restored.IsSuccess) throw new ArgumentException(restored.ErrorCode, nameof(snapshot));
                agents = restored.Value;
                meetings = null;
                if (_meetings != null)
                {
                    var restoredMeetings = _meetings.PrepareRestore(complete.Meetings, agents, _resources);
                    if (!restoredMeetings.IsSuccess) throw new ArgumentException(restoredMeetings.ErrorCode, nameof(snapshot));
                    meetings = restoredMeetings.Value;
                }
                var bodies = complete.Residents.ToDictionary(body => body.NpcId, StringComparer.Ordinal);
                for (int i = 0; i < residents.Length; i++)
                {
                    if (!bodies.TryGetValue(residents[i].NpcId, out var body))
                        throw new ArgumentException("Saved bodies must contain every configured resident.", nameof(snapshot));
                    bodyCandidates[i] = residents[i].PrepareRestore(body, agents, progress.TotalMinutes);
                }
                ValidateMeetingBodies(complete.Meetings, bodies);
                decisions = complete.Decisions;
                _decisions?.ValidateSnapshot(decisions, agents, meetings);
                player = complete.Player;
            }
            PlayerMovement2D.ValidateSnapshot(player);
            return new PreparedRestore(() =>
            {
                if (_decisions != null)
                {
                    if (legacy) _decisions.ResetWorld(agents, meetings, _snapshotRealSeconds());
                    else _decisions.RestoreSnapshot(decisions, agents, meetings, _snapshotRealSeconds());
                }
                _agents = agents;
                _meetings = meetings;
                for (int i = 0; i < residents.Length; i++)
                {
                    residents[i].BindAgents(agents);
                    residents[i].Commit(bodyCandidates[i]);
                }
                _snapshotPlayer.RestoreSnapshot(player);
                Physics2D.SyncTransforms();
                _last = progress;
                _hasState = true;
                _preparedPublication = progress;
                foreach (var resident in residents) resident.CancelInteraction();
                _snapshotGate.Revoke();
                _snapshotClockDriver?.DiscardFrameSample();
                foreach (var resident in residents) CaptureObservation(resident.NpcId, _meetings?.GetContext(resident.NpcId));
            });
        }

        private static void ValidateMeetingBodies(NpcMeetingBoardSnapshot meetings,
            IReadOnlyDictionary<string, NpcBodySnapshot> bodies)
        {
            if (meetings == null) return;
            foreach (var meeting in meetings.Meetings.Where(item => item.State == NpcMeetingState.Talking))
            {
                var plan = meetings.Plans.Single(item => item.Id == meeting.PlanId);
                if (!HasArrived(plan.InitiatorId, plan.InitiatorLocationId)
                    || !HasArrived(plan.PartnerId, plan.PartnerLocationId))
                    throw new ArgumentException("Talking participants must have arrived at their meeting locations with legal bodies.", nameof(bodies));
            }

            bool HasArrived(string npcId, string locationId)
                => bodies.TryGetValue(npcId, out var body) && !body.NoLegalPosition
                    && body.Route.Status == (int)CozyTown.Unity.Town.TownRouteStatus.Arrived
                    && body.Route.TargetLocationId == locationId;
        }

        private string CaptureBodyConfiguration()
        {
            var result = new StringBuilder();
            CozyTown.Unity.Town.TownMap2D.AppendConfiguration(result, "world-bodies-v1", residents.Length,
                _snapshotPlayer.CaptureConfiguration(), _observation.CaptureConfiguration());
            foreach (var resident in residents)
                CozyTown.Unity.Town.TownMap2D.AppendConfiguration(result, resident.CaptureConfiguration());
            return result.ToString();
        }

        private void RequireSnapshotBoundary()
        {
            if (_configuringDecisions || _tickingDecisions || !_hasState || _agents == null
                || residents.Length != 4 || residents.Any(resident => resident == null)
                || residents.Select(resident => resident.NpcId).Distinct().Count() != residents.Length
                || _snapshotPlayer == null || _snapshotGate == null || _snapshotRealSeconds == null)
                throw new InvalidOperationException("Complete snapshots require four bound residents and the player at a completed update boundary.");
        }

        private sealed class PreparedRestore : IPreparedWorldRestore
        {
            private Action _commit;
            internal PreparedRestore(Action commit) => _commit = commit;
            public void Commit()
            {
                var commit = _commit ?? throw new InvalidOperationException("A prepared world restore can only be committed once.");
                _commit = null;
                commit();
            }
        }

        public bool DecisionsEnabled => _decisions != null;
        public bool IsWorldReady => _timeFlow?.State == WorldTimeFlowState.Ready;
        public long DecisionRequestsStarted => _decisions?.RequestsStarted ?? 0;
        public int DecisionRequestsInLastMinute => _decisions?.RequestsInLastMinute ?? 0;
        public int ActiveDecisionRequests => _decisions?.ActiveRequestCount ?? 0;
        public int WaitingDecisionResidents => _decisions?.WaitingResidentCount ?? 0;
        public double GameTotalMinutes => _agents?.TotalMinutes ?? 0;
        public NpcMeetingSnapshot GetMeeting(string npcId) => _meetings?.GetLatest(npcId);
        public IReadOnlyList<NpcMeetingMemory> GetMeetingMemories(string npcId)
            => _meetings?.GetMemories(npcId) ?? Array.Empty<NpcMeetingMemory>();

        public void Configure(params NpcWorldResident2D[] actors)
        {
            if (_configuringDecisions) throw new InvalidOperationException("Complete decision configuration before changing residents.");
            residents = (NpcWorldResident2D[])actors.Clone();
        }

        public CharacterTradeResources GetMeetingResources(string npcId) => _meetings?.GetResources(npcId);

        public void ConfigureObservations(TownLocalObservation2D observation)
            => _observation = observation ?? throw new ArgumentNullException(nameof(observation));

        public NpcLocalObservation GetObservation(string npcId)
            => CaptureObservation(npcId, _meetings?.GetContext(npcId));

        private NpcLocalObservation CaptureObservation(string npcId, NpcSocialContext social)
        {
            var state = GetAgentState(npcId);
            return _observation.Scene.Read(npcId, state.WorldRunId, _agents.TotalMinutes,
                _observation.CaptureBodies(residents), social,
                _resources?.Inspect(social?.Resources?.Terms ?? _meetings?.GetLatest(npcId)?.ResourceTerms, npcId),
                _meetings?.GetMemories(npcId));
        }

        public void Bind(IWorldTimeFlow timeFlow, CharacterResourceTrading resources = null)
        {
            if (_configuringDecisions) throw new InvalidOperationException("Complete decision configuration before changing the world binding.");
            if (timeFlow == null) throw new ArgumentNullException(nameof(timeFlow));
            if (timeFlow.State != WorldTimeFlowState.Ready
                || (_timeFlow != null && _timeFlow.State != WorldTimeFlowState.Ready))
                throw new InvalidOperationException("Complete world recovery before changing the resident binding.");
            bool resourcesChanged = resources != null && !ReferenceEquals(_resources, resources);
            var candidateResources = resources ?? _resources;
            foreach (var resident in residents) resident.ValidateConfiguration();
            if (_observation == null)
            {
                _observation = GetComponent<TownLocalObservation2D>() ?? gameObject.AddComponent<TownLocalObservation2D>();
                var map = residents.FirstOrDefault()?.GetComponentInParent<CozyTown.Unity.Town.TownMap2D>();
                if (residents.All(resident => resident.GetComponentInParent<CozyTown.Unity.Town.TownMap2D>() == map))
                    _observation.TryConfigureDevelopmentMap(map);
            }
            if (_hasState && ReferenceEquals(_timeFlow, timeFlow) && !resourcesChanged) return;
            var schedules = new NpcDailySchedule[residents.Length];
            var actors = new Dictionary<string, NpcWorldResident2D>(StringComparer.Ordinal);
            for (int i = 0; i < residents.Length; i++)
            {
                schedules[i] = residents[i].Schedule;
                actors.Add(residents[i].NpcId, residents[i]);
            }
            var agents = new NpcAgentWorld(schedules, (npcId, locationId) => actors[npcId].CanVisit(locationId));
            var progress = timeFlow.Current;
            agents.Observe(progress);
            _meetings?.ValidateWorldBinding(agents, candidateResources);
            _decisions?.ValidateWorldBinding(agents);
            if (_timeFlow != null)
            {
                _timeFlow.Changed -= Apply;
                _timeFlow.PresentationChanged -= PresentRestoredMeetings;
            }
            _resources = candidateResources;
            _agents = agents;
            foreach (var resident in residents) resident.BindAgents(_agents);
            _timeFlow = timeFlow;
            _hasState = false;
            Apply(progress);
            _decisions?.BindWorld(_agents);
            _timeFlow.Changed += Apply;
            _timeFlow.PresentationChanged += PresentRestoredMeetings;
        }

        public NpcAgentSnapshot GetAgentState(string npcId)
        {
            if (_agents == null) throw new InvalidOperationException("Bind world time before querying residents.");
            return _agents.GetState(npcId);
        }

        public void ConfigureDecisions(INpcDecisionClient client, IEnumerable<NpcDefinition> profiles,
            NpcDecisionSettings settings = null, IEnumerable<NpcMeetingPlan> meetingPlans = null,
            NpcSpeechMode speechMode = NpcSpeechMode.FreeText)
        {
            if (_configuringDecisions || _tickingDecisions)
                throw new InvalidOperationException("Complete the current decision operation before configuring decisions.");
            if (_agents == null) throw new InvalidOperationException("Bind world time before configuring decisions.");
            if (!IsWorldReady) throw new InvalidOperationException("Complete world recovery before configuring decisions.");
            _configuringDecisions = true;
            try
            {
                var profileArray = profiles.ToArray();
                var plans = meetingPlans?.ToArray();
                var meetings = plans == null ? null : new NpcMeetingBoard(_agents, plans, MeetingPresence, _resources);
                if (!IsWorldReady) throw new InvalidOperationException("Complete world recovery before configuring decisions.");
                var before = CaptureActivities();
                var candidate = _decisions == null
                    ? new NpcDecisionScheduler(_agents, profileArray, client, settings, meetings,
                        request => CaptureObservation(request.NpcId, request.Social), speechMode, () => IsWorldReady)
                    : _decisions.CreateReplacement(profileArray, client, settings, meetings,
                        request => CaptureObservation(request.NpcId, request.Social), speechMode, () => IsWorldReady);
                _meetings?.CancelAll();
                _decisions = candidate;
                _meetings = meetings;
                _configuredMeetingPlans = plans;
                RefreshChangedActivities(before);
                if (_meetings != null)
                {
                    if (_meetingView == null) _meetingView = gameObject.AddComponent<NpcMeetingDialogueView>();
                    _meetingView.Configure(residents, profileArray);
                }
                else if (_meetingView != null) { Destroy(_meetingView); _meetingView = null; }
            }
            finally { _configuringDecisions = false; }
        }

        public IReadOnlyList<NpcDecisionOutcome> TickDecisions(double realSeconds)
        {
            if (_decisions == null || _configuringDecisions || _tickingDecisions) return Array.Empty<NpcDecisionOutcome>();
            if (_timeFlow.State == WorldTimeFlowState.Publishing) return Array.Empty<NpcDecisionOutcome>();
            _tickingDecisions = true;
            try
            {
                if (!IsWorldReady) return _decisions.Tick(realSeconds);
                var before = CaptureActivities();
                var outcomes = _decisions.Tick(realSeconds);
                if (!IsWorldReady) return outcomes;
                RefreshChangedActivities(before);
                _meetingView?.Present(_meetings, realSeconds);
                return outcomes;
            }
            finally { _tickingDecisions = false; }
        }

        private NpcActivityRequest[] CaptureActivities()
            => residents.Select(resident => _agents.GetState(resident.NpcId).ActiveActivity).ToArray();

        private void RefreshChangedActivities(NpcActivityRequest[] before)
        {
            for (int i = 0; i < residents.Length; i++)
                if (!ReferenceEquals(before[i], _agents.GetState(residents[i].NpcId).ActiveActivity)) RefreshTarget(residents[i].NpcId);
        }

        private NpcMeetingPresence MeetingPresence(string npcId, string locationId)
        {
            var resident = residents.First(item => item.NpcId == npcId);
            if (resident.GetComponent<CozyTownNpcDebugPresenter>()?.IsOpen == true) return NpcMeetingPresence.Busy;
            if (!resident.isActiveAndEnabled || resident.IsHome || resident.Status == CozyTown.Unity.Town.TownRouteStatus.Blocked)
                return NpcMeetingPresence.Blocked;
            return resident.TargetLocationId == locationId && resident.Status == CozyTown.Unity.Town.TownRouteStatus.Arrived
                ? NpcMeetingPresence.Arrived : NpcMeetingPresence.Travelling;
        }

        public NpcDecisionOutcome GetDecisionOutcome(string npcId) => _decisions?.GetLastOutcome(npcId);

        private void Update() => TickDecisions(UnityEngine.Time.realtimeSinceStartupAsDouble);

        private void PresentRestoredMeetings(WorldTimeProgress progress)
        {
            if (progress.IsRebuild)
                _meetingView?.Present(_meetings, _snapshotRealSeconds?.Invoke()
                    ?? UnityEngine.Time.realtimeSinceStartupAsDouble);
        }

        public IReadOnlyList<NpcAgentEvent> TakeAgentEvents(string npcId)
        {
            if (_agents == null) throw new InvalidOperationException("Bind world time before querying residents.");
            return _agents.TakeEvents(npcId);
        }

        public OperationResult SubmitActivity(NpcActivityRequest request)
        {
            if (_agents == null) return OperationResult.Failure("agent.world_unbound");
            if (!IsWorldReady) return OperationResult.Failure("agent.world_not_ready");
            var result = _agents.SubmitActivity(request);
            if (result.IsSuccess) RefreshTarget(request.NpcId);
            return result;
        }

        public OperationResult CancelActivity(string npcId, Guid worldRunId, long expectedRevision)
        {
            if (_agents == null) return OperationResult.Failure("agent.world_unbound");
            if (!IsWorldReady) return OperationResult.Failure("agent.world_not_ready");
            var result = _agents.CancelActivity(npcId, worldRunId, expectedRevision);
            if (result.IsSuccess) RefreshTarget(npcId);
            return result;
        }

        private void RefreshTarget(string npcId)
        {
            foreach (var resident in residents)
            {
                if (resident.NpcId != npcId) continue;
                resident.Commit(resident.Advance(_last.TotalMinutes, _last.TotalMinutes));
                break;
            }
            Physics2D.SyncTransforms();
        }

        // The world clock owns pause; this subscription follows the bound session,
        // including explicit sleep/load while the presentation is disabled.
        private void OnDestroy()
        {
            _snapshotBinding?.Detach(this);
            _decisions?.Dispose();
            if (_timeFlow != null)
            {
                _timeFlow.Changed -= Apply;
                _timeFlow.PresentationChanged -= PresentRestoredMeetings;
            }
        }

        private void Apply(WorldTimeProgress progress)
        {
            if (_preparedPublication.HasValue)
            {
                if (progress.RebuildVersion != _preparedPublication.Value.RebuildVersion
                    || progress.TotalMinutes != _preparedPublication.Value.TotalMinutes)
                    throw new InvalidOperationException("Prepared bodies must publish at their restored world time.");
                _preparedPublication = null;
                _last = progress;
                return;
            }
            var candidates = new NpcWorldResident2D.Journey[residents.Length];
            bool rebuild = !_hasState || progress.RebuildVersion != _last.RebuildVersion;
            if (rebuild)
            {
                _agents.Observe(progress);
                _meetings?.BindWorld(_agents, _resources);
            }
            for (int i = 0; i < residents.Length; i++)
            {
                candidates[i] = rebuild
                    ? residents[i].Reconstruct(progress.Clock.MinuteOfDay)
                    : residents[i].Advance(progress.AdvanceFromTotalMinutes, progress.TotalMinutes);
            }
            for (int i = 0; i < residents.Length; i++)
            {
                if (rebuild) residents[i].CancelInteraction();
                residents[i].Commit(candidates[i]);
            }
            if (!rebuild) _agents.Observe(progress);
            _last = progress;
            _hasState = true;
            Physics2D.SyncTransforms();
            if (_meetings != null)
            {
                var before = CaptureActivities();
                _meetings.Observe();
                RefreshChangedActivities(before);
            }
        }
    }
}
