namespace CozyTown.Runtime.NpcAgents
{
    public enum NpcAgentEventKind
    {
        WorldRebuilt,
        ScheduleChanged,
        DayChanged,
        ActivityAccepted,
        ActivityCancelled,
        ActivityExpired
    }

    public sealed class NpcAgentEvent
    {
        public NpcAgentEvent(NpcAgentEventKind kind, double totalMinutes)
        {
            Kind = kind;
            TotalMinutes = totalMinutes;
        }

        public NpcAgentEventKind Kind { get; }
        public double TotalMinutes { get; }
    }
}
