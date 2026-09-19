using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class FixedExperimentDecisionClient : INpcDecisionClient, INpcDecisionConfiguration
    {
        public string SnapshotConfiguration => "fixed-four-resident-experiment/v1";

        public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            var allowed = request.AllowedOperations;
            NpcDecisionReply reply;
            if (allowed.Contains("invite"))
                reply = new NpcDecisionReply(NpcDecisionKind.Invite, planId: request.Social.PlanId);
            else if (allowed.Contains("accept_invite"))
                reply = new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: request.Social.MeetingId);
            else if (allowed.Contains("decline_invite"))
                reply = new NpcDecisionReply(NpcDecisionKind.DeclineInvitation, meetingId: request.Social.MeetingId);
            else if (allowed.Contains("deliver"))
                reply = new NpcDecisionReply(NpcDecisionKind.Deliver, meetingId: request.Social.MeetingId);
            else if (allowed.Contains("cancel_exchange"))
                reply = new NpcDecisionReply(NpcDecisionKind.CancelExchange, meetingId: request.Social.MeetingId);
            else if (allowed.Contains("end_conversation"))
                reply = new NpcDecisionReply(NpcDecisionKind.EndConversation, meetingId: request.Social.MeetingId);
            else if (allowed.Contains("say"))
                reply = Say(request);
            else if (allowed.Contains("wait"))
                reply = new NpcDecisionReply(NpcDecisionKind.Wait);
            else throw new InvalidOperationException("The fixed experiment client has no supported allowed response.");
            return Task.FromResult(reply);
        }

        private static NpcDecisionReply Say(NpcDecisionRequest request)
        {
            if (request.SpeechMode == NpcSpeechMode.FreeText)
                return new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: request.Social.MeetingId,
                    text: "Thank you for meeting me.");
            var fact = request.Observation?.Facts.SingleOrDefault(item => item.EntityId == request.NpcId
                && item.Predicate == "region_name" && item.CanExpress);
            var frame = fact == null ? null : new NpcSpeechFrame("report_observation", fact.FactId, "neutral");
            if (!NpcFactSpeech.TryRender(request.Observation, frame, out _, out var error))
                throw new InvalidOperationException("The fixed experiment requires an expressible current region fact: " + error);
            return new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: request.Social.MeetingId, speechFrame: frame);
        }
    }
}
