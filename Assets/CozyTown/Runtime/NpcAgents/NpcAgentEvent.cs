using System.Runtime.Serialization;

namespace CozyTown.Runtime.NpcAgents
{
    public enum NpcAgentEventKind
    {
        WorldRebuilt,
        ScheduleChanged,
        DayChanged,
        ActivityAccepted,
        ActivityCancelled,
        ActivityExpired,
        SocialOpportunity,
        MeetingChanged
    }

    [DataContract]
    public sealed class NpcAgentEvent
    {
        public NpcAgentEvent(NpcAgentEventKind kind, double totalMinutes)
        {
            Kind = kind;
            TotalMinutes = totalMinutes;
        }

        [field: DataMember(Name = "kind", IsRequired = true)]
        public NpcAgentEventKind Kind { get; }
        [field: DataMember(Name = "totalMinutes", IsRequired = true)]
        public double TotalMinutes { get; }
    }
}
