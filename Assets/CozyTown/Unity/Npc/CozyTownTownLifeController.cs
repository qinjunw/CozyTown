using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    [DisallowMultipleComponent]
    public sealed class CozyTownTownLifeController : MonoBehaviour
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

        public bool DecisionsEnabled => _decisions != null;
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
            if (timeFlow == null) throw new ArgumentNullException(nameof(timeFlow));
            bool resourcesChanged = resources != null && !ReferenceEquals(_resources, resources);
            if (resources != null) _resources = resources;
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
            if (_timeFlow != null) _timeFlow.Changed -= Apply;
            _agents = agents;
            foreach (var resident in residents) resident.BindAgents(_agents);
            _timeFlow = timeFlow;
            _hasState = false;
            Apply(_timeFlow.Current);
            _decisions?.BindWorld(_agents);
            _timeFlow.Changed += Apply;
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
            if (_agents == null) throw new InvalidOperationException("Bind world time before configuring decisions.");
            var profileArray = profiles.ToArray();
            var meetings = meetingPlans == null ? null : new NpcMeetingBoard(_agents, meetingPlans, MeetingPresence, _resources);
            var candidate = new NpcDecisionScheduler(_agents, profileArray, client, settings, meetings,
                request => CaptureObservation(request.NpcId, request.Social), speechMode);
            var before = CaptureActivities();
            _decisions?.Dispose();
            _meetings?.CancelAll();
            _decisions = candidate;
            _meetings = meetings;
            RefreshChangedActivities(before);
            if (_meetings != null)
            {
                if (_meetingView == null) _meetingView = gameObject.AddComponent<NpcMeetingDialogueView>();
                _meetingView.Configure(residents, profileArray);
            }
            else if (_meetingView != null) { Destroy(_meetingView); _meetingView = null; }
        }

        public void TickDecisions(double realSeconds)
        {
            if (_decisions == null) return;
            var before = CaptureActivities();
            _decisions.Tick(realSeconds);
            RefreshChangedActivities(before);
            _meetingView?.Present(_meetings, realSeconds);
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

        public IReadOnlyList<NpcAgentEvent> TakeAgentEvents(string npcId)
        {
            if (_agents == null) throw new InvalidOperationException("Bind world time before querying residents.");
            return _agents.TakeEvents(npcId);
        }

        public OperationResult SubmitActivity(NpcActivityRequest request)
        {
            if (_agents == null) return OperationResult.Failure("agent.world_unbound");
            var result = _agents.SubmitActivity(request);
            if (result.IsSuccess) RefreshTarget(request.NpcId);
            return result;
        }

        public OperationResult CancelActivity(string npcId, Guid worldRunId, long expectedRevision)
        {
            if (_agents == null) return OperationResult.Failure("agent.world_unbound");
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
            _decisions?.Dispose();
            if (_timeFlow != null) _timeFlow.Changed -= Apply;
        }

        private void Apply(WorldTimeProgress progress)
        {
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
