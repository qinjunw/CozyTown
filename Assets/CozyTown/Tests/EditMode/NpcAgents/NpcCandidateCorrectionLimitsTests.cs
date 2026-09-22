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
    public sealed class NpcCandidateCorrectionLimitsTests
    {
        private NpcAgentWorld _world;
        private NpcMeetingBoard _board;
        private IEconomyStateStore _store;

        [SetUp]
        public void SetUp()
        {
            _world = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora") });
            _world.Observe(Time(720));
            _store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) },
                Array.Empty<ShopEconomySnapshot>());
            var resources = new CharacterResourceTrading(_store,
                new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            _board = new NpcMeetingBoard(_world, new[] { new NpcMeetingPlan("fish-supply", "sora", "ren", "pond",
                "sora.rest", "ren.rest", 720, 750, 780, resourceTerms: new CharacterTradeTerms("ren", "sora", "fish", 1, 25)) },
                resources: resources);
        }

        [Test]
        public void AOneCallDecision_RecordsTheFieldErrorWithoutRequestingCorrection()
        {
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxCallsPerDecision: 1));
            scheduler.Tick(0);
            client.Fail(0, "candidate.plan_id_required");

            scheduler.Tick(1);
            scheduler.Tick(2);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("agent.response_invalid"));
            Assert.That(outcome.Calls, Is.EqualTo(1));
            Assert.That(outcome.CandidateErrorCodes, Is.EqualTo(new[] { "candidate.plan_id_required" }));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            AssertAssets(50);
        }

        [Test]
        public void ASecondMalformedCandidate_StopsEvenWhenMoreCallsAreConfigured()
        {
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxCallsPerDecision: 5));
            scheduler.Tick(0);
            client.Fail(0, "candidate.plan_id_required");
            scheduler.Tick(1);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            client.Fail(1, "candidate.plan_id_required");

            scheduler.Tick(2);
            scheduler.Tick(3);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("agent.response_invalid"));
            Assert.That(outcome.Calls, Is.EqualTo(2));
            Assert.That(outcome.CandidateErrorCodes,
                Is.EqualTo(new[] { "candidate.plan_id_required", "candidate.plan_id_required" }));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            AssertAssets(50);
        }

        [TestCase("candidate.schema_mismatch")]
        [TestCase("candidate.operation_unavailable")]
        [TestCase("candidate.plan_id_mismatch")]
        [TestCase("candidate.meeting_id_mismatch")]
        [TestCase("candidate.location_unknown")]
        public void AnInvalidBindingOrOperation_DoesNotReceiveAFieldCorrection(string code)
        {
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxCallsPerDecision: 5));
            scheduler.Tick(0);
            client.Fail(0, code);

            scheduler.Tick(1);
            scheduler.Tick(2);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.response_invalid"));
            Assert.That(scheduler.GetLastOutcome("sora").CandidateErrorCodes, Is.EqualTo(new[] { code }));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            AssertAssets(50);
        }

        [Test]
        public void AFieldErrorAfterLocationInspection_CannotExceedTheSharedCallBudget()
        {
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxCallsPerDecision: 2), withMeetings: false);
            scheduler.Tick(0);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.InspectLocation, "sora.rest"));
            scheduler.Tick(1);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].LocationDetails.LocationId, Is.EqualTo("sora.rest"));
            client.Fail(1, "candidate.location_id_required");

            scheduler.Tick(2);
            scheduler.Tick(3);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.response_invalid"));
            Assert.That(scheduler.GetLastOutcome("sora").Calls, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("sora").CandidateErrorCodes, Is.EqualTo(new[] { "candidate.location_id_required" }));
            Assert.That(_world.GetState("sora").ActiveActivity, Is.Null);
        }

        [Test]
        public void LocationInspectionAfterCorrection_CannotStartAThirdCall()
        {
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxCallsPerDecision: 2), withMeetings: false);
            scheduler.Tick(0);
            client.Fail(0, "candidate.location_id_required");
            scheduler.Tick(1);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.InspectLocation, "sora.rest"));

            scheduler.Tick(2);
            scheduler.Tick(3);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.decision_step_limit"));
            Assert.That(scheduler.GetLastOutcome("sora").Calls, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("sora").CandidateErrorCodes, Is.EqualTo(new[] { "candidate.location_id_required" }));
            Assert.That(_world.GetState("sora").ActiveActivity, Is.Null);
        }

        [Test]
        public void ACorrection_WaitsForTheRollingBudgetAndKeepsTheOriginalStartTime()
        {
            var client = new ControlledClient();
            using var scheduler = Scheduler(client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1, decisionTimeoutSeconds: 70));
            scheduler.Tick(0);
            client.Fail(0, "candidate.plan_id_required");
            scheduler.Tick(1);
            scheduler.Tick(59.9);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Assert.That(scheduler.ActiveRequestCount, Is.Zero);

            scheduler.Tick(60);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].DecisionId, Is.EqualTo(client.Requests[0].DecisionId));
            Assert.That(client.Requests[1].GameTotalMinutes, Is.EqualTo(client.Requests[0].GameTotalMinutes));
            Assert.That(scheduler.RequestsInLastMinute, Is.EqualTo(1));
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(60.5);
            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("agent.decision_wait"));
            Assert.That(outcome.StartedAtSeconds, Is.Zero);
            Assert.That(outcome.FinishedAtSeconds, Is.EqualTo(60.5));
            Assert.That(outcome.Calls, Is.EqualTo(2));
            AssertAssets(50);
        }

        [Test]
        public void ACorrectionWaitingForBudget_ExpiresAtTheOriginalDecisionDeadline()
        {
            var client = new ControlledClient();
            using var scheduler = Scheduler(client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1, decisionTimeoutSeconds: 12));
            scheduler.Tick(0);
            client.Fail(0, "candidate.plan_id_required");
            scheduler.Tick(1);
            scheduler.Tick(11.9);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));

            scheduler.Tick(12);
            scheduler.Tick(60);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("agent.decision_timeout"));
            Assert.That(outcome.StartedAtSeconds, Is.Zero);
            Assert.That(outcome.FinishedAtSeconds, Is.EqualTo(12));
            Assert.That(outcome.Calls, Is.EqualTo(1));
            Assert.That(outcome.CandidateErrorCodes, Is.EqualTo(new[] { "candidate.plan_id_required" }));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            AssertAssets(50);
        }

        [TestCase("world", "agent.world_stale")]
        [TestCase("revision", "agent.decision_stale")]
        [TestCase("opportunity", "agent.opportunity_expired")]
        [TestCase("deadline", "agent.decision_timeout")]
        public void AnInvalidatedCorrection_CancelsAndDiscardsItsLateInvitation(string cause, string expectedCode)
        {
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(requestTimeoutSeconds: 20));
            scheduler.Tick(0);
            client.Fail(0, "candidate.plan_id_required");
            scheduler.Tick(1);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            double invalidAt = 2;
            if (cause == "world") _world.Observe(Time(720, rebuild: 2));
            else if (cause == "revision")
            {
                var state = _world.GetState("sora");
                Assert.That(_world.SubmitActivity(new NpcActivityRequest("sora", state.WorldRunId, state.Revision,
                    "sora.work", NpcActivity.Working, 800)).IsSuccess, Is.True);
            }
            else if (cause == "opportunity") _world.Observe(Time(750));
            else invalidAt = 12;

            scheduler.Tick(invalidAt);

            Assert.That(client.Tokens[1].IsCancellationRequested, Is.True);
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo(expectedCode));
            Assert.That(scheduler.GetLastOutcome("sora").Calls, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("sora").CandidateErrorCodes, Is.EqualTo(new[] { "candidate.plan_id_required" }));
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1), "An unfinished external request retains its concurrency slot.");
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Invite, planId: "fish-supply"));
            scheduler.Tick(invalidAt + 0.1);
            Assert.That(scheduler.ActiveRequestCount, Is.Zero);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo(expectedCode));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            Assert.That(_board.GetCurrent("ren"), Is.Null);
            AssertAssets(50);
        }

        [Test]
        public void ACorrectedInvitation_RechecksFundsChangedDuringTheCorrection()
        {
            var client = new ControlledClient();
            using var scheduler = Scheduler(client);
            scheduler.Tick(0);
            client.Fail(0, "candidate.plan_id_required");
            scheduler.Tick(1);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var correction = client.Requests[1];
            Assert.That(correction.Social.Resources.Balance, Is.EqualTo(50));
            Assert.That(correction.AllowedOperations, Does.Contain("invite"));
            Assert.That(_store.CommitCharacter(Character("sora", 0, 24)).IsSuccess, Is.True);
            Assert.That(_world.GetState("sora").Revision, Is.EqualTo(correction.Self.Revision));

            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Invite, planId: correction.Social.PlanId));
            scheduler.Tick(2);

            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("wallet.insufficient_funds"));
            Assert.That(outcome.Calls, Is.EqualTo(2));
            Assert.That(outcome.CandidateErrorCodes, Is.EqualTo(new[] { "candidate.plan_id_required" }));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            Assert.That(_board.GetCurrent("ren"), Is.Null);
            Assert.That(_board.IsPlaceReserved("pond"), Is.False);
            AssertAssets(24);
        }

        private NpcDecisionScheduler Scheduler(INpcDecisionClient client, NpcDecisionSettings settings = null,
            bool withMeetings = true)
            => new NpcDecisionScheduler(_world, new[] { new NpcDefinition("sora", "Sora", "Cook", "Hello") },
                client, settings, withMeetings ? _board : null);

        private void AssertAssets(int soraCoins)
        {
            Assert.That(_store.TryGetCharacter("ren", out var ren), Is.True);
            Assert.That(_store.TryGetCharacter("sora", out var sora), Is.True);
            Assert.That(ren.Backpack.Items.Single().Quantity, Is.EqualTo(2));
            Assert.That(sora.Backpack.Items, Is.Empty);
            Assert.That(ren.Wallet.Balance, Is.Zero);
            Assert.That(sora.Wallet.Balance, Is.EqualTo(soraCoins));
        }

        private sealed class ControlledClient : INpcDecisionClient
        {
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal readonly List<TaskCompletionSource<NpcDecisionReply>> Replies = new List<TaskCompletionSource<NpcDecisionReply>>();
            internal readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                Requests.Add(request);
                Tokens.Add(token);
                var reply = new TaskCompletionSource<NpcDecisionReply>();
                Replies.Add(reply);
                return reply.Task;
            }
            internal void Fail(int index, string code) => Replies[index].SetException(new NpcCandidateException(code));
            internal void Complete(int index, NpcDecisionReply reply) => Replies[index].SetResult(reply);
        }

        private static CharacterEconomySnapshot Character(string id, int fish, int coins)
            => new CharacterEconomySnapshot(id,
                new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>() : new[] { new ItemStack("fish", fish) }),
                new WalletSnapshot(coins));

        private static NpcDailySchedule Schedule(string id)
            => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest",
                id + ".afternoon", 360, 480, 720, 810, 1020, 1080);

        private static WorldTimeProgress Time(int minute, long rebuild = 1)
            => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, rebuild);
    }
}
