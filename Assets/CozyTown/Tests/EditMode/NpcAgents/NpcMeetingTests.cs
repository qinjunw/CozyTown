using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;
using System.Linq;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcMeetingTests
    {
        [Test]
        public void FreshActorAfterLoad_CannotAcceptAnInvitationFromThePreviousWorld()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan() });
            var id = board.Invite(world.GetState("ren"), "lunch").Value.Id;
            world.Observe(Time(720, 2));
            Assert.That(board.Respond(world.GetState("sora"), id, true).ErrorCode, Is.EqualTo("agent.world_stale"));
            Assert.That(world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(world.GetState("sora").ActiveActivity, Is.Null);
        }

        [Test]
        public void UnconsumedOpportunity_DoesNotLeakIntoTheNextMorning()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan() });
            board.Observe();
            Assert.That(board.GetContext("ren"), Is.Not.Null);
            world.Observe(Time(500, day: 2));
            board.Observe();
            Assert.That(board.GetContext("ren"), Is.Null);
        }

        [Test]
        public void ReservedPlace_RejectsAnotherPairWithoutReleasingTheFirstOwners()
        {
            var world = new NpcAgentWorld(new[] { Schedule("ren", 720), Schedule("sora", 750), Schedule("mina", 720), Schedule("eli", 750) });
            world.Observe(Time(720));
            var other = new NpcMeetingPlan("other", "mina", "eli", "pond-walk", "mina.rest", "eli.rest", 720, 750, 780);
            var board = new NpcMeetingBoard(world, new[] { Plan(), other });
            var first = board.Invite(world.GetState("ren"), "lunch").Value.Id;
            board.Respond(world.GetState("sora"), first, true);
            var second = board.Invite(world.GetState("mina"), "other").Value.Id;
            Assert.That(board.Respond(world.GetState("eli"), second, true).ErrorCode, Is.EqualTo("meeting.place_reserved"));
            Assert.That(board.GetCurrent("mina"), Is.Null);
            Assert.That(board.GetCurrent("eli"), Is.Null);
            Assert.That(board.GetCurrent("ren").Id, Is.EqualTo(first));
            Assert.That(board.IsPlaceReserved("pond-walk"), Is.True);
            Assert.That(world.GetState("sora").ActiveActivity, Is.Not.Null);
        }

        [Test]
        public void Conversation_StillWaitsForGlobalBudgetAfterSkippingResidentCooldown()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan(2) }, (npc, location) => NpcMeetingPresence.Arrived);
            var client = new SocialClient();
            using var scheduler = new NpcDecisionScheduler(world, Profiles(), client,
                new NpcDecisionSettings(maxRequestsPerMinute: 2), board);
            scheduler.Tick(0); scheduler.Tick(0.1); scheduler.Tick(0.2);
            world.Observe(Time(750));
            scheduler.Tick(1); scheduler.Tick(59);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(board.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Talking));
            scheduler.Tick(60); scheduler.Tick(60.1); scheduler.Tick(60.2);
            Assert.That(client.Requests.Count, Is.EqualTo(4));
            Assert.That(board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
        }

        [Test]
        public void Load_DiscardsAPendingInvitationReplyWithoutResettingPhysicalSlotOrBudget()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan() });
            var client = new DelayedClient();
            using var scheduler = new NpcDecisionScheduler(world, Profiles(), client,
                new NpcDecisionSettings(maxRequestsPerMinute: 2), board);
            scheduler.Tick(0); scheduler.Tick(0.1);
            Assert.That(board.GetCurrent("sora").State, Is.EqualTo(NpcMeetingState.Invited));
            world.Observe(Time(720, 2));
            scheduler.Tick(0.2);
            Assert.That(client.Token.IsCancellationRequested, Is.True);
            Assert.That(board.GetCurrent("ren"), Is.Null);
            Assert.That(board.GetMemories("ren"), Is.Empty);
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            client.Reply.SetResult(new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: client.Request.Social.MeetingId));
            scheduler.Tick(1);
            Assert.That(scheduler.RequestsStarted, Is.EqualTo(2));
            Assert.That(world.GetState("sora").ActiveActivity, Is.Null);
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.world_stale"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayerBusy_RejectsAcceptanceOrCancelsAFutureCommitment(bool afterAcceptance)
        {
            var world = CreateWorld();
            bool busy = !afterAcceptance;
            var board = new NpcMeetingBoard(world, new[] { Plan() }, (npc, location) =>
                busy && npc == "sora" ? NpcMeetingPresence.Busy : NpcMeetingPresence.Travelling);
            var id = board.Invite(world.GetState("ren"), "lunch").Value.Id;
            var accepted = board.Respond(world.GetState("sora"), id, true);
            if (afterAcceptance) { Assert.That(accepted.IsSuccess, Is.True); busy = true; board.Observe(); }
            else Assert.That(accepted.IsSuccess, Is.False);
            Assert.That(board.GetCurrent("ren"), Is.Null);
            Assert.That(world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(world.GetState("sora").ActiveActivity, Is.Null);
        }

        private static CozyTown.Runtime.Npc.NpcDefinition[] Profiles() => new[] {
            new CozyTown.Runtime.Npc.NpcDefinition("ren", "Ren", "Quiet fisher", "Hello"),
            new CozyTown.Runtime.Npc.NpcDefinition("sora", "Sora", "Enthusiastic cook", "Hello") };

        private sealed class DelayedClient : INpcDecisionClient
        {
            public readonly System.Threading.Tasks.TaskCompletionSource<NpcDecisionReply> Reply = new System.Threading.Tasks.TaskCompletionSource<NpcDecisionReply>();
            public System.Threading.CancellationToken Token;
            public NpcDecisionRequest Request;
            public System.Threading.Tasks.Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, System.Threading.CancellationToken token)
            {
                if (request.Social?.Kind == NpcSocialContextKind.Opportunity)
                    return System.Threading.Tasks.Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Invite, planId: request.Social.PlanId));
                Request = request; Token = token;
                return Reply.Task;
            }
        }

        [Test]
        public void CurrentSpeaker_CanEndAfterTwoLinesButCannotReplayAnEarlierTurn()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan() }, (npc, location) => NpcMeetingPresence.Arrived);
            var id = board.Invite(world.GetState("ren"), "lunch").Value.Id;
            board.Respond(world.GetState("sora"), id, true);
            world.Observe(Time(750));
            board.Observe();
            var oldRen = world.GetState("ren");
            Assert.That(board.EndConversation(oldRen, id).IsSuccess, Is.False);
            board.Speak(oldRen, id, "Morning on the pond.");
            board.Speak(world.GetState("sora"), id, "See you at the kitchen.");
            Assert.That(board.Speak(oldRen, id, "Replay").ErrorCode, Is.EqualTo("agent.decision_stale"));
            Assert.That(board.EndConversation(world.GetState("sora"), id).IsSuccess, Is.False);
            Assert.That(board.EndConversation(world.GetState("ren"), id).IsSuccess, Is.True);
            Assert.That(board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(world.GetState("sora").ActiveActivity, Is.Null);
        }

        [TestCase("schedule")]
        [TestCase("expired")]
        public void FailedAcceptance_ReleasesTheInvitation(string cause)
        {
            var world = CreateWorld();
            var plan = cause == "schedule" ? new NpcMeetingPlan("lunch", "ren", "sora", "pond-walk", "ren.rest", "sora.rest", 720, 820, 850) : Plan();
            var board = new NpcMeetingBoard(world, new[] { plan });
            var id = board.Invite(world.GetState("ren"), "lunch").Value.Id;
            if (cause == "expired") world.Observe(Time(781));
            Assert.That(board.Respond(world.GetState("sora"), id, true).IsSuccess, Is.False);
            Assert.That(board.GetCurrent("ren"), Is.Null);
            Assert.That(board.GetCurrent("sora"), Is.Null);
        }

        [Test]
        public void ReverseInvite_IsDeduplicatedAndBusyAcceptanceWritesNeitherActivity()
        {
            var world = CreateWorld();
            var reverse = new NpcMeetingPlan("reverse", "sora", "ren", "pond-walk", "sora.rest", "ren.rest", 720, 750, 780);
            var board = new NpcMeetingBoard(world, new[] { Plan(), reverse });
            var id = board.Invite(world.GetState("ren"), "lunch").Value.Id;
            Assert.That(board.Invite(world.GetState("sora"), "reverse").ErrorCode, Is.EqualTo("meeting.resident_reserved"));
            var sora = world.GetState("sora");
            var external = new NpcActivityRequest("sora", sora.WorldRunId, sora.Revision, "sora.work", NpcActivity.Working, 850);
            world.SubmitActivity(external);
            Assert.That(board.Respond(world.GetState("sora"), id, true).IsSuccess, Is.False);
            Assert.That(world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(world.GetState("sora").ActiveActivity, Is.SameAs(external));
            Assert.That(board.GetCurrent("ren"), Is.Null);
        }

        [Test]
        public void Scheduler_WorldOpportunityInvitesWorkingPartnerAndAlternatesWithoutPlayerInput()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan(2) }, (npc, location) => NpcMeetingPresence.Arrived);
            var client = new SocialClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] {
                new CozyTown.Runtime.Npc.NpcDefinition("ren", "Ren", "Quiet fisher", "Hello"),
                new CozyTown.Runtime.Npc.NpcDefinition("sora", "Sora", "Enthusiastic cook", "Hello") }, client, meetings: board);
            scheduler.Tick(0);
            Assert.That(client.Requests.Single().Social?.Kind, Is.EqualTo(NpcSocialContextKind.Opportunity));
            scheduler.Tick(0.1);
            Assert.That(client.Requests.Last().Social.Kind, Is.EqualTo(NpcSocialContextKind.Invitation));
            Assert.That(client.Requests.Last().Self.Target.ExpectedActivity, Is.EqualTo(NpcActivity.Working));
            scheduler.Tick(0.2);
            Assert.That(board.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Scheduled));
            Assert.That(world.GetTargetAt("sora", 749).TargetLocationId, Is.EqualTo("sora.work"));
            scheduler.Tick(0.3);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            world.Observe(Time(750));
            scheduler.Tick(1);
            scheduler.Tick(1.1);
            scheduler.Tick(1.2);
            Assert.That(board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(client.Requests.Where(item => item.Social?.Kind == NpcSocialContextKind.Conversation)
                .Select(item => item.NpcId), Is.EqualTo(new[] { "ren", "sora" }));
            Assert.That(client.Requests[2].Social.Transcript, Is.Empty);
            Assert.That(client.Requests[3].Social.Transcript.Count, Is.EqualTo(1));
            Assert.That(world.GetState("ren").ActiveActivity, Is.Null);
        }

        private sealed class SocialClient : INpcDecisionClient
        {
            public readonly System.Collections.Generic.List<NpcDecisionRequest> Requests = new System.Collections.Generic.List<NpcDecisionRequest>();
            public System.Threading.Tasks.Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, System.Threading.CancellationToken token)
            {
                Requests.Add(request);
                var social = request.Social;
                var reply = social == null ? new NpcDecisionReply(NpcDecisionKind.Wait)
                    : social.Kind == NpcSocialContextKind.Opportunity ? new NpcDecisionReply(NpcDecisionKind.Invite, planId: social.PlanId)
                    : social.Kind == NpcSocialContextKind.Invitation ? new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: social.MeetingId)
                    : new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: social.MeetingId, text: request.NpcId + " enjoys the pond.");
                return System.Threading.Tasks.Task.FromResult(reply);
            }
        }

        [Test]
        public void RepeatedMeetings_KeepOnlySixteenRecentEventsPerParticipant()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan(2) }, (npc, location) => NpcMeetingPresence.Arrived);
            for (int day = 1; day <= 9; day++)
            {
                world.Observe(Time(720, day: day));
                board.Observe();
                var invitation = board.Invite(world.GetState("ren"), "lunch").Value;
                board.Respond(world.GetState("sora"), invitation.Id, true);
                world.Observe(Time(750, day: day));
                board.Observe();
                board.Speak(world.GetState("ren"), invitation.Id, "The pond is calm.");
                board.Speak(world.GetState("sora"), invitation.Id, "Good weather for a walk.");
            }
            Assert.That(board.GetMemories("ren").Count, Is.EqualTo(16));
            Assert.That(board.GetMemories("sora").Count, Is.EqualTo(16));
            Assert.That(board.GetMemories("ren").Last().Kind, Is.EqualTo("meeting.completed"));
        }

        [Test]
        public void Memories_ContainOnlyTheParticipantsActualEventsAndDoNotCrossALoad()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan(2) }, (npc, location) => NpcMeetingPresence.Arrived);
            var invitation = board.Invite(world.GetState("ren"), "lunch").Value;
            Assert.That(board.GetMemories("ren").Select(item => item.Kind), Is.EqualTo(new[] { "meeting.invited" }));
            Assert.That(board.GetMemories("mina"), Is.Empty);
            board.Respond(world.GetState("sora"), invitation.Id, true);
            world.Observe(Time(750));
            board.Observe();
            board.Speak(world.GetState("ren"), invitation.Id, "The pond is quiet.");
            board.Speak(world.GetState("sora"), invitation.Id, "We could discuss a soup recipe.");

            Assert.That(board.GetMemories("ren").Select(item => item.Kind), Is.EqualTo(new[] {
                "meeting.invited", "meeting.accepted", "meeting.arrived", "meeting.spoken", "meeting.spoken", "meeting.completed" }));
            Assert.That(board.GetMemories("sora").All(item => item.PartnerId == "ren"), Is.True);
            Assert.That(board.GetMemories("ren").Single(item => item.SpeakerId == "sora").Text, Is.EqualTo("We could discuss a soup recipe."));
            Assert.That(board.GetMemories("mina"), Is.Empty);
            world.Observe(Time(720, 2));
            board.Observe();
            Assert.That(board.GetMemories("ren"), Is.Empty);
            Assert.That(board.GetMemories("sora"), Is.Empty);
        }

        [TestCase("deadline")]
        [TestCase("path")]
        [TestCase("busy")]
        [TestCase("activity")]
        [TestCase("load")]
        [TestCase("presence_lost")]
        [TestCase("invitation_timeout")]
        public void InvalidMeeting_ReleasesReservationsAndPreservesAnExternalReplacementActivity(string cause)
        {
            var world = CreateWorld();
            var presence = NpcMeetingPresence.Arrived;
            var board = new NpcMeetingBoard(world, new[] { Plan() }, (npc, location) => presence);
            var invitation = board.Invite(world.GetState("ren"), "lunch").Value;
            if (cause != "invitation_timeout") board.Respond(world.GetState("sora"), invitation.Id, true);
            world.Observe(Time(750));
            board.Observe();
            NpcActivityRequest replacement = null;
            if (cause == "deadline") world.Observe(Time(901));
            if (cause == "path") presence = NpcMeetingPresence.Blocked;
            if (cause == "busy") presence = NpcMeetingPresence.Busy;
            if (cause == "presence_lost") presence = NpcMeetingPresence.Travelling;
            if (cause == "load") world.Observe(Time(720, 2));
            if (cause == "invitation_timeout") world.Observe(Time(781));
            if (cause == "activity")
            {
                var state = world.GetState("ren");
                world.CancelActivity("ren", state.WorldRunId, state.Revision);
                state = world.GetState("ren");
                replacement = new NpcActivityRequest("ren", state.WorldRunId, state.Revision, "ren.work", NpcActivity.Working, 950);
                Assert.That(world.SubmitActivity(replacement).IsSuccess, Is.True);
            }

            board.Observe();

            Assert.That(board.GetCurrent("ren"), Is.Null);
            Assert.That(board.GetCurrent("sora"), Is.Null);
            Assert.That(board.IsPlaceReserved("pond-walk"), Is.False);
            Assert.That(world.GetState("ren").ActiveActivity, Is.SameAs(replacement));
            Assert.That(world.GetState("sora").ActiveActivity, Is.Null);
            if (cause == "load") Assert.That(board.GetLatest("ren"), Is.Null);
            else Assert.That(board.GetLatest("ren").State, Is.Not.EqualTo(NpcMeetingState.Completed));
        }

        [Test]
        public void FiniteAlternatingDialogue_ReleasesBothResidentsToTheCurrentSchedule()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan(2) }, (npc, location) => NpcMeetingPresence.Arrived);
            var invitation = board.Invite(world.GetState("ren"), "lunch").Value;
            board.Respond(world.GetState("sora"), invitation.Id, true);
            world.Observe(Time(750));
            board.Observe();
            Assert.That(board.Speak(world.GetState("sora"), invitation.Id, "Out of turn").IsSuccess, Is.False);

            var firstLine = board.Speak(world.GetState("ren"), invitation.Id, "The pond is quiet today.");

            Assert.That(firstLine.IsSuccess, Is.True, firstLine.ErrorCode);
            var afterFirst = board.GetCurrent("ren");
            Assert.That(afterFirst.SpeakerId, Is.EqualTo("sora"));
            Assert.That(afterFirst.Transcript.Count, Is.EqualTo(1));
            world.Observe(Time(815));
            board.Observe();
            Assert.That(board.Speak(world.GetState("sora"), invitation.Id, "A calm day for a warm fish soup.").IsSuccess, Is.True);
            Assert.That(board.GetCurrent("ren"), Is.Null);
            Assert.That(board.GetCurrent("sora"), Is.Null);
            Assert.That(board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(board.GetLatest("ren").Transcript.Count, Is.EqualTo(2));
            Assert.That(afterFirst.Transcript.Count, Is.EqualTo(1), "Later speech must not mutate earlier observations.");
            Assert.That(board.IsPlaceReserved("pond-walk"), Is.False);
            Assert.That(world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(world.GetState("sora").ActiveActivity, Is.Null);
            Assert.That(world.GetState("ren").Target.TargetLocationId, Is.EqualTo("ren.afternoon"));
            Assert.That(world.GetState("sora").Target.TargetLocationId, Is.EqualTo("sora.afternoon"));
        }

        [Test]
        public void Conversation_OpensOnlyAfterTheStartTimeAndBothActualArrivals()
        {
            var world = CreateWorld();
            bool soraArrived = false;
            var board = new NpcMeetingBoard(world, new[] { Plan() }, (npc, location) =>
                npc == "ren" || soraArrived ? NpcMeetingPresence.Arrived : NpcMeetingPresence.Travelling);
            var invitation = board.Invite(world.GetState("ren"), "lunch").Value;
            board.Respond(world.GetState("sora"), invitation.Id, true);
            world.Observe(Time(749));
            board.Observe();
            Assert.That(board.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Scheduled));
            world.Observe(Time(750));

            board.Observe();

            Assert.That(board.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Travelling));
            soraArrived = true;
            board.Observe();
            Assert.That(board.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Talking));
            Assert.That(board.GetCurrent("sora").SpeakerId, Is.EqualTo("ren"));
        }

        [Test]
        public void Acceptance_ReservesBothFutureActivitiesWhileSoraFinishesCurrentWork()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan() });
            var invitation = board.Invite(world.GetState("ren"), "lunch").Value;

            var accepted = board.Respond(world.GetState("sora"), invitation.Id, true);

            Assert.That(accepted.IsSuccess, Is.True, accepted.ErrorCode);
            Assert.That(board.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Scheduled));
            Assert.That(invitation.State, Is.EqualTo(NpcMeetingState.Invited));
            Assert.That(board.IsPlaceReserved("pond-walk"), Is.True);
            Assert.That(world.GetState("ren").ActiveActivity, Is.Not.Null);
            Assert.That(world.GetState("sora").ActiveActivity, Is.Not.Null);
            Assert.That(world.GetTargetAt("sora", 749).TargetLocationId, Is.EqualTo("sora.work"));
            Assert.That(world.GetTargetAt("sora", 750).TargetLocationId, Is.EqualTo("sora.rest"));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
        }

        [Test]
        public void Invitation_IsVisibleToBothResidentsWithoutTakingOverTheirBodies()
        {
            var world = CreateWorld();
            var board = new NpcMeetingBoard(world, new[] { Plan() });

            var invited = board.Invite(world.GetState("ren"), "lunch");

            Assert.That(invited.IsSuccess, Is.True, invited.ErrorCode);
            Assert.That(invited.Value.State, Is.EqualTo(NpcMeetingState.Invited));
            Assert.That(board.GetCurrent("ren").Id, Is.EqualTo(board.GetCurrent("sora").Id));
            Assert.That(world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(world.GetState("sora").ActiveActivity, Is.Null);
            Assert.That(world.GetState("sora").Target.ExpectedActivity, Is.EqualTo(NpcActivity.Working));
        }

        private static NpcMeetingPlan Plan(int maxTurns = 4)
            => new NpcMeetingPlan("lunch", "ren", "sora", "pond-walk", "ren.rest", "sora.rest", 720, 750, 780, maxTurns: maxTurns);

        private static NpcAgentWorld CreateWorld()
        {
            var world = new NpcAgentWorld(new[] { Schedule("ren", 720), Schedule("sora", 750), Schedule("mina", 720) });
            world.Observe(Time(720));
            return world;
        }

        private static NpcDailySchedule Schedule(string id, int rest)
            => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest",
                id + ".afternoon", 360, 480, rest, 810, 1020, 1080);

        private static WorldTimeProgress Time(int minute, long rebuild = 1, int day = 1)
            => new WorldTimeProgress(new GameClockSnapshot(day, minute), 0, false, rebuild);
    }
}
