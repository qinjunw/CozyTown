using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using CozyTown.Runtime.Npc;

namespace CozyTown.Runtime.NpcAgents
{
    [DataContract]
    public sealed class NpcDecisionSchedulerSnapshot
    {
        [DataMember(Name = "gameTotalMinutes", IsRequired = true)] private double _gameTotalMinutes;
        [DataMember(Name = "residents", IsRequired = true)] private NpcDecisionResidentSnapshot[] _residents;
        [DataMember(Name = "settings", IsRequired = true)] private NpcDecisionSettingsSnapshot _settings;
        [DataMember(Name = "speechMode", IsRequired = true)] private NpcSpeechMode _speechMode;
        [DataMember(Name = "nextResidentId", IsRequired = true)] private string _nextResidentId;
        [DataMember(Name = "clientConfiguration", IsRequired = true)] private string _clientConfiguration;

        public NpcDecisionSchedulerSnapshot(double gameTotalMinutes, IEnumerable<NpcDecisionResidentSnapshot> residents,
            NpcDecisionSettingsSnapshot settings, NpcSpeechMode speechMode, string nextResidentId, string clientConfiguration = null)
        {
            _gameTotalMinutes = gameTotalMinutes;
            _residents = residents?.ToArray();
            _settings = settings;
            _speechMode = speechMode;
            _nextResidentId = nextResidentId;
            _clientConfiguration = clientConfiguration;
        }

        public double GameTotalMinutes => _gameTotalMinutes;
        public IReadOnlyList<NpcDecisionResidentSnapshot> Residents => _residents == null ? null : Array.AsReadOnly(_residents);
        public NpcDecisionSettingsSnapshot Settings => _settings;
        public NpcSpeechMode SpeechMode => _speechMode;
        public string NextResidentId => _nextResidentId;
        public string ClientConfiguration => _clientConfiguration;
    }

    [DataContract]
    public sealed class NpcDecisionSettingsSnapshot
    {
        [DataMember(Name = "maxRequestsPerMinute", IsRequired = true)] private int _maxRequestsPerMinute;
        [DataMember(Name = "maxConcurrentRequests", IsRequired = true)] private int _maxConcurrentRequests;
        [DataMember(Name = "residentCooldownSeconds", IsRequired = true)] private double _residentCooldownSeconds;
        [DataMember(Name = "requestTimeoutSeconds", IsRequired = true)] private double _requestTimeoutSeconds;
        [DataMember(Name = "decisionTimeoutSeconds", IsRequired = true)] private double _decisionTimeoutSeconds;
        [DataMember(Name = "maxCallsPerDecision", IsRequired = true)] private int _maxCallsPerDecision;
        [DataMember(Name = "opportunityLifetimeGameMinutes", IsRequired = true)] private double _opportunityLifetimeGameMinutes;

        public NpcDecisionSettingsSnapshot(NpcDecisionSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _maxRequestsPerMinute = settings.MaxRequestsPerMinute;
            _maxConcurrentRequests = settings.MaxConcurrentRequests;
            _residentCooldownSeconds = settings.ResidentCooldownSeconds;
            _requestTimeoutSeconds = settings.RequestTimeoutSeconds;
            _decisionTimeoutSeconds = settings.DecisionTimeoutSeconds;
            _maxCallsPerDecision = settings.MaxCallsPerDecision;
            _opportunityLifetimeGameMinutes = settings.OpportunityLifetimeGameMinutes;
        }

        public int MaxRequestsPerMinute => _maxRequestsPerMinute;
        public int MaxConcurrentRequests => _maxConcurrentRequests;
        public double ResidentCooldownSeconds => _residentCooldownSeconds;
        public double RequestTimeoutSeconds => _requestTimeoutSeconds;
        public double DecisionTimeoutSeconds => _decisionTimeoutSeconds;
        public int MaxCallsPerDecision => _maxCallsPerDecision;
        public double OpportunityLifetimeGameMinutes => _opportunityLifetimeGameMinutes;

