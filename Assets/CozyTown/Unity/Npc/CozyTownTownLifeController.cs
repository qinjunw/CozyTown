using System;
using System.Collections.Generic;
using CozyTown.Runtime.Core;
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

        public bool DecisionsEnabled => _decisions != null;
        public long DecisionRequestsStarted => _decisions?.RequestsStarted ?? 0;
        public int DecisionRequestsInLastMinute => _decisions?.RequestsInLastMinute ?? 0;
        public int ActiveDecisionRequests => _decisions?.ActiveRequestCount ?? 0;
        public int WaitingDecisionResidents => _decisions?.WaitingResidentCount ?? 0;

        public void Configure(params NpcWorldResident2D[] actors)
        {
            residents = (NpcWorldResident2D[])actors.Clone();
        }

        public void Bind(IWorldTimeFlow timeFlow)
        {
            if (timeFlow == null) throw new ArgumentNullException(nameof(timeFlow));
            foreach (var resident in residents) resident.ValidateConfiguration();
            if (_hasState && ReferenceEquals(_timeFlow, timeFlow)) return;
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
            NpcDecisionSettings settings = null)
        {
            if (_agents == null) throw new InvalidOperationException("Bind world time before configuring decisions.");
            var candidate = new NpcDecisionScheduler(_agents, profiles, client, settings);
            _decisions?.Dispose();
            _decisions = candidate;
        }

        public void TickDecisions(double realSeconds)
        {
            if (_decisions == null) return;
            foreach (var outcome in _decisions.Tick(realSeconds))
                if (outcome.ActivityAccepted) RefreshTarget(outcome.NpcId);
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
            if (rebuild) _agents.Observe(progress);
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
        }
    }
}
