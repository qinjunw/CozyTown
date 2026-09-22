using System.Runtime.Serialization;
using System;
using CozyTown.Runtime.Economy;

namespace CozyTown.Runtime.NpcAgents
{
    [DataContract]
    public sealed class NpcMeetingPlan
    {
        public NpcMeetingPlan(string id, string initiatorId, string partnerId, string placeId,
            string initiatorLocationId, string partnerLocationId, int inviteStartMinute,
            int meetingStartMinute, int inviteEndMinute, int durationGameMinutes = 150, int maxTurns = 4,
            CharacterTradeTerms resourceTerms = null)
        {
            foreach (string value in new[] { id, initiatorId, partnerId, placeId, initiatorLocationId, partnerLocationId })
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Meeting plans require named residents, a place and two locations.");
            if (initiatorId == partnerId || initiatorLocationId == partnerLocationId)
                throw new ArgumentException("A meeting requires two residents and two distinct standing locations.");
            if (inviteStartMinute < 0 || inviteStartMinute > meetingStartMinute || meetingStartMinute >= inviteEndMinute
                || inviteEndMinute >= 1440 || durationGameMinutes < 1 || durationGameMinutes > 1440 || maxTurns < 2 || maxTurns > 8)
                throw new ArgumentOutOfRangeException(nameof(inviteStartMinute), "Meeting windows, duration and turn count must be bounded and ordered.");
            Id = id;
            InitiatorId = initiatorId;
            PartnerId = partnerId;
            PlaceId = placeId;
            InitiatorLocationId = initiatorLocationId;
            PartnerLocationId = partnerLocationId;
            InviteStartMinute = inviteStartMinute;
            MeetingStartMinute = meetingStartMinute;
            InviteEndMinute = inviteEndMinute;
            DurationGameMinutes = durationGameMinutes;
            MaxTurns = maxTurns;
            if (resourceTerms != null && (resourceTerms.BuyerId != initiatorId || resourceTerms.SellerId != partnerId))
                throw new ArgumentException("A resource meeting must be initiated by the buyer and accepted by the seller.");
            ResourceTerms = resourceTerms;
        }

        [field: DataMember(Name = "id", IsRequired = true)]
        public string Id { get; }
        [field: DataMember(Name = "initiatorId", IsRequired = true)]
        public string InitiatorId { get; }
        [field: DataMember(Name = "partnerId", IsRequired = true)]
        public string PartnerId { get; }
        [field: DataMember(Name = "placeId", IsRequired = true)]
        public string PlaceId { get; }
        [field: DataMember(Name = "initiatorLocationId", IsRequired = true)]
        public string InitiatorLocationId { get; }
        [field: DataMember(Name = "partnerLocationId", IsRequired = true)]
        public string PartnerLocationId { get; }
        [field: DataMember(Name = "inviteStartMinute", IsRequired = true)]
        public int InviteStartMinute { get; }
        [field: DataMember(Name = "meetingStartMinute", IsRequired = true)]
        public int MeetingStartMinute { get; }
        [field: DataMember(Name = "inviteEndMinute", IsRequired = true)]
        public int InviteEndMinute { get; }
        [field: DataMember(Name = "durationGameMinutes", IsRequired = true)]
        public int DurationGameMinutes { get; }
        [field: DataMember(Name = "maxTurns", IsRequired = true)]
        public int MaxTurns { get; }
        [field: DataMember(Name = "resourceTerms", IsRequired = true)]
        public CharacterTradeTerms ResourceTerms { get; }
    }
}
