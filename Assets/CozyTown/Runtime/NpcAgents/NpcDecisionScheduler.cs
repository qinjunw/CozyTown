using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcLife;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcDecisionScheduler : IDisposable
    {
        private NpcAgentWorld _world;
        private readonly Resident[] _residents;
        private readonly INpcDecisionClient _client;
        private readonly NpcDecisionSettings _settings;
        private readonly NpcMeetingBoard _meetings;
        private readonly Func<NpcDecisionRequest, NpcLocalObservation> _observe;
        private readonly Queue<double> _requestStarts = new Queue<double>();
        private readonly List<NpcDecisionOutcome> _completed = new List<NpcDecisionOutcome>();
        private double _lastTick = double.NegativeInfinity;
        private int _nextResident;
        private bool _disposed;

        public long RequestsStarted { get; private set; }
        public int RequestsInLastMinute => _requestStarts.Count;
        public int ActiveRequestCount => _residents.Count(item => item.Request != null && !item.Request.IsCompleted);
        public int WaitingResidentCount => _residents.Count(item => item.Pending != null || (item.Current != null && item.Request == null));

        private sealed class Resident
        {
            internal NpcDefinition Profile;
            internal NpcDecisionRequest Pending;
            internal NpcDecisionRequest Current;
            internal Task<NpcDecisionReply> Request;
            internal CancellationTokenSource Cancellation;
            internal NpcDecisionOutcome LastOutcome;
            internal readonly List<string> CandidateErrorCodes = new List<string>();
            internal int Calls;
            internal double RequestStarted;
            internal double LastStarted = double.NegativeInfinity;
        }

        public NpcDecisionScheduler(NpcAgentWorld world, IEnumerable<NpcDefinition> profiles, INpcDecisionClient client,
            NpcDecisionSettings settings = null, NpcMeetingBoard meetings = null,
            Func<NpcDecisionRequest, NpcLocalObservation> observe = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _settings = settings ?? new NpcDecisionSettings();
            _meetings = meetings;
            _observe = observe;
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            _residents = profiles.Select(profile => new Resident { Profile = profile }).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var resident in _residents)
            {
                if (resident.Profile == null || !ids.Add(resident.Profile.Id))
                    throw new ArgumentException("Decision residents require unique configured profiles.", nameof(profiles));
                _world.GetState(resident.Profile.Id);
            }
        }

        public NpcDecisionOutcome GetLastOutcome(string npcId)
        {
            var resident = _residents.FirstOrDefault(item => item.Profile.Id == npcId);
            if (resident == null) throw new ArgumentException("The NPC has no decision profile.", nameof(npcId));
            return resident.LastOutcome;
        }

        public void BindWorld(NpcAgentWorld world)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NpcDecisionScheduler));
            if (world == null) throw new ArgumentNullException(nameof(world));
            foreach (var resident in _residents) world.GetState(resident.Profile.Id);
            _world = world;
            _meetings?.BindWorld(world);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var resident in _residents)
            {
                resident.Pending = null;
                if (resident.Current != null) Finish(resident, "agent.decision_cancelled", cancelRequest: true);
                var task = resident.Request;
                var cancellation = resident.Cancellation;
                if (task == null) continue;
                _ = task.ContinueWith(completed =>
                {
                    _ = completed.Exception;
                    cancellation.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                resident.Request = null;
                resident.Cancellation = null;
            }
        }

        public IReadOnlyList<NpcDecisionOutcome> Tick(double realSeconds)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NpcDecisionScheduler));
            if (double.IsNaN(realSeconds) || double.IsInfinity(realSeconds) || realSeconds < 0 || realSeconds < _lastTick)
                throw new ArgumentOutOfRangeException(nameof(realSeconds), "Decision time must be finite, nonnegative and monotonic.");
            _lastTick = realSeconds;
            _completed.Clear();
            _meetings?.Observe();
            while (_requestStarts.Count > 0 && _requestStarts.Peek() <= realSeconds - 60) _requestStarts.Dequeue();
            int active = 0;
            foreach (var resident in _residents)
            {
                var state = _world.GetState(resident.Profile.Id);
                if (resident.Current != null)
                {
                    string invalid = InvalidContext(resident.Current, state);
                    if (invalid != null) Finish(resident, invalid, cancelRequest: true);
                }
                if (resident.Current != null && realSeconds >= resident.LastStarted + _settings.DecisionTimeoutSeconds)
                    Finish(resident, "agent.decision_timeout", cancelRequest: true);
                if (resident.Current != null && resident.Request != null
                    && realSeconds >= resident.RequestStarted + _settings.RequestTimeoutSeconds)
                    Finish(resident, "agent.request_timeout", cancelRequest: true);
                if (resident.Pending != null && InvalidContext(resident.Pending, state) != null)
                    resident.Pending = null;
                if (resident.Request != null)
                {
                    if (resident.Request.IsCompleted)
                    {
                        NpcDecisionReply reply = null;
                        bool receivedReply = false;
                        try
                        {
                            reply = resident.Request.GetAwaiter().GetResult();
                            receivedReply = true;
                        }
                        catch (NpcCandidateException exception)
                        {
                            if (resident.Current != null) HandleCandidateFailure(resident, exception);
                        }
                        catch (FormatException)
                        {
                            if (resident.Current != null) Finish(resident, "agent.response_invalid");
                        }
                        catch (Exception)
                        {
                            if (resident.Current != null) Finish(resident, "agent.client_failure");
                        }
                        if (resident.Current != null && receivedReply) ApplyReply(resident, reply);
                        resident.Request = null;
                        resident.Cancellation.Dispose();
                        resident.Cancellation = null;
                    }
                    else active++;
                }
                var events = _world.TakeEvents(resident.Profile.Id);
                state = _world.GetState(resident.Profile.Id);
                var social = _meetings?.GetContext(resident.Profile.Id);
                if (social != null)
                {
                    if (events.Count == 0) continue;
                }
                else if (_meetings?.GetCurrent(resident.Profile.Id) != null
                    || !events.Any(item => item.Kind != NpcAgentEventKind.ActivityAccepted && item.Kind != NpcAgentEventKind.MeetingChanged)
                    || state.ActiveActivity != null || state.Target.ExpectedActivity != NpcActivity.Resting) continue;
                resident.Pending = new NpcDecisionRequest(resident.Profile, state, _world.TotalMinutes, events,
                    _world.GetKnownLocationIds(resident.Profile.Id), _settings.MaxCallsPerDecision,
                    resident.LastOutcome?.WorldRunId == state.WorldRunId ? resident.LastOutcome.Code : null, social);
            }
            int first = _nextResident;
            for (int offset = 0; offset < _residents.Length; offset++)
            {
                if (active >= _settings.MaxConcurrentRequests || _requestStarts.Count >= _settings.MaxRequestsPerMinute) break;
                int index = (first + offset) % _residents.Length;
                var resident = _residents[index];
                if (resident.Request != null) continue;
                if (resident.Current == null)
                {
                    if (resident.Pending == null) continue;
                    if (InvalidContext(resident.Pending, _world.GetState(resident.Profile.Id)) != null)
                    {
                        resident.Pending = null;
                        continue;
                    }
                    if (resident.Pending.Social == null && _residents.Any(other =>
                    {
                        var contact = (other.Current ?? other.Pending)?.Social;
                        return contact?.Kind == NpcSocialContextKind.Opportunity && contact.Resources != null
                            && contact.PartnerId == resident.Profile.Id;
                    })) continue;
                    bool socialReply = resident.Pending.Social != null && resident.Pending.Social.Kind != NpcSocialContextKind.Opportunity;
                    if (!socialReply && realSeconds < resident.LastStarted + _settings.ResidentCooldownSeconds) continue;
                    if (resident.Pending.Social != null)
                    {
                        var social = _meetings.GetContext(resident.Profile.Id);
                        if (social == null) { resident.Pending = null; continue; }
                        resident.Pending = new NpcDecisionRequest(resident.Pending, social);
                    }
                    resident.Current = resident.Pending;
                    resident.Pending = null;
                    resident.LastStarted = realSeconds;
                    resident.Calls = 0;
                    resident.CandidateErrorCodes.Clear();
                    if (_observe != null)
                    {
                        if (!TryReadObservation(resident.Current, out var observation))
                        {
                            Finish(resident, "agent.observation_unavailable");
                            continue;
                        }
                        resident.Current = new NpcDecisionRequest(resident.Current, observation);
                    }
                    if (resident.Current.Social?.Kind == NpcSocialContextKind.Opportunity) _meetings.TakeOpportunity(resident.Profile.Id);
                }
                else if (_observe != null)
                {
                    string invalid = ObservationFailure(resident.Current);
                    if (invalid != null) { Finish(resident, invalid); continue; }
                }
                _requestStarts.Enqueue(realSeconds);
                RequestsStarted++;
                resident.Calls++;
                resident.RequestStarted = realSeconds;
                resident.Cancellation = new CancellationTokenSource();
                bool candidateFailure = false;
                try
                {
                    resident.Request = _client.DecideAsync(resident.Current, resident.Cancellation.Token);
                }
                catch (NpcCandidateException exception)
                {
                    candidateFailure = true;
                    HandleCandidateFailure(resident, exception);
                }
                catch (FormatException)
                {
                    Finish(resident, "agent.response_invalid");
                }
                catch (Exception)
                {
                    Finish(resident, "agent.client_failure");
                }
                if (resident.Request == null)
                {
                    if (resident.Current != null && !candidateFailure) Finish(resident, "agent.client_failure");
                    resident.Cancellation.Dispose();
                    resident.Cancellation = null;
                }
                else active++;
                _nextResident = (index + 1) % _residents.Length;
            }
            return Array.AsReadOnly(_completed.ToArray());
        }

        private void ApplyReply(Resident resident, NpcDecisionReply reply)
        {
            var context = resident.Current;
            if (reply == null)
            {
                Finish(resident, "agent.response_invalid");
                return;
            }
            string observationFailure = _observe == null ? null : ObservationFailure(context);
            if (observationFailure != null)
            {
                Finish(resident, observationFailure, reply: reply);
                return;
            }
            if (!context.AllowedOperations.Contains(reply.Operation))
            {
                Finish(resident, string.IsNullOrEmpty(reply.Operation) ? "agent.response_invalid" : "agent.operation_unavailable", reply: reply);
                return;
            }
            if (context.Social != null && reply.Kind != NpcDecisionKind.Wait)
            {
                if (reply.Kind == NpcDecisionKind.Invite)
                {
                    if (reply.PlanId != context.Social.PlanId) { Finish(resident, "meeting.plan_unknown", reply: reply); return; }
                    var invited = _meetings.Invite(context.Self, reply.PlanId);
                    Finish(resident, invited.IsSuccess ? "meeting.invited" : invited.ErrorCode, reply: reply);
                    return;
                }
                if (reply.MeetingId != context.Social.MeetingId) { Finish(resident, "meeting.unknown", reply: reply); return; }
                var result = reply.Kind == NpcDecisionKind.Speak ? _meetings.Speak(context.Self, reply.MeetingId, reply.Text)
                    : reply.Kind == NpcDecisionKind.Deliver ? _meetings.Deliver(context.Self, reply.MeetingId)
                    : reply.Kind == NpcDecisionKind.CancelExchange ? _meetings.CancelExchange(context.Self, reply.MeetingId)
                    : reply.Kind == NpcDecisionKind.EndConversation ? _meetings.EndConversation(context.Self, reply.MeetingId)
                    : _meetings.Respond(context.Self, reply.MeetingId, reply.Kind == NpcDecisionKind.AcceptInvitation);
                Finish(resident, result.IsSuccess ? "meeting." + reply.Operation : result.ErrorCode, reply: reply);
                return;
            }
            if (reply.Kind == NpcDecisionKind.Wait)
            {
                Finish(resident, "agent.decision_wait", reply: reply);
                return;
            }
            if (reply.Kind == NpcDecisionKind.InspectLocation)
            {
                if (resident.Calls >= _settings.MaxCallsPerDecision)
                {
                    Finish(resident, "agent.decision_step_limit", reply: reply);
                    return;
                }
                var details = _world.InspectLocation(resident.Profile.Id, reply.LocationId);
                if (details.IsSuccess)
                {
                    resident.Current = new NpcDecisionRequest(context, details.Value);
                    return;
                }
                Finish(resident, details.ErrorCode, reply: reply);
                return;
            }
            if (reply.Kind == NpcDecisionKind.Visit)
            {
                if (!context.KnownLocationIds.Contains(reply.LocationId))
                {
                    Finish(resident, "agent.location_unknown", reply: reply);
                    return;
                }
                var result = _world.SubmitActivity(new NpcActivityRequest(context.NpcId, context.Self.WorldRunId, context.Self.Revision,
                    reply.LocationId, reply.Activity, _world.TotalMinutes + reply.DurationGameMinutes));
                Finish(resident, result.IsSuccess ? "agent.activity_accepted" : result.ErrorCode, reply: reply);
                return;
            }
            Finish(resident, "agent.response_invalid", reply: reply);
        }

        private string InvalidContext(NpcDecisionRequest context, NpcAgentSnapshot state)
        {
            if (context.Self.WorldRunId != state.WorldRunId) return "agent.world_stale";
            if (context.Self.Revision != state.Revision) return "agent.decision_stale";
            if (_world.TotalMinutes >= context.GameTotalMinutes + _settings.OpportunityLifetimeGameMinutes)
                return "agent.opportunity_expired";
            return null;
        }

        private bool TryReadObservation(NpcDecisionRequest context, out NpcLocalObservation observation)
        {
            observation = null;
            try
            {
                observation = _observe(context);
                return observation != null && observation.ObserverId == context.NpcId
                    && observation.WorldRunId == context.Self.WorldRunId
                    && observation.WorldRunId == _world.GetState(context.NpcId).WorldRunId
                    && observation.ObservedAtTotalMinutes == _world.TotalMinutes
                    && observation.ListenerId == context.Social?.PartnerId;
            }
            catch (Exception) { return false; }
        }

        private string ObservationFailure(NpcDecisionRequest context)
        {
            if (!TryReadObservation(context, out var current)) return "agent.observation_unavailable";
            return context.Observation != null && context.Observation.HasSameFacts(current) ? null : "agent.observation_stale";
        }

        private void HandleCandidateFailure(Resident resident, NpcCandidateException exception)
        {
            resident.CandidateErrorCodes.Add(exception.Code);
            if (exception.CanCorrect && resident.CandidateErrorCodes.Count == 1 && resident.Calls < _settings.MaxCallsPerDecision)
                resident.Current = new NpcDecisionRequest(resident.Current, resident.Current.LocationDetails, exception.Code);
            else Finish(resident, "agent.response_invalid");
        }

        private void Finish(Resident resident, string code, bool cancelRequest = false, NpcDecisionReply reply = null)
        {
            var outcome = new NpcDecisionOutcome(resident.Current, code, resident.Calls, resident.LastStarted, _lastTick, reply,
                resident.CandidateErrorCodes);
            resident.LastOutcome = outcome;
            _completed.Add(outcome);
            resident.Current = null;
            if (cancelRequest)
            {
                try { resident.Cancellation?.Cancel(); }
                catch (AggregateException) { /* A client callback cannot prevent invalidating its decision. */ }
            }
        }
    }
}
