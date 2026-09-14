using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcObservationDecisionTests
    {
        private NpcAgentWorld _world;
        private NpcMeetingBoard _board;
        private IEconomyStateStore _store;
        private CharacterResourceTrading _trading;
        private NpcObservationScene _scene;
        private NpcObservationBody[] _bodies;

        [SetUp]
        public void SetUp()
        {
            _world = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora") });
            _world.Observe(Time(720));
            _store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) },
                Array.Empty<ShopEconomySnapshot>());
            _trading = new CharacterResourceTrading(_store,
                new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            _board = new NpcMeetingBoard(_world, new[] { new NpcMeetingPlan("fish-supply", "sora", "ren", "pond",
                "sora.rest", "ren.rest", 720, 750, 780,
                resourceTerms: new CharacterTradeTerms("ren", "sora", "fish", 1, 25)) }, resources: _trading);
            _scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond walk", -10, -10, 10, 10) },
                new[] { new NpcObservationEntity("water", "Pond", 1, 0, "landmark", interactionId: "fishing", resourceItemId: "fish") });
            _bodies = new[] { new NpcObservationBody("sora", 0, 0), new NpcObservationBody("ren", 2, 0) };
        }

        [TestCase(true, NpcSpeechMode.FreeText)]
        [TestCase(false, NpcSpeechMode.FreeText)]
        [TestCase(true, NpcSpeechMode.StructuredFacts)]
        [TestCase(false, NpcSpeechMode.StructuredFacts)]
        public void InvitationReply_SameRegionMovementPreservesTheAnswerWithoutAnotherCall(bool accept, NpcSpeechMode mode)
        {
            Assert.That(_board.Invite(_world.GetState("sora"), "fish-supply").IsSuccess, Is.True);
            var client = new ControlledClient();
            var views = new List<NpcLocalObservation>();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("ren") }, client,
                meetings: _board, speechMode: mode,
                observe: request => { var view = Observe(request); views.Add(view); return view; });
            scheduler.Tick(0);
            var request = client.Requests.Single();
            Assert.That(request.Social.Kind, Is.EqualTo(NpcSocialContextKind.Invitation));
            _bodies[1] = new NpcObservationBody("ren", 2.25, 0.25);
            _world.Observe(Time(720));
            var candidate = new NpcDecisionReply(accept ? NpcDecisionKind.AcceptInvitation : NpcDecisionKind.DeclineInvitation,
                meetingId: request.Social.MeetingId);
            client.Replies.Single().SetResult(candidate);

            scheduler.Tick(1);

            Assert.That(views.Count, Is.EqualTo(2));
            TestContext.Out.WriteLine("Dispatch: time={0}, x={1}, y={2}, region={3}; execution: time={4}, x={5}, y={6}, region={7}",
                views[0].ObservedAtTotalMinutes, views[0].X, views[0].Y, views[0].RegionId,
                views[1].ObservedAtTotalMinutes, views[1].X, views[1].Y, views[1].RegionId);
            Assert.That(views[1].Facts.Select(FactProjection), Is.EqualTo(views[0].Facts.Select(FactProjection)),
                "The registered facts must be unchanged in this coordinate-only reproduction.");
            CollectionAssert.AreEqual(views[0].NearbyEntityIds, views[1].NearbyEntityIds);
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo(accept ? "meeting.accept_invite" : "meeting.decline_invite"));
            Assert.That(scheduler.GetLastOutcome("ren").Context.Observation, Is.SameAs(request.Observation));
            Assert.That(scheduler.GetLastOutcome("ren").ExecutionObservation, Is.SameAs(views[1]));
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(accept ? NpcMeetingState.Scheduled : NpcMeetingState.Declined));
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            AssertAssets(2, 0, 0, 50);
        }

        [TestCase("region", true)]
        [TestCase("region", false)]
        [TestCase("space", true)]
        [TestCase("space", false)]
        [TestCase("nearby", true)]
        [TestCase("nearby", false)]
        [TestCase("capability", true)]
        [TestCase("capability", false)]
        [TestCase("quantity", true)]
        [TestCase("quantity", false)]
        [TestCase("balance", true)]
        [TestCase("balance", false)]
        [TestCase("unknown_region", true)]
        [TestCase("unknown_region", false)]
        public void InvitationReply_ChangedFactsStillRejectBothAnswers(string change, bool accept)
        {
            if (change == "unknown_region") _bodies[1] = new NpcObservationBody("ren", 20, 0);
            Assert.That(_board.Invite(_world.GetState("sora"), "fish-supply").IsSuccess, Is.True);
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("ren") }, client,
                meetings: _board, observe: Observe);
            scheduler.Tick(0);
            var request = client.Requests.Single();
            switch (change)
            {
                case "region": _bodies[1] = new NpcObservationBody("ren", 12, 0); break;
                case "space": _bodies[1] = new NpcObservationBody("ren", 2, 0, "ren.home"); break;
                case "nearby": _bodies[1] = new NpcObservationBody("ren", 9, 0); break;
                case "quantity": _store.CommitCharacter(Character("ren", 0, 0)); break;
                case "balance": _store.CommitCharacter(Character("ren", 2, 5)); break;
                case "unknown_region": _bodies[1] = new NpcObservationBody("ren", 21, 0); break;
                case "capability":
                    _scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond walk", -10, -10, 10, 10) },
                        new[] { new NpcObservationEntity("water", "Pond", 1, 0, "landmark", resourceItemId: "fish") });
                    break;
            }
            _world.Observe(Time(721));
            Assert.That(_world.GetState("ren").Revision, Is.EqualTo(request.Self.Revision));
            var beforeAssets = new[] { "ren", "sora" }.Select(id => _trading.Inspect(request.Social.Resources.Terms, id))
                .Select(state => new { state.OwnedQuantity, state.Balance }).ToArray();
            client.Replies.Single().SetResult(new NpcDecisionReply(accept ? NpcDecisionKind.AcceptInvitation : NpcDecisionKind.DeclineInvitation,
                meetingId: request.Social.MeetingId));
            scheduler.Tick(1);

            var outcome = scheduler.GetLastOutcome("ren");
            Assert.That(outcome.Code, Is.EqualTo("agent.observation_stale"));
            Assert.That(outcome.Context.Observation, Is.SameAs(request.Observation));
            Assert.That(outcome.ExecutionObservation, Is.Not.Null);
            Assert.That(_board.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Invited));
            Assert.That(_board.GetCurrent("ren").Transcript, Is.Empty);
            Assert.That(_board.IsPlaceReserved("pond"), Is.False);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(new[] { "ren", "sora" }.Select(id => _trading.Inspect(request.Social.Resources.Terms, id))
                .Select(state => new { state.OwnedQuantity, state.Balance }), Is.EqualTo(beforeAssets));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InvitationReply_OnlySamplingTimeChanges_StillApplies(bool accept)
        {
            Assert.That(_board.Invite(_world.GetState("sora"), "fish-supply").IsSuccess, Is.True);
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("ren") }, client,
                meetings: _board, observe: Observe);
            scheduler.Tick(0);
            var request = client.Requests.Single();
            _world.Observe(Time(721));
            client.Replies.Single().SetResult(new NpcDecisionReply(accept ? NpcDecisionKind.AcceptInvitation : NpcDecisionKind.DeclineInvitation,
                meetingId: request.Social.MeetingId));
            scheduler.Tick(1);
            var outcome = scheduler.GetLastOutcome("ren");
            Assert.That(outcome.Code, Is.EqualTo(accept ? "meeting.accept_invite" : "meeting.decline_invite"));
            Assert.That(outcome.ExecutionObservation.ObservedAtTotalMinutes, Is.EqualTo(721));
            Assert.That(outcome.Context.Observation.ObservedAtTotalMinutes, Is.EqualTo(720));
            Assert.That(outcome.Calls, Is.EqualTo(1));
        }

        [TestCase(NpcSpeechMode.FreeText)]
        [TestCase(NpcSpeechMode.StructuredFacts)]
        public void SpeechAfterSameRegionMovement_RemainsRejectedWithoutPublishingOrRemembering(NpcSpeechMode mode)
        {
            _board = new NpcMeetingBoard(_world, new[] { new NpcMeetingPlan("lunch", "sora", "ren", "pond",
                "sora.rest", "ren.rest", 720, 750, 780) }, (npc, location) => NpcMeetingPresence.Arrived);
            var meeting = _board.Invite(_world.GetState("sora"), "lunch").Value;
            Assert.That(_board.Respond(_world.GetState("ren"), meeting.Id, true).IsSuccess, Is.True);
            _world.Observe(Time(750));
            _board.Observe();
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("sora") }, client,
                meetings: _board, observe: Observe, speechMode: mode);
            scheduler.Tick(0);
            var request = client.Requests.Single();
            Assert.That(request.Social.Kind, Is.EqualTo(NpcSocialContextKind.Conversation));
            var memories = _board.GetMemories("sora").ToArray();
            _bodies[0] = new NpcObservationBody("sora", 0.25, 0);
            client.Replies.Single().SetResult(new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: meeting.Id,
                text: mode == NpcSpeechMode.FreeText ? "I am beside the pond." : null,
                speechFrame: mode == NpcSpeechMode.StructuredFacts ? new NpcSpeechFrame("report_observation", "sora:region_name", "neutral") : null));
            scheduler.Tick(1);
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.observation_stale"));
            Assert.That(_board.GetCurrent("sora").Transcript, Is.Empty);
            CollectionAssert.AreEqual(memories, _board.GetMemories("sora"));
            Assert.That(_board.GetMemories("ren").Any(item => item.Kind == "meeting.spoken"), Is.False);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            AssertAssets(2, 0, 0, 50);
        }

        [TestCase("reload", "agent.world_stale")]
        [TestCase("opportunity_deadline", "agent.opportunity_expired")]
        [TestCase("invitation_deadline", "agent.decision_stale")]
        [TestCase("timeout", "agent.request_timeout")]
        public void InvitationReply_PreExecutionGatesCancelTheOldAnswer(string change, string expected)
        {
            Assert.That(_board.Invite(_world.GetState("sora"), "fish-supply").IsSuccess, Is.True);
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("ren") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1), meetings: _board, observe: Observe);
            scheduler.Tick(0);
            var request = client.Requests.Single();
            _bodies[1] = new NpcObservationBody("ren", 2.25, 0);
            if (change == "reload") _world.Observe(Time(720, rebuild: 2));
            if (change == "opportunity_deadline") _world.Observe(Time(750));
            if (change == "invitation_deadline") _world.Observe(Time(780));
            scheduler.Tick(change == "timeout" ? 8 : 1);
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo(expected));
            Assert.That(scheduler.GetLastOutcome("ren").ExecutionObservation, Is.Null,
                "A cancelled response must not be reported as having passed execution-time observation checks.");
            Assert.That(client.Tokens.Single().IsCancellationRequested, Is.True);
            client.Replies.Single().SetResult(new NpcDecisionReply(NpcDecisionKind.AcceptInvitation,
                meetingId: request.Social.MeetingId));
            scheduler.Tick(9);
            Assert.That(_board.IsPlaceReserved("pond"), Is.False);
            Assert.That(_world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(_world.GetState("sora").ActiveActivity, Is.Null);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            AssertAssets(2, 0, 0, 50);
        }

        [Test]
        public void StaleInvitationReply_DoesNotRetryAndANewWorldEventCanStartANewDecision()
        {
            Assert.That(_board.Invite(_world.GetState("sora"), "fish-supply").IsSuccess, Is.True);
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("ren") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1), meetings: _board, observe: Observe);
            scheduler.Tick(0);
            var old = client.Requests.Single();
            _bodies[1] = new NpcObservationBody("ren", 9, 0);
            client.Replies.Single().SetResult(new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: old.Social.MeetingId));
            scheduler.Tick(1);
            scheduler.Tick(10);
            Assert.That(client.Requests.Count, Is.EqualTo(1), "Observation changes alone do not mint another decision or call.");
            _world.Observe(Time(780));
            scheduler.Tick(11);
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Expired));
            Assert.That(_board.IsPlaceReserved("pond"), Is.False);
            _world.Observe(Time(720, rebuild: 2));
            scheduler.Tick(59);
            Assert.That(client.Requests.Count, Is.EqualTo(1), "The world event does not reset the rolling call budget.");
            scheduler.Tick(60);
            var fresh = client.Requests.Last();
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(fresh.DecisionId, Is.Not.EqualTo(old.DecisionId));
            Assert.That(fresh.Self.WorldRunId, Is.Not.EqualTo(old.Self.WorldRunId));
            Assert.That(fresh.Observation.X, Is.EqualTo(9));
            Assert.That(fresh.PreviousResultCode, Is.Null);
            client.Replies.Last().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(61);
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("agent.decision_wait"));
            AssertAssets(2, 0, 0, 50);
        }

        [Test]
        public void OwnAssetsChangedDuringGeneration_RejectsTheCandidateAndRetainsItsOriginalObservation()
        {
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world,
                new[] { new NpcDefinition("sora", "Sora", "Cook", "Hello") }, client, meetings: _board, observe: Observe);
            scheduler.Tick(0);
            var request = client.Requests.Single();
            Assert.That(request.Observation.Facts.Single(fact => fact.EntityId == "sora" && fact.Predicate == "balance").Value,
                Is.EqualTo("50"));
            Assert.That(request.AllowedOperations, Does.Contain("invite"));
            var candidate = new NpcDecisionReply(NpcDecisionKind.Invite, planId: request.Social.PlanId);

            _store.CommitCharacter(Character("sora", 0, 24));
            Assert.That(_world.GetState("sora").Revision, Is.EqualTo(request.Self.Revision),
                "An economy change does not advance the resident revision.");
            client.Replies.Single().SetResult(candidate);
            scheduler.Tick(1);

            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("agent.observation_stale"));
            Assert.That(outcome.Reply, Is.SameAs(candidate));
            Assert.That(outcome.Context.Observation, Is.SameAs(request.Observation));
            Assert.That(outcome.Context.Observation.Facts.Single(fact => fact.EntityId == "sora" && fact.Predicate == "balance").Value,
                Is.EqualTo("50"));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            Assert.That(_board.GetCurrent("ren"), Is.Null);
            Assert.That(_board.IsPlaceReserved("pond"), Is.False);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            AssertAssets(2, 0, 0, 24);
        }

        [Test]
        public void PendingBudgetWait_SamplesTheCurrentTimeAndBodyOnlyWhenDispatched()
        {
            var client = new ControlledClient();
            var sampledResidents = new List<string>();
            using var scheduler = new NpcDecisionScheduler(_world,
                new[] { Profile("ren"), Profile("sora") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1), observe: request =>
                {
                    sampledResidents.Add(request.NpcId);
                    return Observe(request);
                });
            scheduler.Tick(0);
            Assert.That(client.Requests.Single().NpcId, Is.EqualTo("ren"));
            client.Replies.Single().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(1);

            _world.Observe(Time(725));
            _bodies[0] = new NpcObservationBody("sora", 5, 0);
            scheduler.Tick(59);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(sampledResidents, Does.Not.Contain("sora"));
            _world.Observe(Time(726));
            _bodies[0] = new NpcObservationBody("sora", 6, 0);
            scheduler.Tick(60);

            var dispatched = client.Requests.Last();
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(dispatched.NpcId, Is.EqualTo("sora"));
            Assert.That(dispatched.GameTotalMinutes, Is.EqualTo(720), "Waiting does not renew the opportunity lifetime.");
            Assert.That(dispatched.Observation.ObservedAtTotalMinutes, Is.EqualTo(726));
            Assert.That(dispatched.Observation.X, Is.EqualTo(6));
            Assert.That(dispatched.Observation.NearbyEntityIds, Is.Empty);
            Assert.That(dispatched.Observation.Facts.All(fact => fact.ObservedAtTotalMinutes == 726), Is.True);
            client.Replies.Last().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(61);
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.decision_wait"));
        }

        [Test]
        public void PendingResourceDecision_RefreshesOwnTermsAndOperationsBeforeItsFirstDispatch()
        {
            _world = new NpcAgentWorld(new[] { Schedule("ren", 719), Schedule("sora") });
            _world.Observe(Time(719));
            _board = new NpcMeetingBoard(_world, new[] { new NpcMeetingPlan("fish-supply", "sora", "ren", "pond",
                "sora.rest", "ren.rest", 720, 750, 780,
                resourceTerms: new CharacterTradeTerms("ren", "sora", "fish", 1, 25)) }, resources: _trading);
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world,
                new[] { Profile("ren"), Profile("sora") }, client,
                new NpcDecisionSettings(maxConcurrentRequests: 1), meetings: _board, observe: Observe);
            scheduler.Tick(0);
            Assert.That(client.Requests.Single().NpcId, Is.EqualTo("ren"));
            Assert.That(client.Requests.Single().Social, Is.Null);
            _world.Observe(Time(720));
            scheduler.Tick(1);
            Assert.That(client.Requests.Count, Is.EqualTo(1), "The ordinary request still occupies the only client slot.");
            Assert.That(_board.GetContext("sora").Resources.Balance, Is.EqualTo(50));

            _store.CommitCharacter(Character("sora", 0, 24));
            _world.Observe(Time(721));
            scheduler.Tick(2);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            client.Replies.Single().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(3);

            var request = client.Requests.Last();
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(request.NpcId, Is.EqualTo("sora"));
            Assert.That(request.GameTotalMinutes, Is.EqualTo(720));
            Assert.That(request.Observation.ObservedAtTotalMinutes, Is.EqualTo(721));
            Assert.That(request.Observation.Facts.Single(fact => fact.Predicate == "balance").Value, Is.EqualTo("24"));
            Assert.That(request.Social.Resources.Balance, Is.EqualTo(24), "Own terms must agree with the first observation sent to the model.");
            Assert.That(request.AllowedOperations, Is.EqualTo(new[] { "wait" }));
            client.Replies.Last().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(4);
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.decision_wait"));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            AssertAssets(2, 0, 0, 24);
        }

        [Test]
        public void PendingResourceNeedSatisfied_DiscardsTheUnsentDecisionWithoutAnotherModelCall()
        {
            _world = new NpcAgentWorld(new[] { Schedule("ren", 719), Schedule("sora") });
            _world.Observe(Time(719));
            _board = new NpcMeetingBoard(_world, new[] { new NpcMeetingPlan("fish-supply", "sora", "ren", "pond",
                "sora.rest", "ren.rest", 720, 750, 780,
                resourceTerms: new CharacterTradeTerms("ren", "sora", "fish", 1, 25)) }, resources: _trading);
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world,
                new[] { Profile("ren"), Profile("sora") }, client,
                new NpcDecisionSettings(maxConcurrentRequests: 1), meetings: _board, observe: Observe);
            scheduler.Tick(0);
            Assert.That(client.Requests.Single().NpcId, Is.EqualTo("ren"));
            _world.Observe(Time(720));
            scheduler.Tick(1);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Assert.That(_board.GetContext("sora").Kind, Is.EqualTo(NpcSocialContextKind.Opportunity));
            var revision = _world.GetState("sora").Revision;

            _store.CommitCharacter(Character("sora", 1, 50));
            _world.Observe(Time(721));
            Assert.That(_world.GetState("sora").Revision, Is.EqualTo(revision));
            Assert.That(_board.GetContext("sora"), Is.Null);
            scheduler.Tick(2);
            client.Replies.Single().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(3);
            scheduler.Tick(4);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.RequestsStarted, Is.EqualTo(1));
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            Assert.That(scheduler.GetLastOutcome("sora"), Is.Null, "No model decision was sent for the satisfied need.");
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            Assert.That(_world.GetState("sora").ActiveActivity, Is.Null);
            AssertAssets(2, 1, 0, 50);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Continuation_KeepsTheOriginalObservationAndDecisionDeadline(bool correctCandidate)
        {
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("ren") }, client, observe: Observe);
            scheduler.Tick(0);
            var first = client.Requests.Single();
            if (correctCandidate)
                client.Replies.Single().SetException(new NpcCandidateException("candidate.location_id_required"));
            else
                client.Replies.Single().SetResult(new NpcDecisionReply(NpcDecisionKind.InspectLocation, "ren.work"));
            _world.Observe(Time(721));
            scheduler.Tick(1);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var second = client.Requests.Last();
            Assert.That(second.DecisionId, Is.EqualTo(first.DecisionId));
            Assert.That(second.Step, Is.EqualTo(2));
            Assert.That(second.Observation, Is.SameAs(first.Observation));
            Assert.That(second.Observation.ObservedAtTotalMinutes, Is.EqualTo(720));
            Assert.That(second.GameTotalMinutes, Is.EqualTo(720));
            Assert.That(second.Self.Revision, Is.EqualTo(first.Self.Revision));
            if (correctCandidate)
                Assert.That(second.CandidateErrorCode, Is.EqualTo("candidate.location_id_required"));
            else
                Assert.That(second.LocationDetails.LocationId, Is.EqualTo("ren.work"));
            client.Replies.Last().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            _world.Observe(Time(722));
            scheduler.Tick(2);

            var outcome = scheduler.GetLastOutcome("ren");
            Assert.That(outcome.Code, Is.EqualTo("agent.decision_wait"), "A later sample time alone does not invalidate unchanged facts.");
            Assert.That(outcome.Context.Observation, Is.SameAs(first.Observation));
            Assert.That(outcome.Calls, Is.EqualTo(2));
            Assert.That(outcome.StartedAtSeconds, Is.Zero);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HiddenPartnerAssetChanges_KeepTheSameProjectionOperationsAndInvitationResult(bool changePartnerAssets)
        {
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("sora") }, client,
                meetings: _board, observe: Observe);
            scheduler.Tick(0);
            var request = client.Requests.Single();
            var operations = request.AllowedOperations.ToArray();
            if (changePartnerAssets) _store.CommitCharacter(Character("ren", 0, 100));
            var refreshed = Observe(request);

            Assert.That(ObservationMetadata(refreshed), Is.EqualTo(ObservationMetadata(request.Observation)));
            Assert.That(refreshed.NearbyEntityIds, Is.EqualTo(request.Observation.NearbyEntityIds));
            Assert.That(refreshed.Facts.Select(FactProjection), Is.EqualTo(request.Observation.Facts.Select(FactProjection)));
            Assert.That(refreshed.Facts.Any(fact => fact.EntityId == "ren" && fact.Source == "character_resources"), Is.False);
            Assert.That(request.AllowedOperations, Is.EqualTo(operations));
            Assert.That(operations, Is.EqualTo(new[] { "invite", "wait" }));
            Assert.That(_world.GetState("sora").Revision, Is.EqualTo(request.Self.Revision));
            var candidate = new NpcDecisionReply(NpcDecisionKind.Invite, planId: request.Social.PlanId);
            client.Replies.Single().SetResult(candidate);
            scheduler.Tick(1);

            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("meeting.invited"));
            Assert.That(outcome.Reply, Is.SameAs(candidate));
            Assert.That(outcome.Calls, Is.EqualTo(1));
            Assert.That(outcome.CandidateErrorCodes, Is.Empty);
            Assert.That(_board.GetCurrent("sora").State, Is.EqualTo(NpcMeetingState.Invited));
            AssertAssets(changePartnerAssets ? 0 : 2, 0, changePartnerAssets ? 100 : 0, 50);
        }

        [Test]
        public void RepeatedObservationRefresh_WithoutANewOpportunityDoesNotCallTheModel()
        {
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("sora") }, client,
                meetings: _board, observe: Observe);
            scheduler.Tick(0);
            var request = client.Requests.Single();
            client.Replies.Single().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(1);

            for (int i = 1; i <= 20; i++)
            {
                _world.Observe(Time(720 + i));
                _bodies[0] = new NpcObservationBody("sora", i % 2 == 0 ? 0 : 6, 0);
                var observation = Observe(request);
                Assert.That(observation.ObservedAtTotalMinutes, Is.EqualTo(720 + i));
                Assert.That(observation.X, Is.EqualTo(i % 2 == 0 ? 0 : 6));
                scheduler.Tick(1 + i);
            }

            Assert.That(scheduler.RequestsStarted, Is.EqualTo(1));
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.decision_wait"));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            AssertAssets(2, 0, 0, 50);
        }

        [Test]
        public void WorldReload_PreservesTheEpochGateAndBudgetWithObservationsEnabled()
        {
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("ren") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1, maxConcurrentRequests: 1), observe: Observe);
            scheduler.Tick(0);
            var oldRequest = client.Requests.Single();
            _world.Observe(Time(720, rebuild: 2));
            scheduler.Tick(1);

            Assert.That(client.Tokens.Single().IsCancellationRequested, Is.True);
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("agent.world_stale"));
            Assert.That(scheduler.GetLastOutcome("ren").Context.Observation, Is.SameAs(oldRequest.Observation));
            client.Replies.Single().SetResult(new NpcDecisionReply(NpcDecisionKind.Visit, "ren.work", NpcActivity.Working, 20));
            scheduler.Tick(2);
            scheduler.Tick(59);
            Assert.That(_world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            scheduler.Tick(60);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var next = client.Requests.Last();
            Assert.That(next.Self.WorldRunId, Is.Not.EqualTo(oldRequest.Self.WorldRunId));
            Assert.That(next.Observation.WorldRunId, Is.EqualTo(next.Self.WorldRunId));
            Assert.That(next.Observation.WorldRunId, Is.Not.EqualTo(oldRequest.Observation.WorldRunId));
            Assert.That(next.PreviousResultCode, Is.Null);
            client.Replies.Last().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(61);
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("agent.decision_wait"));
        }

        [TestCase(0.25, "outdoors")]
        [TestCase(4, "outdoors")]
        [TestCase(0, "sora.home")]
        public void BodyMovementDuringGeneration_RejectsTheCandidateEvenWithoutAResidentRevision(double x, string spaceId)
        {
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("sora") }, client,
                meetings: _board, observe: Observe);
            scheduler.Tick(0);
            var request = client.Requests.Single();
            var candidate = new NpcDecisionReply(NpcDecisionKind.Invite, planId: request.Social.PlanId);
            _bodies[0] = new NpcObservationBody("sora", x, 0, spaceId);
            _world.Observe(Time(721));
            Assert.That(_world.GetState("sora").Revision, Is.EqualTo(request.Self.Revision));
            client.Replies.Single().SetResult(candidate);
            scheduler.Tick(1);

            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("agent.observation_stale"));
            Assert.That(outcome.Reply, Is.SameAs(candidate));
            Assert.That(outcome.Context.Observation, Is.SameAs(request.Observation));
            Assert.That(outcome.Context.Observation.X, Is.Zero);
            Assert.That(outcome.Context.Observation.SpaceId, Is.EqualTo("outdoors"));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            AssertAssets(2, 0, 0, 50);
        }

        [TestCase("null")]
        [TestCase("throw")]
        [TestCase("wrong_observer")]
        [TestCase("wrong_world")]
        [TestCase("stale_time")]
        [TestCase("wrong_listener")]
        public void InitialObservationFailure_SkipsTheModelAndLetsAnotherResidentUseTheBudget(string failure)
        {
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("sora"), Profile("ren") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1, maxConcurrentRequests: 1), meetings: _board,
                observe: request => request.NpcId == "sora" ? InvalidObservation(request, failure) : Observe(request));

            Assert.DoesNotThrow(() => scheduler.Tick(0));

            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome.Code, Is.EqualTo("agent.observation_unavailable"));
            Assert.That(outcome.Calls, Is.Zero);
            Assert.That(outcome.Reply, Is.Null);
            Assert.That(outcome.StartedAtSeconds, Is.Zero);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(client.Requests.Single().NpcId, Is.EqualTo("ren"),
                "Failed observation must neither send Sora's request nor spend the only provider slot.");
            Assert.That(scheduler.RequestsStarted, Is.EqualTo(1));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            client.Replies.Single().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            Assert.DoesNotThrow(() => scheduler.Tick(1));
            scheduler.Tick(2);

            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("agent.decision_wait"));
            Assert.That(scheduler.ActiveRequestCount, Is.Zero);
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            AssertAssets(2, 0, 0, 50);
        }

        [TestCase("null")]
        [TestCase("throw")]
        [TestCase("wrong_observer")]
        [TestCase("wrong_world")]
        [TestCase("stale_time")]
        [TestCase("wrong_listener")]
        public void ObservationFailureBeforeExecution_RetainsTheCandidateAndReleasesTheCompletedRequest(string failure)
        {
            var client = new ControlledClient();
            bool rejectSoraObservation = false;
            using var scheduler = new NpcDecisionScheduler(_world, new[] { Profile("sora"), Profile("ren") }, client,
                new NpcDecisionSettings(maxConcurrentRequests: 1), meetings: _board,
                observe: request => request.NpcId == "sora" && rejectSoraObservation
                    ? InvalidObservation(request, failure) : Observe(request));
            scheduler.Tick(0);
            var request = client.Requests.Single();
            Assert.That(request.NpcId, Is.EqualTo("sora"));
            var candidate = new NpcDecisionReply(NpcDecisionKind.Invite, planId: request.Social.PlanId);
            client.Replies.Single().SetResult(candidate);
            rejectSoraObservation = true;

            Assert.DoesNotThrow(() => scheduler.Tick(1));

            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("agent.observation_unavailable"));
            Assert.That(outcome.Calls, Is.EqualTo(1));
            Assert.That(outcome.Reply, Is.SameAs(candidate));
            Assert.That(outcome.Context.Observation, Is.SameAs(request.Observation));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            Assert.That(_board.GetCurrent("ren"), Is.Null);
            Assert.That(_board.IsPlaceReserved("pond"), Is.False);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests.Last().NpcId, Is.EqualTo("ren"), "The completed request must release the sole client slot.");
            client.Replies.Last().SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            Assert.DoesNotThrow(() => scheduler.Tick(2));
            scheduler.Tick(3);

            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("agent.decision_wait"));
            Assert.That(scheduler.ActiveRequestCount, Is.Zero);
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("sora").Reply, Is.SameAs(candidate));
            AssertAssets(2, 0, 0, 50);
        }

        private NpcLocalObservation InvalidObservation(NpcDecisionRequest request, string failure)
        {
            if (failure == "null") return null;
            if (failure == "throw") throw new InvalidOperationException("Observation source is unavailable.");
            string observer = failure == "wrong_observer" ? "ren" : request.NpcId;
            return _scene.Read(observer,
                failure == "wrong_world" ? Guid.NewGuid() : _world.GetState(observer).WorldRunId,
                failure == "stale_time" ? _world.TotalMinutes - 1 : _world.TotalMinutes, _bodies,
                social: failure == "wrong_listener" ? null : request.Social,
                ownResources: _trading.Inspect(request.Social?.Resources?.Terms, observer), memories: _board.GetMemories(observer));
        }

        private NpcLocalObservation Observe(NpcDecisionRequest request)
            => _scene.Read(request.NpcId, _world.GetState(request.NpcId).WorldRunId, _world.TotalMinutes, _bodies,
                social: request.Social, ownResources: _trading.Inspect(request.Social?.Resources?.Terms, request.NpcId),
                memories: _board.GetMemories(request.NpcId));

        private static object ObservationMetadata(NpcLocalObservation observation) => new
        {
            observation.ObserverId, observation.WorldRunId, observation.ObservedAtTotalMinutes,
            observation.X, observation.Y, observation.SpaceId, observation.RegionId, observation.Radius,
            observation.NearbyComplete, observation.CoverageDomain, observation.ListenerId
        };

        private static object FactProjection(NpcObservationFact fact) => new
        {
            fact.FactId, fact.EntityId, fact.Predicate, fact.Value, fact.ValueType, fact.Unit, fact.Knowledge,
            fact.Source, fact.ObserverId, fact.ObservedAtTotalMinutes, fact.Scope, fact.CanExpress, fact.SpeakerId
        };

        private void AssertAssets(int renFish, int soraFish, int renCoins, int soraCoins)
        {
            Assert.That(_store.TryGetCharacter("ren", out var ren), Is.True);
            Assert.That(_store.TryGetCharacter("sora", out var sora), Is.True);
            Assert.That(ren.Backpack.Items.Sum(item => item.Quantity), Is.EqualTo(renFish));
            Assert.That(sora.Backpack.Items.Sum(item => item.Quantity), Is.EqualTo(soraFish));
            Assert.That(ren.Wallet.Balance, Is.EqualTo(renCoins));
            Assert.That(sora.Wallet.Balance, Is.EqualTo(soraCoins));
        }

        private sealed class ControlledClient : INpcDecisionClient
        {
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal readonly List<TaskCompletionSource<NpcDecisionReply>> Replies = new List<TaskCompletionSource<NpcDecisionReply>>();
            internal readonly List<CancellationToken> Tokens = new List<CancellationToken>();

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                Tokens.Add(cancellationToken);
                var reply = new TaskCompletionSource<NpcDecisionReply>();
                Replies.Add(reply);
                return reply.Task;
            }
        }

        private static NpcDefinition Profile(string id) => new NpcDefinition(id, id, "Resident", "Hello");
        private static NpcDailySchedule Schedule(string id, int restMinute = 720) => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
            id + ".work", id + ".rest", id + ".afternoon", 360, 480, restMinute, 810, 1020, 1080);
        private static WorldTimeProgress Time(int minute, long rebuild = 1)
            => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, rebuild);
        private static CharacterEconomySnapshot Character(string id, int fish, int coins) => new CharacterEconomySnapshot(id,
            new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>() : new[] { new ItemStack("fish", fish) }), new WalletSnapshot(coins));
    }
}
