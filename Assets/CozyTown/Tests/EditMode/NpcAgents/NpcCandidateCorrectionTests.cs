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
    public sealed class NpcCandidateCorrectionTests
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
            var trading = new CharacterResourceTrading(_store,
                new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            _board = new NpcMeetingBoard(_world, new[] { new NpcMeetingPlan("fish-supply", "sora", "ren", "pond",
                "sora.rest", "ren.rest", 720, 750, 780, resourceTerms: new CharacterTradeTerms("ren", "sora", "fish", 1, 25)) },
                resources: trading);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingInvitationField_CanBeCorrectedWithinTheOriginalOpportunity(bool synchronous)
        {
            var client = new ControlledClient(synchronous);
            using var scheduler = Scheduler(client);
            scheduler.Tick(0);
            var first = client.Requests.Single();
            if (!synchronous) client.Fail(0, "candidate.plan_id_required");
            scheduler.Tick(1);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var correction = client.Requests.Last();
            Assert.That(correction.DecisionId, Is.EqualTo(first.DecisionId));
            Assert.That(correction.Step, Is.EqualTo(2));
            Assert.That(correction.GameTotalMinutes, Is.EqualTo(first.GameTotalMinutes));
            Assert.That(correction.Self, Is.SameAs(first.Self));
            Assert.That(correction.Social, Is.SameAs(first.Social));
            Assert.That(correction.AllowedOperations, Is.EqualTo(first.AllowedOperations));
            Assert.That(first.CandidateErrorCode, Is.Null);
            Assert.That(correction.CandidateErrorCode, Is.EqualTo("candidate.plan_id_required"));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            AssertUnchangedAssets();

            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Invite, planId: "fish-supply"));
            scheduler.Tick(2);
            Assert.That(_board.GetCurrent("sora").State, Is.EqualTo(NpcMeetingState.Invited));
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("meeting.invited"));
            Assert.That(scheduler.GetLastOutcome("sora").Calls, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("sora").CandidateErrorCodes, Is.EqualTo(new[] { "candidate.plan_id_required" }));
            AssertUnchangedAssets();
        }

        [Test]
        public void SuccessfulQuery_ClearsCorrectionFeedbackWithoutAllowingAnotherCorrection()
        {
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(_world,
                new[] { new NpcDefinition("sora", "Sora", "Cook", "Hello") }, client,
                new NpcDecisionSettings(maxCallsPerDecision: 4));
            scheduler.Tick(0);
            client.Fail(0, "candidate.location_id_required");
            scheduler.Tick(1);
            Assert.That(client.Requests[1].CandidateErrorCode, Is.EqualTo("candidate.location_id_required"));
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.InspectLocation, "sora.work"));
            scheduler.Tick(2);
            Assert.That(client.Requests.Count, Is.EqualTo(3));
            Assert.That(client.Requests[2].DecisionId, Is.EqualTo(client.Requests[0].DecisionId));
            Assert.That(client.Requests[2].LocationDetails, Is.Not.Null);
            Assert.That(client.Requests[2].CandidateErrorCode, Is.Null);

            client.Fail(2, "candidate.duration_invalid");
            scheduler.Tick(3);
            Assert.That(client.Requests.Count, Is.EqualTo(3));
            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("agent.response_invalid"));
            Assert.That(outcome.Calls, Is.EqualTo(3));
            Assert.That(outcome.CandidateErrorCodes, Is.EqualTo(new[]
                { "candidate.location_id_required", "candidate.duration_invalid" }));
            AssertUnchangedAssets();
        }

        private NpcDecisionScheduler Scheduler(INpcDecisionClient client, NpcDecisionSettings settings = null)
            => new NpcDecisionScheduler(_world, new[] { new NpcDefinition("sora", "Sora", "Cook", "Hello") },
                client, settings, _board);

        private void AssertUnchangedAssets()
        {
            _store.TryGetCharacter("ren", out var ren);
            _store.TryGetCharacter("sora", out var sora);
            Assert.That(ren.Backpack.Items.Single().Quantity, Is.EqualTo(2));
            Assert.That(sora.Backpack.Items, Is.Empty);
            Assert.That(ren.Wallet.Balance, Is.Zero);
            Assert.That(sora.Wallet.Balance, Is.EqualTo(50));
        }

        private sealed class ControlledClient : INpcDecisionClient
        {
            private readonly bool _synchronous;
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal readonly List<TaskCompletionSource<NpcDecisionReply>> Replies = new List<TaskCompletionSource<NpcDecisionReply>>();
            internal readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            internal ControlledClient(bool synchronous = false) => _synchronous = synchronous;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                Requests.Add(request); Tokens.Add(token);
                var reply = new TaskCompletionSource<NpcDecisionReply>();
                Replies.Add(reply);
                if (_synchronous && Requests.Count == 1) throw new NpcCandidateException("candidate.plan_id_required");
                return reply.Task;
            }
            internal void Fail(int index, string code) => Replies[index].SetException(new NpcCandidateException(code));
            internal void Complete(int index, NpcDecisionReply reply) => Replies[index].SetResult(reply);
        }

        private static CharacterEconomySnapshot Character(string id, int fish, int coins)
            => new CharacterEconomySnapshot(id, new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>()
                : new[] { new ItemStack("fish", fish) }), new WalletSnapshot(coins));
        private static NpcDailySchedule Schedule(string id) => new NpcDailySchedule(id, id + ".home", id + ".outside",
            id + ".entry", id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 810, 1020, 1080);
        private static WorldTimeProgress Time(int minute, long rebuild = 1)
            => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, rebuild);
    }
}
