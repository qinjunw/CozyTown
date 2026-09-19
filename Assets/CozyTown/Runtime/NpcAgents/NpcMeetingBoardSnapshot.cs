using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace CozyTown.Runtime.NpcAgents
{
    [DataContract]
    public sealed class NpcMeetingBoardSnapshot
    {
        [DataMember(Name = "plans", IsRequired = true)] private readonly NpcMeetingPlan[] _plans;
        [DataMember(Name = "meetings", IsRequired = true)] private readonly NpcMeetingStateSnapshot[] _meetings;
        [DataMember(Name = "latestResults", IsRequired = true)] private readonly NpcMeetingSnapshot[] _latestResults;
        [DataMember(Name = "latestByResident", IsRequired = true)] private readonly NpcLatestMeetingSnapshot[] _latestByResident;
        [DataMember(Name = "offeredDays", IsRequired = true)] private readonly NpcMeetingDaySnapshot[] _offeredDays;
        [DataMember(Name = "opportunities", IsRequired = true)] private readonly NpcMeetingOpportunitySnapshot[] _opportunities;
        [DataMember(Name = "memories", IsRequired = true)] private readonly NpcResidentMemoriesSnapshot[] _memories;

        public NpcMeetingBoardSnapshot(IEnumerable<NpcMeetingPlan> plans, IEnumerable<NpcMeetingStateSnapshot> meetings,
            IEnumerable<NpcMeetingSnapshot> latestResults, IEnumerable<NpcLatestMeetingSnapshot> latestByResident,
            IEnumerable<NpcMeetingDaySnapshot> offeredDays, IEnumerable<NpcMeetingOpportunitySnapshot> opportunities,
            IEnumerable<NpcResidentMemoriesSnapshot> memories)
        {
            _plans = plans?.ToArray();
            _meetings = meetings?.ToArray();
            _latestResults = latestResults?.ToArray();
            _latestByResident = latestByResident?.ToArray();
            _offeredDays = offeredDays?.ToArray();
            _opportunities = opportunities?.ToArray();
            _memories = memories?.ToArray();
        }

        public IReadOnlyList<NpcMeetingPlan> Plans => _plans == null ? null : Array.AsReadOnly(_plans);
        public IReadOnlyList<NpcMeetingStateSnapshot> Meetings => _meetings == null ? null : Array.AsReadOnly(_meetings);
        public IReadOnlyList<NpcMeetingSnapshot> LatestResults => _latestResults == null ? null : Array.AsReadOnly(_latestResults);
        public IReadOnlyList<NpcLatestMeetingSnapshot> LatestByResident => _latestByResident == null ? null : Array.AsReadOnly(_latestByResident);
        public IReadOnlyList<NpcMeetingDaySnapshot> OfferedDays => _offeredDays == null ? null : Array.AsReadOnly(_offeredDays);
        public IReadOnlyList<NpcMeetingOpportunitySnapshot> Opportunities => _opportunities == null ? null : Array.AsReadOnly(_opportunities);
        public IReadOnlyList<NpcResidentMemoriesSnapshot> Memories => _memories == null ? null : Array.AsReadOnly(_memories);
    }

    [DataContract]
    public sealed class NpcMeetingStateSnapshot
    {
        [DataMember(Name = "id", IsRequired = true)] private readonly Guid _id;
        [DataMember(Name = "planId", IsRequired = true)] private readonly string _planId;
        [DataMember(Name = "state", IsRequired = true)] private readonly NpcMeetingState _state;
        [DataMember(Name = "dayStart", IsRequired = true)] private readonly double _dayStart;
        [DataMember(Name = "startsAt", IsRequired = true)] private readonly double _startsAt;
        [DataMember(Name = "endsAt", IsRequired = true)] private readonly double _endsAt;
        [DataMember(Name = "initiatorActivityId", IsRequired = true)] private readonly Guid _initiatorActivityId;
        [DataMember(Name = "partnerActivityId", IsRequired = true)] private readonly Guid _partnerActivityId;
        [DataMember(Name = "speakerId", IsRequired = true)] private readonly string _speakerId;
        [DataMember(Name = "deliveryResultCode", IsRequired = true)] private readonly string _deliveryResultCode;
        [DataMember(Name = "transcript", IsRequired = true)] private readonly NpcConversationLine[] _transcript;

        public NpcMeetingStateSnapshot(Guid id, string planId, NpcMeetingState state, double dayStart, double startsAt,
            double endsAt, Guid initiatorActivityId, Guid partnerActivityId, string speakerId, string deliveryResultCode,
            IEnumerable<NpcConversationLine> transcript)
        {
            _id = id;
            _planId = planId;
            _state = state;
            _dayStart = dayStart;
            _startsAt = startsAt;
            _endsAt = endsAt;
            _initiatorActivityId = initiatorActivityId;
            _partnerActivityId = partnerActivityId;
            _speakerId = speakerId;
            _deliveryResultCode = deliveryResultCode;
            _transcript = transcript?.ToArray();
        }

        public Guid Id => _id;
        public string PlanId => _planId;
        public NpcMeetingState State => _state;
        public double DayStart => _dayStart;
        public double StartsAt => _startsAt;
        public double EndsAt => _endsAt;
        public Guid InitiatorActivityId => _initiatorActivityId;
        public Guid PartnerActivityId => _partnerActivityId;
        public string SpeakerId => _speakerId;
        public string DeliveryResultCode => _deliveryResultCode;
        public IReadOnlyList<NpcConversationLine> Transcript => _transcript == null ? null : Array.AsReadOnly(_transcript);
    }

    [DataContract]
    public sealed class NpcLatestMeetingSnapshot
    {
        [DataMember(Name = "npcId", IsRequired = true)] private readonly string _npcId;
        [DataMember(Name = "meetingId", IsRequired = true)] private readonly Guid _meetingId;

        public NpcLatestMeetingSnapshot(string npcId, Guid meetingId)
        {
            _npcId = npcId;
            _meetingId = meetingId;
        }

        public string NpcId => _npcId;
        public Guid MeetingId => _meetingId;
    }

    [DataContract]
    public sealed class NpcMeetingDaySnapshot
    {
        [DataMember(Name = "planId", IsRequired = true)] private readonly string _planId;
        [DataMember(Name = "dayStart", IsRequired = true)] private readonly double _dayStart;

        public NpcMeetingDaySnapshot(string planId, double dayStart)
        {
            _planId = planId;
            _dayStart = dayStart;
        }

        public string PlanId => _planId;
        public double DayStart => _dayStart;
    }

    [DataContract]
    public sealed class NpcMeetingOpportunitySnapshot
    {
        [DataMember(Name = "npcId", IsRequired = true)] private readonly string _npcId;
        [DataMember(Name = "planId", IsRequired = true)] private readonly string _planId;

        public NpcMeetingOpportunitySnapshot(string npcId, string planId)
        {
            _npcId = npcId;
            _planId = planId;
        }

        public string NpcId => _npcId;
        public string PlanId => _planId;
    }

    [DataContract]
    public sealed class NpcResidentMemoriesSnapshot
    {
        [DataMember(Name = "npcId", IsRequired = true)] private readonly string _npcId;
        [DataMember(Name = "memories", IsRequired = true)] private readonly NpcMeetingMemory[] _memories;

        public NpcResidentMemoriesSnapshot(string npcId, IEnumerable<NpcMeetingMemory> memories)
        {
            _npcId = npcId;
            _memories = memories?.ToArray();
        }

        public string NpcId => _npcId;
        public IReadOnlyList<NpcMeetingMemory> Memories => _memories == null ? null : Array.AsReadOnly(_memories);
    }

}
