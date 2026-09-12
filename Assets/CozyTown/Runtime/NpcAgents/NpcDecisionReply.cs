using CozyTown.Runtime.NpcLife;

namespace CozyTown.Runtime.NpcAgents
{
    public enum NpcDecisionKind
    {
        Wait,
        InspectLocation,
        Visit
    }

    public sealed class NpcDecisionReply
    {
        public NpcDecisionReply(NpcDecisionKind kind, string locationId = null,
            NpcActivity activity = NpcActivity.Resting, double durationGameMinutes = 0)
        {
            Kind = kind;
            LocationId = locationId;
            Activity = activity;
            DurationGameMinutes = durationGameMinutes;
        }

        public NpcDecisionKind Kind { get; }
        public string LocationId { get; }
        public NpcActivity Activity { get; }
        public double DurationGameMinutes { get; }
    }
}