        internal bool Matches(NpcDecisionSettings settings)
            => _maxRequestsPerMinute == settings.MaxRequestsPerMinute
                && _maxConcurrentRequests == settings.MaxConcurrentRequests
                && _residentCooldownSeconds == settings.ResidentCooldownSeconds
                && _requestTimeoutSeconds == settings.RequestTimeoutSeconds
                && _decisionTimeoutSeconds == settings.DecisionTimeoutSeconds
                && _maxCallsPerDecision == settings.MaxCallsPerDecision
                && _opportunityLifetimeGameMinutes == settings.OpportunityLifetimeGameMinutes;
    }

    [DataContract]
    public sealed class NpcDecisionResidentSnapshot
    {
        [DataMember(Name = "npcId", IsRequired = true)] private string _npcId;
        [DataMember(Name = "displayName", IsRequired = true)] private string _displayName;
        [DataMember(Name = "persona", IsRequired = true)] private string _persona;
        [DataMember(Name = "fallbackDialogue", IsRequired = true)] private string _fallbackDialogue;
        [DataMember(Name = "remainingCooldownSeconds", IsRequired = true)] private double _remainingCooldownSeconds;
        [DataMember(Name = "lastResultCode", IsRequired = true)] private string _lastResultCode;
        [DataMember(Name = "current", IsRequired = true)] private NpcDecisionProgressSnapshot _current;
        [DataMember(Name = "pending", IsRequired = true)] private NpcDecisionProgressSnapshot _pending;
        [DataMember(Name = "waitingTurn", IsRequired = true)] private NpcWaitingTurnSnapshot _waitingTurn;

        public NpcDecisionResidentSnapshot(NpcDefinition profile, double remainingCooldownSeconds,
            string lastResultCode, NpcDecisionProgressSnapshot current, NpcDecisionProgressSnapshot pending = null,
            NpcWaitingTurnSnapshot waitingTurn = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            _npcId = profile.Id;
            _displayName = profile.DisplayName;
            _persona = profile.Persona;
            _fallbackDialogue = profile.FallbackDialogue;
            _remainingCooldownSeconds = remainingCooldownSeconds;
            _lastResultCode = lastResultCode;
            _current = current;
            _pending = pending;
            _waitingTurn = waitingTurn;
        }

        public string NpcId => _npcId;
        public string DisplayName => _displayName;
        public string Persona => _persona;
        public string FallbackDialogue => _fallbackDialogue;
        public double RemainingCooldownSeconds => _remainingCooldownSeconds;
        public string LastResultCode => _lastResultCode;
        public NpcDecisionProgressSnapshot Current => _current;
        public NpcDecisionProgressSnapshot Pending => _pending;
        public NpcWaitingTurnSnapshot WaitingTurn => _waitingTurn;

        internal bool Matches(NpcDefinition profile)
            => _npcId == profile.Id && _displayName == profile.DisplayName
                && _persona == profile.Persona && _fallbackDialogue == profile.FallbackDialogue;
    }

    [DataContract]
    public sealed class NpcDecisionProgressSnapshot
    {
        [DataMember(Name = "expectedRevision", IsRequired = true)] private long _expectedRevision;
        [DataMember(Name = "gameTotalMinutes", IsRequired = true)] private double _gameTotalMinutes;
        [DataMember(Name = "activityDeadlineTotalMinutes", IsRequired = true)] private double _activityDeadlineTotalMinutes;
        [DataMember(Name = "triggers", IsRequired = true)] private NpcAgentEvent[] _triggers;
        [DataMember(Name = "maxCalls", IsRequired = true)] private int _maxCalls;
        [DataMember(Name = "nextStep", IsRequired = true)] private int _nextStep;
        [DataMember(Name = "calls", IsRequired = true)] private int _calls;
        [DataMember(Name = "remainingDecisionSeconds", IsRequired = true)] private double _remainingDecisionSeconds;
        [DataMember(Name = "previousResultCode", IsRequired = true)] private string _previousResultCode;
        [DataMember(Name = "hasLocationDetails", IsRequired = true)] private bool _hasLocationDetails;
        [DataMember(Name = "locationId", IsRequired = true)] private string _locationId;
        [DataMember(Name = "candidateErrorCodes", IsRequired = true)] private string[] _candidateErrorCodes;
        [DataMember(Name = "candidateErrorCode", IsRequired = true)] private string _candidateErrorCode;
        [DataMember(Name = "socialKind", IsRequired = true)] private NpcSocialContextKind? _socialKind;
        [DataMember(Name = "planId", IsRequired = true)] private string _planId;
        [DataMember(Name = "meetingId", IsRequired = true)] private Guid _meetingId;
        [DataMember(Name = "spokenLines", IsRequired = true)] private int _spokenLines;

