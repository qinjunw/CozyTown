using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Unity.Npc;
using NUnit.Framework;

namespace CozyTown.Tests.PlayMode
{
    public sealed partial class CompleteWorldSnapshotPlayModeTests
    {
        [Test]
        public void ResourceConversation_LoadRestoresAcceptedBodiesAndDeliveredPendingTurnWithoutRepeatingTradeOrFutureBubble()
        {
            CreateWorld(sharedMeetingLocations: true);
            var client = new ConversationSnapshotClient();
            string sora = DefaultMvpIds.Npcs.Cook, ren = DefaultMvpIds.Npcs.Fisher;
            var plan = new NpcMeetingPlan("snapshot-resource-meeting", sora, ren, "shared-rest",
                sora + ".rest", ren + ".rest", 720, 720, 730,
                resourceTerms: new CharacterTradeTerms(ren, sora, DefaultMvpIds.Items.Carp, 1, 25));
            _controller.ConfigureDecisions(client, DefaultMvpContent.CreateConfiguration().Npcs,
                new NpcDecisionSettings(maxRequestsPerMinute: 100, maxConcurrentRequests: 4,
                    residentCooldownSeconds: 0.01), new[] { plan });
            _services.WorldTime.AdvanceMinutes(360);
            PumpUntil(() => _controller.GetMeeting(sora)?.State == NpcMeetingState.Travelling);
            Guid meetingId = _controller.GetMeeting(sora).Id;
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            Guid soraActivity = _controller.GetAgentState(sora).ActiveActivity.ActivityId;
            Guid renActivity = _controller.GetAgentState(ren).ActiveActivity.ActivityId;

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            Assert.That(_controller.GetMeeting(sora).Id, Is.EqualTo(meetingId));
            Assert.That(_controller.GetAgentState(sora).ActiveActivity.ActivityId, Is.EqualTo(soraActivity));
            Assert.That(_controller.GetAgentState(ren).ActiveActivity.ActivityId, Is.EqualTo(renActivity));
            Assert.That(_services.WorldTime.AdvanceMinutes(10).IsSuccess, Is.True);
            PumpUntil(() => _controller.GetMeeting(sora)?.Transcript.Count == 1 && client.HasPendingPartner);
            Assert.That(_controller.GetMeeting(sora).DeliveryResultCode, Is.EqualTo("resource.delivered"));
            AssertResourceOwnership();
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var delivered = _services.SaveStorage.Load("main").Value;
            Assert.That(delivered.CompleteWorld.Decisions.Residents.Single(item => item.NpcId == ren).Current, Is.Not.Null);
            client.CompletePartner("future statement");
            PumpUntil(() => _controller.GetMeeting(sora)?.Transcript.Count == 2);
            var view = _world.GetComponent<NpcMeetingDialogueView>();
            Assert.That(view.VisibleText, Does.Contain("future statement"));

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            Assert.That(_controller.GetMeeting(sora).Id, Is.EqualTo(meetingId));
            Assert.That(_controller.GetMeeting(sora).Transcript.Count, Is.EqualTo(1));
            Assert.That(_controller.GetMeeting(sora).SpeakerId, Is.EqualTo(ren));
            Assert.That(_controller.GetMeeting(sora).DeliveryResultCode, Is.EqualTo("resource.delivered"));
            AssertResourceOwnership();
            Assert.That(view.VisibleText, Does.Not.Contain("future statement"));
            Assert.That(view.VisibleText, Does.Contain("saved statement"));
            PumpUntil(() => client.HasPendingPartner);
            Assert.That(client.LastPartnerRequest.Step, Is.EqualTo(2));
            client.CompletePartner("restored statement");
            PumpUntil(() => _controller.GetMeeting(sora)?.State == NpcMeetingState.Completed);
            AssertResourceOwnership();
            Assert.That(client.DeliveryCalls, Is.EqualTo(1));
            Assert.That(_controller.GetMeetingMemories(sora).Count(item => item.Kind == "resource.delivered"), Is.EqualTo(1));
            Assert.That(_controller.GetMeetingMemories(ren).Count(item => item.Kind == "resource.delivered"), Is.EqualTo(1));
            Assert.That(_controller.GetAgentState(sora).ActiveActivity, Is.Null);
            Assert.That(_controller.GetAgentState(ren).ActiveActivity, Is.Null);

            void AssertResourceOwnership()
            {
                var characters = _services.EconomyState.CaptureSnapshot().Characters;
                foreach (string npc in new[] { sora, ren })
                {
                    var character = characters.Single(item => item.CharacterId == npc);
                    Assert.That(character.Wallet.Balance, Is.EqualTo(25));
                    Assert.That(character.Backpack.Items.Where(item => item.ItemId == DefaultMvpIds.Items.Carp)
                        .Sum(item => item.Quantity), Is.EqualTo(1));
                }
            }
        }

        private void PumpUntil(Func<bool> condition)
        {
            for (int i = 0; i < 80 && !condition(); i++)
            {
                _realSeconds += 0.1;
                _controller.TickDecisions(_realSeconds);
            }
            Assert.That(condition(), Is.True, "The fixed client did not reach the expected meeting stage: "
                + string.Join("; ", _residents.Select(resident => resident.NpcId + "="
                    + _controller.GetMeeting(resident.NpcId)?.State + "/" + _controller.GetDecisionOutcome(resident.NpcId)?.Code)));
        }

        private sealed class ConversationSnapshotClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            private TaskCompletionSource<NpcDecisionReply> _partnerReply;
            internal NpcDecisionRequest LastPartnerRequest;
            internal bool HasPendingPartner => _partnerReply != null && !_partnerReply.Task.IsCompleted;
            internal int DeliveryCalls;
            public string SnapshotConfiguration => "fixed-resource-conversation-fixture/v1";
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                var social = request.Social;
                NpcDecisionReply reply;
                if (social == null) reply = new NpcDecisionReply(NpcDecisionKind.Wait);
                else if (social.Kind == NpcSocialContextKind.Opportunity)
                    reply = new NpcDecisionReply(NpcDecisionKind.Invite, planId: social.PlanId);
                else if (social.Kind == NpcSocialContextKind.Invitation)
                    reply = new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: social.MeetingId);
                else if (social.Kind == NpcSocialContextKind.Delivery)
                {
                    DeliveryCalls++;
                    reply = new NpcDecisionReply(NpcDecisionKind.Deliver, meetingId: social.MeetingId);
                }
                else if (social.Transcript.Count >= 2)
                    reply = new NpcDecisionReply(NpcDecisionKind.EndConversation, meetingId: social.MeetingId);
                else if (request.NpcId == DefaultMvpIds.Npcs.Fisher)
                {
                    LastPartnerRequest = request;
                    _partnerReply = new TaskCompletionSource<NpcDecisionReply>();
                    return _partnerReply.Task;
                }
                else reply = new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: social.MeetingId, text: "saved statement");
                return Task.FromResult(reply);
            }
            internal void CompletePartner(string text) => _partnerReply.SetResult(new NpcDecisionReply(
                NpcDecisionKind.Speak, meetingId: LastPartnerRequest.Social.MeetingId, text: text));
        }
    }
}
