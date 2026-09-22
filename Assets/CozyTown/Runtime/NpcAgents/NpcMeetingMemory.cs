using System.Runtime.Serialization;
using System;

namespace CozyTown.Runtime.NpcAgents
{
    [DataContract]
    public sealed class NpcMeetingMemory
    {
        internal NpcMeetingMemory(Guid meetingId, string kind, string partnerId, double totalMinutes,
            string speakerId = null, string text = null)
        {
            MeetingId = meetingId;
            Kind = kind;
            PartnerId = partnerId;
            TotalMinutes = totalMinutes;
            SpeakerId = speakerId;
            Text = text;
        }

        [field: DataMember(Name = "meetingId", IsRequired = true)]
        public Guid MeetingId { get; }
        [field: DataMember(Name = "kind", IsRequired = true)]
        public string Kind { get; }
        [field: DataMember(Name = "partnerId", IsRequired = true)]
        public string PartnerId { get; }
        [field: DataMember(Name = "totalMinutes", IsRequired = true)]
        public double TotalMinutes { get; }
        [field: DataMember(Name = "speakerId", IsRequired = true)]
        public string SpeakerId { get; }
        [field: DataMember(Name = "text", IsRequired = true)]
        public string Text { get; }
    }
}
