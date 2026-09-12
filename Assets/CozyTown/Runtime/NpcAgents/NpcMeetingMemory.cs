using System;

namespace CozyTown.Runtime.NpcAgents
{
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

        public Guid MeetingId { get; }
        public string Kind { get; }
        public string PartnerId { get; }
        public double TotalMinutes { get; }
        public string SpeakerId { get; }
        public string Text { get; }
    }
}
