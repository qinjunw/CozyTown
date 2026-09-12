using CozyTown.Runtime.NpcLife;

namespace CozyTown.Runtime.NpcAgents
{
    public enum NpcDecisionKind
    {
        Wait,
        InspectLocation,
        Visit,
        Invite,
        AcceptInvitation,
        DeclineInvitation,
        Speak,
        EndConversation,
        Deliver,
        CancelExchange
    }

    public sealed class NpcDecisionReply
    {
        public NpcDecisionReply(NpcDecisionKind kind, string locationId = null,
            NpcActivity activity = NpcActivity.Resting, double durationGameMinutes = 0,
            string planId = null, System.Guid meetingId = default, string text = null)
        {
            Kind = kind;
            LocationId = locationId;
            Activity = activity;
            DurationGameMinutes = durationGameMinutes;
            PlanId = planId;
            MeetingId = meetingId;
            Text = text;
        }

        public NpcDecisionKind Kind { get; }
        public string LocationId { get; }
        public NpcActivity Activity { get; }
        public double DurationGameMinutes { get; }
        public string PlanId { get; }
        public System.Guid MeetingId { get; }
        public string Text { get; }
        public string Operation => Kind switch
        {
            NpcDecisionKind.Wait => "wait",
            NpcDecisionKind.InspectLocation => "inspect_location",
            NpcDecisionKind.Visit => "visit",
            NpcDecisionKind.Invite => "invite",
            NpcDecisionKind.AcceptInvitation => "accept_invite",
            NpcDecisionKind.DeclineInvitation => "decline_invite",
            NpcDecisionKind.Speak => "say",
            NpcDecisionKind.EndConversation => "end_conversation",
            NpcDecisionKind.Deliver => "deliver",
            NpcDecisionKind.CancelExchange => "cancel_exchange",
            _ => string.Empty
        };
    }
}
