using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using CozyTown.Runtime.Economy;

namespace CozyTown.Runtime.NpcAgents
{
    public enum NpcMeetingState { Invited, Scheduled, Travelling, Talking, Completed, Declined, Cancelled, Expired }
    public enum NpcMeetingPresence { Travelling, Arrived, Blocked, Busy }

    [DataContract]
    public sealed class NpcMeetingSnapshot
    {
        [DataMember(Name = "transcript", IsRequired = true)] private readonly NpcConversationLine[] _transcript;
        internal NpcMeetingSnapshot(Guid id, string initiatorId, string partnerId, NpcMeetingState state, string speakerId = null,
            IEnumerable<NpcConversationLine> transcript = null, CharacterTradeTerms resourceTerms = null, string deliveryResultCode = null)
        {
            Id = id;
            InitiatorId = initiatorId;
            PartnerId = partnerId;
            State = state;
            SpeakerId = speakerId;
            _transcript = transcript?.ToArray() ?? Array.Empty<NpcConversationLine>();
            ResourceTerms = resourceTerms;
            DeliveryResultCode = deliveryResultCode;
        }

        [field: DataMember(Name = "id", IsRequired = true)]
        public Guid Id { get; }
        [field: DataMember(Name = "initiatorId", IsRequired = true)]
        public string InitiatorId { get; }
        [field: DataMember(Name = "partnerId", IsRequired = true)]
        public string PartnerId { get; }
        [field: DataMember(Name = "state", IsRequired = true)]
        public NpcMeetingState State { get; }
        [field: DataMember(Name = "speakerId", IsRequired = true)]
        public string SpeakerId { get; }
        public IReadOnlyList<NpcConversationLine> Transcript => _transcript == null ? null : Array.AsReadOnly(_transcript);
        [field: DataMember(Name = "resourceTerms", IsRequired = true)]
        public CharacterTradeTerms ResourceTerms { get; }
        [field: DataMember(Name = "deliveryResultCode", IsRequired = true)]
        public string DeliveryResultCode { get; }
    }
}
