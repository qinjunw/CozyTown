using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Economy;

namespace CozyTown.Runtime.NpcAgents
{
    public enum NpcMeetingState { Invited, Scheduled, Travelling, Talking, Completed, Declined, Cancelled, Expired }
    public enum NpcMeetingPresence { Travelling, Arrived, Blocked, Busy }

    public sealed class NpcMeetingSnapshot
    {
        internal NpcMeetingSnapshot(Guid id, string initiatorId, string partnerId, NpcMeetingState state, string speakerId = null,
            IEnumerable<NpcConversationLine> transcript = null, CharacterTradeTerms resourceTerms = null, string deliveryResultCode = null)
        {
            Id = id;
            InitiatorId = initiatorId;
            PartnerId = partnerId;
            State = state;
            SpeakerId = speakerId;
            Transcript = Array.AsReadOnly(transcript?.ToArray() ?? Array.Empty<NpcConversationLine>());
            ResourceTerms = resourceTerms;
            DeliveryResultCode = deliveryResultCode;
        }

        public Guid Id { get; }
        public string InitiatorId { get; }
        public string PartnerId { get; }
        public NpcMeetingState State { get; }
        public string SpeakerId { get; }
        public IReadOnlyList<NpcConversationLine> Transcript { get; }
        public CharacterTradeTerms ResourceTerms { get; }
        public string DeliveryResultCode { get; }
    }
}