        public NpcDecisionProgressSnapshot(long expectedRevision, double gameTotalMinutes, double activityDeadlineTotalMinutes,
            IEnumerable<NpcAgentEvent> triggers, int maxCalls, int nextStep, int calls,
            double remainingDecisionSeconds, string previousResultCode, bool hasLocationDetails = false,
            string locationId = null, IEnumerable<string> candidateErrorCodes = null, string candidateErrorCode = null,
            NpcSocialContextKind? socialKind = null, string planId = null, Guid meetingId = default, int spokenLines = 0)
        {
            _expectedRevision = expectedRevision;
            _gameTotalMinutes = gameTotalMinutes;
            _activityDeadlineTotalMinutes = activityDeadlineTotalMinutes;
            _triggers = triggers?.ToArray();
            _maxCalls = maxCalls;
            _nextStep = nextStep;
            _calls = calls;
            _remainingDecisionSeconds = remainingDecisionSeconds;
            _previousResultCode = previousResultCode;
            _hasLocationDetails = hasLocationDetails;
            _locationId = locationId;
            _candidateErrorCodes = candidateErrorCodes?.ToArray() ?? Array.Empty<string>();
            _candidateErrorCode = candidateErrorCode;
            _socialKind = socialKind;
            _planId = planId;
            _meetingId = meetingId;
            _spokenLines = spokenLines;
        }

        public long ExpectedRevision => _expectedRevision;
        public double GameTotalMinutes => _gameTotalMinutes;
        public double ActivityDeadlineTotalMinutes => _activityDeadlineTotalMinutes;
        public IReadOnlyList<NpcAgentEvent> Triggers => _triggers == null ? null : Array.AsReadOnly(_triggers);
        public int MaxCalls => _maxCalls;
        public int NextStep => _nextStep;
        public int Calls => _calls;
        public double RemainingDecisionSeconds => _remainingDecisionSeconds;
        public string PreviousResultCode => _previousResultCode;
        public bool HasLocationDetails => _hasLocationDetails;
        public string LocationId => _locationId;
        public IReadOnlyList<string> CandidateErrorCodes => _candidateErrorCodes == null ? null : Array.AsReadOnly(_candidateErrorCodes);
        public string CandidateErrorCode => _candidateErrorCode;
        public NpcSocialContextKind? SocialKind => _socialKind;
        public string PlanId => _planId;
        public Guid MeetingId => _meetingId;
        public int SpokenLines => _spokenLines;
    }

    [DataContract]
    public sealed class NpcWaitingTurnSnapshot
    {
        [DataMember(Name = "meetingId", IsRequired = true)] private Guid _meetingId;
        [DataMember(Name = "speakerId", IsRequired = true)] private string _speakerId;
        [DataMember(Name = "spokenLines", IsRequired = true)] private int _spokenLines;
        [DataMember(Name = "triggers", IsRequired = true)] private NpcAgentEvent[] _triggers;

        public NpcWaitingTurnSnapshot(Guid meetingId, string speakerId, int spokenLines, IEnumerable<NpcAgentEvent> triggers)
        {
            _meetingId = meetingId;
            _speakerId = speakerId;
            _spokenLines = spokenLines;
            _triggers = triggers?.ToArray();
        }

        public Guid MeetingId => _meetingId;
        public string SpeakerId => _speakerId;
        public int SpokenLines => _spokenLines;
        public IReadOnlyList<NpcAgentEvent> Triggers => _triggers == null ? null : Array.AsReadOnly(_triggers);
    }
}
