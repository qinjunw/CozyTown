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
