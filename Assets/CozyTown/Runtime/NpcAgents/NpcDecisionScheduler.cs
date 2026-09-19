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
        private NpcMeetingBoard _meetings;
        private readonly Func<NpcDecisionRequest, NpcLocalObservation> _observe;
        private readonly NpcSpeechMode _speechMode;
        private readonly Func<bool> _canDecide;
        private RequestHistory _history = new RequestHistory();
        private readonly List<RetiredRequest> _retiredRequests = new List<RetiredRequest>();

        private sealed class RetiredRequest
        {
            internal Task<NpcDecisionReply> Task;
            internal CancellationTokenSource Cancellation;
        }

        private sealed class RequestHistory
        {
            internal readonly Queue<double> Starts = new Queue<double>();
            // Snapshot loads restore behavior cooldowns; actual call times and counts remain unchanged.
            internal readonly Dictionary<string, double> ResidentStarts = new Dictionary<string, double>(StringComparer.Ordinal);
            internal long RequestsStarted;
        }
        private readonly List<NpcDecisionOutcome> _completed = new List<NpcDecisionOutcome>();
        private double _lastTick = double.NegativeInfinity;
        private int _nextResident;
        private bool _disposed;
        private bool _isTicking;
        private bool _isReplacing;
        private bool _isRestoring;

        public long RequestsStarted => _history.RequestsStarted;
        public int RequestsInLastMinute => _history.Starts.Count;
        public int ActiveRequestCount => _residents.Count(item => item.Request != null && !item.Request.IsCompleted)
            + _retiredRequests.Count(item => !item.Task.IsCompleted);
        public int WaitingResidentCount => _residents.Count(item => item.WaitingTurn != null || item.Pending != null
            || (item.Current != null && item.Request == null));

        private sealed class Resident
        {
            internal NpcDefinition Profile;
            internal NpcDecisionRequest Pending;
            internal ConversationTurn WaitingTurn;
            internal NpcDecisionRequest Current;
            internal Task<NpcDecisionReply> Request;
            internal CancellationTokenSource Cancellation;
            internal NpcDecisionOutcome LastOutcome;
            internal readonly List<string> CandidateErrorCodes = new List<string>();
            internal int Calls;
            internal double RequestStarted;
            internal double LastStarted = double.NegativeInfinity;
            internal double DecisionDeadlineSeconds;
            internal bool RefreshObservation;
            internal string RestoredLocationId;
            internal string RestoredLastResultCode;
            internal Guid RestoredResultWorldRunId;
        }

        private sealed class ConversationTurn
        {
            internal readonly Guid WorldRunId, MeetingId;
            internal readonly string SpeakerId;
            internal readonly int SpokenLines;
            internal readonly IReadOnlyList<NpcAgentEvent> Triggers;

            internal ConversationTurn(NpcAgentSnapshot self, NpcSocialContext social, IReadOnlyList<NpcAgentEvent> triggers)
            {
                WorldRunId = self.WorldRunId;
                MeetingId = social.MeetingId;
                SpeakerId = self.NpcId;
                SpokenLines = social.Transcript.Count;
                Triggers = triggers;
            }

            internal bool Matches(NpcAgentSnapshot self, NpcSocialContext social, double gameTotalMinutes)
                => self.WorldRunId == WorldRunId && self.NpcId == SpeakerId && IsDeliveredConversation(social)
                    && social.MeetingId == MeetingId && social.Transcript.Count == SpokenLines
                    && gameTotalMinutes < social.DeadlineTotalMinutes;
        }

        private static bool IsDeliveredConversation(NpcSocialContext social)
            => social?.Kind == NpcSocialContextKind.Conversation && social.DeliveryResultCode == "resource.delivered";

        public NpcDecisionScheduler(NpcAgentWorld world, IEnumerable<NpcDefinition> profiles, INpcDecisionClient client,
            NpcDecisionSettings settings = null, NpcMeetingBoard meetings = null,
            Func<NpcDecisionRequest, NpcLocalObservation> observe = null, NpcSpeechMode speechMode = NpcSpeechMode.FreeText,
            Func<bool> canDecide = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _settings = settings ?? new NpcDecisionSettings();
            _meetings = meetings;
            _observe = observe;
            _canDecide = canDecide;
            if (speechMode != NpcSpeechMode.FreeText && speechMode != NpcSpeechMode.StructuredFacts)
                throw new ArgumentOutOfRangeException(nameof(speechMode));
            _speechMode = speechMode;
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

        public NpcDecisionSchedulerSnapshot CaptureSnapshot(double realSeconds)
        {
            RequireSnapshotBoundary();
            RequireRealTime(realSeconds);
            string clientConfiguration = RequireClientConfiguration();
            var residents = _residents.Select(resident => new NpcDecisionResidentSnapshot(resident.Profile,
                Math.Min(_settings.ResidentCooldownSeconds, Math.Max(0, resident.LastStarted + _settings.ResidentCooldownSeconds - realSeconds)),
                PreviousResultCode(resident), CaptureProgress(resident, realSeconds), CaptureProgress(resident, realSeconds, pending: true),
                CaptureWaitingTurn(resident))).ToArray();
            return new NpcDecisionSchedulerSnapshot(_world.TotalMinutes, residents, new NpcDecisionSettingsSnapshot(_settings),
                _speechMode, _residents.Length == 0 ? null : _residents[_nextResident].Profile.Id, clientConfiguration);
        }

        private NpcDecisionProgressSnapshot CaptureProgress(Resident resident, double realSeconds, bool pending = false)
        {
            var current = pending ? resident.Pending : resident.Current;
            if (current == null || InvalidContext(current, _world.GetState(resident.Profile.Id)) != null) return null;
            if (!pending && resident.Request != null && realSeconds >= resident.RequestStarted + _settings.RequestTimeoutSeconds)
                return null;
            var social = current.Social;
            if (social != null && MatchingSocial(_meetings, resident.Profile.Id, social.Kind, social.PlanId, social.MeetingId,
                social.Transcript.Count, current.GameTotalMinutes, !pending, _world.TotalMinutes) == null) return null;
            int calls = pending ? 0 : resident.Calls;
            return new NpcDecisionProgressSnapshot(current.Self.Revision, current.GameTotalMinutes,
                current.ActivityDeadlineTotalMinutes, current.Triggers, current.MaxCalls, calls + 1,
                calls, pending ? 0 : Math.Min(_settings.DecisionTimeoutSeconds, Math.Max(0, resident.DecisionDeadlineSeconds - realSeconds)),
                current.PreviousResultCode,
                !pending && (current.LocationDetails != null || resident.RestoredLocationId != null),
                pending ? null : current.LocationDetails?.LocationId ?? resident.RestoredLocationId,
                pending ? (IEnumerable<string>)Array.Empty<string>() : resident.CandidateErrorCodes, current.CandidateErrorCode,
                social?.Kind, social?.PlanId, social?.MeetingId ?? Guid.Empty, social?.Transcript.Count ?? 0);
        }

        private NpcWaitingTurnSnapshot CaptureWaitingTurn(Resident resident)
        {
            var turn = resident.WaitingTurn;
            return turn != null && turn.Matches(_world.GetState(resident.Profile.Id), _meetings?.GetContext(resident.Profile.Id), _world.TotalMinutes)
                ? new NpcWaitingTurnSnapshot(turn.MeetingId, turn.SpeakerId, turn.SpokenLines, turn.Triggers) : null;
        }

        public void ValidateSnapshot(NpcDecisionSchedulerSnapshot snapshot, NpcAgentWorld world, NpcMeetingBoard meetings)
        {
            RequireSnapshotBoundary();
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (snapshot.GameTotalMinutes != world.TotalMinutes || snapshot.Settings?.Matches(_settings) != true
                || snapshot.ClientConfiguration != RequireClientConfiguration()
                || snapshot.SpeechMode != _speechMode || snapshot.Residents == null || snapshot.Residents.Count != _residents.Length
                || !snapshot.Residents.Select(item => item?.NpcId).SequenceEqual(_residents.Select(item => item.Profile.Id)))
                throw new ArgumentException("Decision snapshot time, settings and residents must match the restored world.", nameof(snapshot));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var saved in snapshot.Residents)
            {
                var resident = _residents.FirstOrDefault(item => item.Profile.Id == saved?.NpcId);
                if (resident == null || !ids.Add(saved.NpcId) || !saved.Matches(resident.Profile)
                    || !FiniteRange(saved.RemainingCooldownSeconds, 0, _settings.ResidentCooldownSeconds))
                    throw new ArgumentException("Decision snapshot resident configuration or cooldown is invalid.", nameof(snapshot));
                world.GetState(saved.NpcId);
                ValidateProgress(saved.Current, saved.NpcId, world, meetings, true);
                ValidateProgress(saved.Pending, saved.NpcId, world, meetings, false);
                if (saved.WaitingTurn != null)
                {
                    var turn = saved.WaitingTurn;
                    var social = meetings?.GetContext(saved.NpcId);
                    if (turn.SpeakerId != saved.NpcId || !IsDeliveredConversation(social) || social.MeetingId != turn.MeetingId
                        || social.Transcript.Count != turn.SpokenLines || world.TotalMinutes >= social.DeadlineTotalMinutes
                        || !ValidTriggers(turn.Triggers, world.TotalMinutes))
                        throw new ArgumentException("The saved conversation turn must match the restored meeting speaker and transcript.", nameof(snapshot));
                }
            }
            if (_residents.Length == 0 ? snapshot.NextResidentId != null : !ids.Contains(snapshot.NextResidentId ?? string.Empty))
                throw new ArgumentException("Decision polling cursor must identify a configured resident.", nameof(snapshot));
        }

        private void ValidateProgress(NpcDecisionProgressSnapshot progress, string npcId, NpcAgentWorld world,
            NpcMeetingBoard meetings, bool started)
        {
            if (progress == null) return;
            if (progress.ExpectedRevision != world.GetState(npcId).Revision || !FiniteRange(progress.GameTotalMinutes, 0, world.TotalMinutes)
                    || world.TotalMinutes >= progress.GameTotalMinutes + _settings.OpportunityLifetimeGameMinutes
                    || progress.MaxCalls != _settings.MaxCallsPerDecision
                    || (started ? progress.Calls < 1 || progress.Calls > progress.MaxCalls : progress.Calls != 0)
                    || progress.NextStep != progress.Calls + 1
                    || !FiniteRange(progress.RemainingDecisionSeconds, 0, started ? _settings.DecisionTimeoutSeconds : 0)
                    || progress.HasLocationDetails != (progress.LocationId != null)
                    || (progress.HasLocationDetails && (!started || !world.GetKnownLocationIds(npcId).Contains(progress.LocationId)))
                    || progress.CandidateErrorCodes == null || progress.CandidateErrorCodes.Count > (started ? 1 : 0)
                    || progress.CandidateErrorCodes.Any(code => !NpcCandidateException.IsKnownCode(code) || !new NpcCandidateException(code).CanCorrect)
                    || (progress.CandidateErrorCode != null && (progress.CandidateErrorCodes.Count != 1
                        || progress.CandidateErrorCodes[0] != progress.CandidateErrorCode))
                    || !ValidTriggers(progress.Triggers, world.TotalMinutes))
                throw new ArgumentException("Decision continuation fields are invalid.", nameof(progress));
            if (progress.SocialKind == null)
            {
                if (progress.PlanId != null || progress.MeetingId != Guid.Empty || progress.SpokenLines != 0
                    || progress.ActivityDeadlineTotalMinutes != world.NextScheduleChangeAfter(npcId, progress.GameTotalMinutes)
                    || world.TotalMinutes >= progress.ActivityDeadlineTotalMinutes || world.GetState(npcId).ActiveActivity != null
                    || world.GetState(npcId).Target.ExpectedActivity != NpcActivity.Resting)
                    throw new ArgumentException("An ordinary decision must retain its original schedule window.", nameof(progress));
            }
            else if (progress.ActivityDeadlineTotalMinutes != 0 || progress.HasLocationDetails
                || !Enum.IsDefined(typeof(NpcSocialContextKind), progress.SocialKind.Value)
                || meetings?.MatchesSnapshotDecisionContext(npcId, progress.SocialKind.Value, progress.PlanId, progress.MeetingId,
                    progress.SpokenLines, Math.Floor(progress.GameTotalMinutes / 1440) * 1440, started) != true)
                throw new ArgumentException("The saved social decision must match the restored meeting or offered plan.", nameof(progress));
        }

        private static bool ValidTriggers(IReadOnlyList<NpcAgentEvent> triggers, double totalMinutes)
            => triggers != null && triggers.Count > 0 && triggers.Count <= 16 && triggers.Select(item => item?.Kind).Distinct().Count() == triggers.Count
                && triggers.All(item => item != null && Enum.IsDefined(typeof(NpcAgentEventKind), item.Kind)
                    && FiniteRange(item.TotalMinutes, 0, totalMinutes));

        private static NpcSocialContext MatchingSocial(NpcMeetingBoard meetings, string npcId, NpcSocialContextKind kind,
            string planId, Guid meetingId, int spokenLines, double gameTotalMinutes, bool started, double currentTotalMinutes)
        {
            if (meetings == null) return null;
            var social = kind == NpcSocialContextKind.Opportunity && started
                ? meetings.GetDecisionContinuationContext(npcId, planId, Math.Floor(gameTotalMinutes / 1440) * 1440)
                : meetings.GetContext(npcId);
            return social != null && social.Kind == kind && social.PlanId == planId
                && social.MeetingId == meetingId && social.Transcript.Count == spokenLines
                && currentTotalMinutes < social.DeadlineTotalMinutes ? social : null;
        }

        public void RestoreSnapshot(NpcDecisionSchedulerSnapshot snapshot, NpcAgentWorld world,
            NpcMeetingBoard meetings, double realSeconds)
        {
            ValidateSnapshot(snapshot, world, meetings);
            RequireRealTime(realSeconds);
            var restored = _residents.Select(previous =>
            {
                var saved = snapshot.Residents.First(item => item.NpcId == previous.Profile.Id);
                double lastStarted = realSeconds + saved.RemainingCooldownSeconds - _settings.ResidentCooldownSeconds;
                var resident = new Resident { Profile = previous.Profile, LastStarted = lastStarted,
                    RestoredLastResultCode = saved.LastResultCode, RestoredResultWorldRunId = world.GetState(saved.NpcId).WorldRunId };
                resident.Pending = RestoreProgress(saved.Pending, resident.Profile, world, meetings, false);
                var progress = saved.Current;
                resident.Current = RestoreProgress(progress, resident.Profile, world, meetings, true);
                if (resident.Current != null)
                {
                    resident.Calls = progress.Calls;
                    resident.DecisionDeadlineSeconds = realSeconds + progress.RemainingDecisionSeconds;
                    resident.RefreshObservation = true;
                    resident.RestoredLocationId = progress.LocationId;
                    resident.CandidateErrorCodes.AddRange(progress.CandidateErrorCodes);
                }
                if (saved.WaitingTurn != null)
                    resident.WaitingTurn = new ConversationTurn(world.GetState(saved.NpcId), meetings.GetContext(saved.NpcId), saved.WaitingTurn.Triggers);
                return resident;
            }).ToArray();
            CommitRestoredResidents(restored, world, meetings, realSeconds,
                _residents.Length == 0 ? 0 : Array.FindIndex(restored, item => item.Profile.Id == snapshot.NextResidentId));
        }

        public void ResetWorld(NpcAgentWorld world, NpcMeetingBoard meetings, double realSeconds)
        {
            RequireSnapshotBoundary();
            RequireRealTime(realSeconds);
            if (world == null) throw new ArgumentNullException(nameof(world));
            foreach (var resident in _residents) world.GetState(resident.Profile.Id);
            meetings?.ValidateWorldBinding(world);
            var restored = _residents.Select(previous => new Resident { Profile = previous.Profile }).ToArray();
            CommitRestoredResidents(restored, world, meetings, realSeconds, 0);
        }

        private void CommitRestoredResidents(Resident[] restored, NpcAgentWorld world, NpcMeetingBoard meetings,
            double realSeconds, int nextResident)
        {
            _isReplacing = true;
            _isRestoring = true;
            try
            {
                foreach (var resident in _residents)
                {
                    var request = resident.Request;
                    if (request == null) continue;
                    if (request.IsCompleted)
                    {
                        _ = request.Exception;
                        resident.Cancellation.Dispose();
                    }
                    else
                    {
                        _retiredRequests.Add(new RetiredRequest { Task = request, Cancellation = resident.Cancellation });
                        try { resident.Cancellation.Cancel(); }
                        catch (AggregateException) { /* Invalidating the old decision must survive client cancellation callbacks. */ }
                    }
                }
                Array.Copy(restored, _residents, restored.Length);
                _world = world;
                _meetings = meetings;
                _lastTick = realSeconds;
                _nextResident = nextResident;
                foreach (var resident in _residents) _history.ResidentStarts[resident.Profile.Id] = resident.LastStarted;
                _completed.Clear();
            }
            finally { _isReplacing = false; _isRestoring = false; }
        }

        private NpcDecisionRequest RestoreProgress(NpcDecisionProgressSnapshot progress, NpcDefinition profile, NpcAgentWorld world,
            NpcMeetingBoard meetings, bool started)
        {
            if (progress == null || progress.ExpectedRevision != world.GetState(profile.Id).Revision
                || world.TotalMinutes >= progress.GameTotalMinutes + _settings.OpportunityLifetimeGameMinutes
                || (progress.SocialKind == null && world.TotalMinutes >= progress.ActivityDeadlineTotalMinutes)) return null;
            var social = progress.SocialKind == null ? null : MatchingSocial(meetings, profile.Id, progress.SocialKind.Value,
                progress.PlanId, progress.MeetingId, progress.SpokenLines, progress.GameTotalMinutes, started, world.TotalMinutes);
            if (progress.SocialKind != null && social == null) return null;
            return new NpcDecisionRequest(profile, world.GetState(profile.Id), world.GetKnownLocationIds(profile.Id), progress, _speechMode, social);
        }

        private void RequireSnapshotBoundary()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NpcDecisionScheduler));
            if (_isTicking || _isReplacing) throw new InvalidOperationException("Complete the current decision operation before capturing or restoring decisions.");
        }

        private string RequireClientConfiguration()
        {
            string configuration = (_client as INpcDecisionConfiguration)?.SnapshotConfiguration;
            if (string.IsNullOrWhiteSpace(configuration) || configuration.Length > 16384)
                throw new InvalidOperationException("Complete snapshots require a declared non-secret decision client configuration of at most 16384 characters.");
            return configuration;
        }

        private void RequireRealTime(double realSeconds)
        {
            if (!FiniteRange(realSeconds, 0, double.MaxValue) || realSeconds < _lastTick)
                throw new ArgumentOutOfRangeException(nameof(realSeconds), "Decision time must be finite, nonnegative and monotonic.");
        }

        private static bool FiniteRange(double value, double minimum, double maximum)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum;

        private string PreviousResultCode(Resident resident)
            => resident.LastOutcome?.WorldRunId == _world.GetState(resident.Profile.Id).WorldRunId
                ? resident.LastOutcome.Code : resident.RestoredResultWorldRunId == _world.GetState(resident.Profile.Id).WorldRunId
                    ? resident.RestoredLastResultCode : null;

        public void BindWorld(NpcAgentWorld world)
        {
            ValidateWorldBinding(world);
            _meetings?.BindWorld(world);
            _world = world;
        }

        public void ValidateWorldBinding(NpcAgentWorld world)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NpcDecisionScheduler));
            if (_isReplacing) throw new InvalidOperationException("Complete decision replacement before changing the world binding.");
            if (world == null) throw new ArgumentNullException(nameof(world));
            foreach (var resident in _residents) world.GetState(resident.Profile.Id);
            _meetings?.ValidateWorldBinding(world);
        }

        public NpcDecisionScheduler CreateReplacement(IEnumerable<NpcDefinition> profiles, INpcDecisionClient client,
            NpcDecisionSettings settings = null, NpcMeetingBoard meetings = null,
            Func<NpcDecisionRequest, NpcLocalObservation> observe = null, NpcSpeechMode speechMode = NpcSpeechMode.FreeText,
            Func<bool> canDecide = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NpcDecisionScheduler));
            if (_isTicking || _isReplacing)
                throw new InvalidOperationException("Complete the current decision operation before replacing decisions.");
            if (ActiveRequestCount != 0)
                throw new InvalidOperationException("Wait for every client task to complete before replacing decisions.");
            settings = settings ?? _settings;
            if (RequestsStarted > 0 && (settings.MaxRequestsPerMinute > _settings.MaxRequestsPerMinute
                || settings.MaxConcurrentRequests > _settings.MaxConcurrentRequests
                || settings.ResidentCooldownSeconds < _settings.ResidentCooldownSeconds
                || settings.RequestTimeoutSeconds > _settings.RequestTimeoutSeconds
                || settings.DecisionTimeoutSeconds > _settings.DecisionTimeoutSeconds
                || settings.MaxCallsPerDecision > _settings.MaxCallsPerDecision
                || settings.OpportunityLifetimeGameMinutes > _settings.OpportunityLifetimeGameMinutes))
                throw new InvalidOperationException("Decision limits can only stay unchanged or become stricter after the first client call.");
            _isReplacing = true;
            try
            {
                var candidate = new NpcDecisionScheduler(_world, profiles, client, settings, meetings,
                    observe, speechMode, canDecide);
                if (_disposed) throw new ObjectDisposedException(nameof(NpcDecisionScheduler));
                bool canReplace = _canDecide?.Invoke() != false;
                if (_disposed) throw new ObjectDisposedException(nameof(NpcDecisionScheduler));
                if (!canReplace)
                    throw new InvalidOperationException("Complete world recovery before replacing decisions.");
                candidate._history = _history;
                candidate._lastTick = _lastTick;
                foreach (var resident in candidate._residents)
                {
                    if (_history.ResidentStarts.TryGetValue(resident.Profile.Id, out var started)) resident.LastStarted = started;
                    var pending = _residents.FirstOrDefault(item => item.Profile.Id == resident.Profile.Id)?.Pending;
                    if (pending == null || pending.Social != null
                        || candidate.InvalidContext(pending, _world.GetState(resident.Profile.Id)) != null) continue;
                    resident.Pending = new NpcDecisionRequest(resident.Profile, pending.Self, pending.GameTotalMinutes,
                        pending.Triggers, pending.KnownLocationIds, settings.MaxCallsPerDecision, pending.PreviousResultCode,
                        speechMode: speechMode, activityDeadlineTotalMinutes: pending.ActivityDeadlineTotalMinutes);
                }
                Dispose();
                return candidate;
            }
            finally { _isReplacing = false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_isRestoring) throw new InvalidOperationException("Complete decision restoration before disposing the scheduler.");
            _disposed = true;
            foreach (var resident in _residents)
            {
                resident.Pending = null;
                resident.WaitingTurn = null;
                if (resident.Current != null) Finish(resident, "agent.decision_cancelled",
                    cancelRequest: resident.Request != null && !resident.Request.IsCompleted);
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
            foreach (var retired in _retiredRequests)
                _ = retired.Task.ContinueWith(completed =>
                {
                    _ = completed.Exception;
                    retired.Cancellation.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _retiredRequests.Clear();
        }

        public IReadOnlyList<NpcDecisionOutcome> Tick(double realSeconds)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NpcDecisionScheduler));
            if (_isTicking || _isReplacing)
                throw new InvalidOperationException("Complete the current decision operation before ticking decisions.");
            _isTicking = true;
            try { return TickCore(realSeconds); }
            finally { _isTicking = false; }
        }

        private IReadOnlyList<NpcDecisionOutcome> TickCore(double realSeconds)
        {
            if (double.IsNaN(realSeconds) || double.IsInfinity(realSeconds) || realSeconds < 0 || realSeconds < _lastTick)
                throw new ArgumentOutOfRangeException(nameof(realSeconds), "Decision time must be finite, nonnegative and monotonic.");
            _lastTick = realSeconds;
            _completed.Clear();
            foreach (var retired in _retiredRequests.Where(item => item.Task.IsCompleted).ToArray())
            {
                _ = retired.Task.Exception;
                retired.Cancellation.Dispose();
                _retiredRequests.Remove(retired);
            }
            while (_history.Starts.Count > 0 && _history.Starts.Peek() <= realSeconds - 60) _history.Starts.Dequeue();
            if (_canDecide?.Invoke() == false) return SuspendDecisions();
            _meetings?.Observe();
            int active = _retiredRequests.Count;
            foreach (var resident in _residents)
            {
                var state = _world.GetState(resident.Profile.Id);
                if (resident.Current != null)
                {
                    string invalid = InvalidContext(resident.Current, state);
                    if (invalid != null) Finish(resident, invalid, cancelRequest: true);
                }
                if (resident.Current != null && realSeconds >= resident.DecisionDeadlineSeconds)
                    Finish(resident, "agent.decision_timeout", cancelRequest: true);
                if (resident.Current != null && resident.Request == null && resident.Calls >= resident.Current.MaxCalls)
                    Finish(resident, "agent.decision_step_limit");
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
                if (_canDecide?.Invoke() == false) return SuspendDecisions();
                var events = _world.TakeEvents(resident.Profile.Id);
                state = _world.GetState(resident.Profile.Id);
                var social = _meetings?.GetContext(resident.Profile.Id);
                if (resident.WaitingTurn != null && !resident.WaitingTurn.Matches(state, social, _world.TotalMinutes))
                    resident.WaitingTurn = null;
                if (social != null)
                {
                    if (events.Count == 0) continue;
                }
                else if (_meetings?.GetCurrent(resident.Profile.Id) != null
                    || !events.Any(item => item.Kind != NpcAgentEventKind.ActivityAccepted && item.Kind != NpcAgentEventKind.MeetingChanged)
                    || state.ActiveActivity != null || state.Target.ExpectedActivity != NpcActivity.Resting) continue;
                if (IsDeliveredConversation(social))
                {
                    if (resident.Current?.Social?.MeetingId == social.MeetingId
                        && resident.Current.Social.Transcript.Count == social.Transcript.Count) continue;
                    resident.Pending = null;
                    if (resident.WaitingTurn == null) resident.WaitingTurn = new ConversationTurn(state, social, events);
                    continue;
                }
                resident.Pending = new NpcDecisionRequest(resident.Profile, state, _world.TotalMinutes, events,
                    _world.GetKnownLocationIds(resident.Profile.Id), _settings.MaxCallsPerDecision,
                    PreviousResultCode(resident), social, _speechMode,
                    social == null ? _world.NextScheduleChangeAfter(resident.Profile.Id, _world.TotalMinutes) : 0);
            }
            int first = _nextResident;
            for (int offset = 0; offset < _residents.Length; offset++)
            {
                if (active >= _settings.MaxConcurrentRequests || _history.Starts.Count >= _settings.MaxRequestsPerMinute) break;
                int index = (first + offset) % _residents.Length;
                var resident = _residents[index];
                if (resident.Request != null) continue;
                if (resident.Current == null)
                {
                    if (resident.WaitingTurn != null)
                    {
                        var state = _world.GetState(resident.Profile.Id);
                        var social = _meetings.GetContext(resident.Profile.Id);
                        var turn = resident.WaitingTurn;
                        resident.WaitingTurn = null;
                        if (!turn.Matches(state, social, _world.TotalMinutes)) continue;
                        resident.Pending = new NpcDecisionRequest(resident.Profile, state, _world.TotalMinutes, turn.Triggers,
                            _world.GetKnownLocationIds(resident.Profile.Id), _settings.MaxCallsPerDecision,
                            PreviousResultCode(resident), social, _speechMode);
                    }
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
                    resident.DecisionDeadlineSeconds = realSeconds + _settings.DecisionTimeoutSeconds;
                    _history.ResidentStarts[resident.Profile.Id] = realSeconds;
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
                else
                {
                    if (resident.RestoredLocationId != null)
                    {
                        var details = _world.InspectLocation(resident.Profile.Id, resident.RestoredLocationId);
                        if (!details.IsSuccess) { Finish(resident, details.ErrorCode); continue; }
                        resident.Current = new NpcDecisionRequest(resident.Current, details.Value, preserveStep: true);
                        resident.RestoredLocationId = null;
                    }
                    if (_observe != null)
                    {
                        string invalid;
                        if (resident.RefreshObservation)
                        {
                            invalid = TryReadObservation(resident.Current, out var observation) ? null : "agent.observation_unavailable";
                            if (invalid == null) resident.Current = new NpcDecisionRequest(resident.Current, observation);
                            resident.RefreshObservation = false;
                        }
                        else invalid = ObservationFailure(resident.Current);
                        if (invalid != null) { Finish(resident, invalid); continue; }
                    }
                }
                if (_canDecide?.Invoke() == false) return SuspendDecisions();
                string dispatchFailure = InvalidContext(resident.Current, _world.GetState(resident.Profile.Id));
                if (dispatchFailure != null) { Finish(resident, dispatchFailure); continue; }
                if (resident.Current.Social == null)
                    resident.Current = new NpcDecisionRequest(resident.Current, _world.TotalMinutes);
                _history.Starts.Enqueue(realSeconds);
                _history.RequestsStarted++;
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
                if (_canDecide?.Invoke() == false) return SuspendDecisions();
            }
            return Array.AsReadOnly(_completed.ToArray());
        }

        private IReadOnlyList<NpcDecisionOutcome> SuspendDecisions()
        {
            foreach (var resident in _residents)
            {
                resident.Pending = null;
                resident.WaitingTurn = null;
                if (resident.Current != null) Finish(resident, "agent.world_not_ready", cancelRequest: true);
                if (resident.Request == null || !resident.Request.IsCompleted) continue;
                _ = resident.Request.Exception;
                resident.Request = null;
                resident.Cancellation.Dispose();
                resident.Cancellation = null;
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
            NpcLocalObservation executionObservation = null;
            bool invitationReply = context.Social?.Kind == NpcSocialContextKind.Invitation
                && (reply.Kind == NpcDecisionKind.AcceptInvitation || reply.Kind == NpcDecisionKind.DeclineInvitation);
            string observationFailure = _observe == null ? null : ObservationFailure(context, invitationReply, out executionObservation);
            if (_canDecide?.Invoke() == false)
            {
                Finish(resident, "agent.world_not_ready", cancelRequest: true);
                return;
            }
            if (observationFailure != null)
            {
                Finish(resident, observationFailure, reply: reply, executionObservation: executionObservation);
                return;
            }
            string contextFailure = InvalidContext(context, _world.GetState(resident.Profile.Id));
            if (contextFailure != null)
            {
                Finish(resident, contextFailure, reply: reply, executionObservation: executionObservation);
                return;
            }
            if (!context.AllowedOperations.Contains(reply.Operation))
            {
                Finish(resident, string.IsNullOrEmpty(reply.Operation) ? "agent.response_invalid" : "agent.operation_unavailable", reply: reply, executionObservation: executionObservation);
                return;
            }
            if (context.Social != null && reply.Kind != NpcDecisionKind.Wait)
            {
                if (reply.Kind == NpcDecisionKind.Invite)
                {
                    if (reply.PlanId != context.Social.PlanId) { Finish(resident, "meeting.plan_unknown", reply: reply, executionObservation: executionObservation); return; }
                    var invited = _meetings.Invite(context.Self, reply.PlanId);
                    Finish(resident, invited.IsSuccess ? "meeting.invited" : invited.ErrorCode, reply: reply, executionObservation: executionObservation);
                    return;
                }
                if (reply.MeetingId != context.Social.MeetingId) { Finish(resident, "meeting.unknown", reply: reply, executionObservation: executionObservation); return; }
                if (reply.Kind == NpcDecisionKind.Speak &&
                    (context.SpeechMode == NpcSpeechMode.FreeText ? reply.SpeechFrame != null : reply.Text != null))
                {
                    Finish(resident, "speech.mode_mismatch", reply: reply, executionObservation: executionObservation);
                    return;
                }
                string speech = reply.Text;
                if (reply.Kind == NpcDecisionKind.Speak && context.SpeechMode == NpcSpeechMode.StructuredFacts
                    && !NpcFactSpeech.TryRender(context.Observation, reply.SpeechFrame, out speech, out var speechError))
                {
                    Finish(resident, speechError, reply: reply, executionObservation: executionObservation);
                    return;
                }
                var result = reply.Kind == NpcDecisionKind.Speak ? _meetings.Speak(context.Self, reply.MeetingId, speech)
                    : reply.Kind == NpcDecisionKind.Deliver ? _meetings.Deliver(context.Self, reply.MeetingId)
                    : reply.Kind == NpcDecisionKind.CancelExchange ? _meetings.CancelExchange(context.Self, reply.MeetingId)
                    : reply.Kind == NpcDecisionKind.EndConversation ? _meetings.EndConversation(context.Self, reply.MeetingId)
                    : _meetings.Respond(context.Self, reply.MeetingId, reply.Kind == NpcDecisionKind.AcceptInvitation);
                Finish(resident, result.IsSuccess ? "meeting." + reply.Operation : result.ErrorCode, reply: reply, executionObservation: executionObservation);
                return;
            }
            if (reply.Kind == NpcDecisionKind.Wait)
            {
                Finish(resident, "agent.decision_wait", reply: reply, executionObservation: executionObservation);
                return;
            }
            if (reply.Kind == NpcDecisionKind.InspectLocation)
            {
                if (resident.Calls >= _settings.MaxCallsPerDecision)
                {
                    Finish(resident, "agent.decision_step_limit", reply: reply, executionObservation: executionObservation);
                    return;
                }
                var details = _world.InspectLocation(resident.Profile.Id, reply.LocationId);
                if (details.IsSuccess)
                {
                    resident.Current = new NpcDecisionRequest(context, details.Value);
                    return;
                }
                Finish(resident, details.ErrorCode, reply: reply, executionObservation: executionObservation);
                return;
            }
            if (reply.Kind == NpcDecisionKind.Visit)
            {
                if (double.IsNaN(reply.DurationGameMinutes) || double.IsInfinity(reply.DurationGameMinutes)
                    || reply.DurationGameMinutes <= 0 || reply.DurationGameMinutes > context.MaxActivityDurationGameMinutes)
                {
                    HandleCandidateFailure(resident, new NpcCandidateException("candidate.duration_invalid"));
                    return;
                }
                if (!context.KnownLocationIds.Contains(reply.LocationId))
                {
                    Finish(resident, "agent.location_unknown", reply: reply, executionObservation: executionObservation);
                    return;
                }
                var result = _world.SubmitActivity(new NpcActivityRequest(context.NpcId, context.Self.WorldRunId, context.Self.Revision,
                    reply.LocationId, reply.Activity, Math.Min(_world.TotalMinutes + reply.DurationGameMinutes,
                        context.ActivityDeadlineTotalMinutes)));
                Finish(resident, result.IsSuccess ? "agent.activity_accepted" : result.ErrorCode, reply: reply, executionObservation: executionObservation);
                return;
            }
            Finish(resident, "agent.response_invalid", reply: reply, executionObservation: executionObservation);
        }

        private string InvalidContext(NpcDecisionRequest context, NpcAgentSnapshot state)
        {
            if (context.Self.WorldRunId != state.WorldRunId) return "agent.world_stale";
            if (context.Self.Revision != state.Revision) return "agent.decision_stale";
            if (_world.TotalMinutes >= context.GameTotalMinutes + _settings.OpportunityLifetimeGameMinutes
                || (context.Social == null && _world.TotalMinutes >= context.ActivityDeadlineTotalMinutes))
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
            => ObservationFailure(context, false, out _);

        private string ObservationFailure(NpcDecisionRequest context, bool allowPositionChange, out NpcLocalObservation current)
        {
            if (!TryReadObservation(context, out current)) return "agent.observation_unavailable";
            return context.Observation != null && context.Observation.HasSameFacts(current, allowPositionChange) ? null : "agent.observation_stale";
        }

        private void HandleCandidateFailure(Resident resident, NpcCandidateException exception)
        {
            resident.CandidateErrorCodes.Add(exception.Code);
            if (exception.CanCorrect && resident.CandidateErrorCodes.Count == 1 && resident.Calls < _settings.MaxCallsPerDecision)
                resident.Current = new NpcDecisionRequest(resident.Current, resident.Current.LocationDetails, exception.Code);
            else Finish(resident, "agent.response_invalid");
        }

        private void Finish(Resident resident, string code, bool cancelRequest = false, NpcDecisionReply reply = null,
            NpcLocalObservation executionObservation = null)
        {
            var outcome = new NpcDecisionOutcome(resident.Current, code, resident.Calls, resident.LastStarted, _lastTick, reply,
                resident.CandidateErrorCodes, executionObservation);
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
