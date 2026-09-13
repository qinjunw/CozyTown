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
    public sealed class NpcResourcePreconditionEdgeTests
    {
        private NpcAgentWorld _world;
        private NpcMeetingBoard _board;
        private IEconomyStateStore _store;
        private bool _arrived;

        [Test]
        public void DeliveringTheLastFishAndAllCoins_AllowsBothResidentsToSpeakAndComplete()
        {
            CreateWorld(1, 25);
            var id = AcceptAndArrive();
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.True);
            AssertAssets(0, 1, 25, 0);
            var client = new RecordingClient(request => Task.FromResult(new NpcDecisionReply(
                NpcDecisionKind.Speak, meetingId: request.Social.MeetingId, text: "The agreed exchange is recorded.")));
            using var scheduler = CreateScheduler(client, "sora", "ren");

            scheduler.Tick(0);
            scheduler.Tick(0.1);
            scheduler.Tick(0.2);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            foreach (string npc in new[] { "sora", "ren" })
            {
                var request = client.Requests.Single(item => item.NpcId == npc);
                Assert.That(request.Social.Kind, Is.EqualTo(NpcSocialContextKind.Conversation));
                Assert.That(request.Social.DeliveryResultCode, Is.EqualTo("resource.delivered"));
                Assert.That(request.HasSelfAssessment, Is.False);
                Assert.That(request.AllowedOperations, Does.Contain("say"));
                Assert.That(_world.GetState(npc).ActiveActivity, Is.Null);
            }
            Assert.That(_board.GetLatest("sora").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(_board.GetLatest("sora").Transcript.Select(line => line.SpeakerId), Is.EqualTo(new[] { "sora", "ren" }));
            AssertAssets(0, 1, 25, 0);
        }

        [Test]
        public void WaitingForAnUnaffordableOpportunity_DoesNotRetryWhenFundsIncreaseThatDay()
        {
            CreateWorld(1, 24);
            var client = new RecordingClient(request => Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Wait)));
            using var scheduler = CreateScheduler(client, "sora");
            scheduler.Tick(0);
            scheduler.Tick(0.1);
            Assert.That(client.Requests.Single().AllowedOperations, Is.EqualTo(new[] { "wait" }));
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.decision_wait"));
            Assert.That(_store.CommitCharacter(Character("sora", 0, 25)).IsSuccess, Is.True);

            scheduler.Tick(31);
            _world.Observe(Time(740));
            scheduler.Tick(61);
            _world.Observe(Time(779));
            scheduler.Tick(91);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(_board.GetContext("sora"), Is.Null);
            Assert.That(_board.GetLatest("sora"), Is.Null);
            Assert.That(_world.GetState("sora").ActiveActivity, Is.Null);
            Assert.That(_world.GetState("ren").ActiveActivity, Is.Null);
            AssertAssets(1, 0, 0, 25);
        }

        [Test]
        public void AcceptingWithoutStock_IsRejectedAndExpiresWithoutAnInventedDecline()
        {
            CreateWorld(0, 25);
            var invited = _board.Invite(_world.GetState("sora"), "fish-supply");
            Assert.That(invited.IsSuccess, Is.True);
            var client = new RecordingClient(request => Task.FromResult(new NpcDecisionReply(
                NpcDecisionKind.AcceptInvitation, meetingId: request.Social.MeetingId)));
            using var scheduler = CreateScheduler(client, "ren");
            scheduler.Tick(0);
            scheduler.Tick(0.1);

            Assert.That(client.Requests.Single().AllowedOperations, Is.EqualTo(new[] { "decline_invite" }));
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("agent.operation_unavailable"));
            Assert.That(_board.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Invited));
            _world.Observe(Time(780));
            scheduler.Tick(1);

            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Expired));
            Assert.That(_board.GetMemories("ren").Select(item => item.Kind), Does.Not.Contain("meeting.declined"));
            Assert.That(_board.GetMemories("ren").Select(item => item.Kind), Does.Contain("meeting.expired"));
            Assert.That(_board.GetCurrent("ren"), Is.Null);
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            Assert.That(_board.IsPlaceReserved("pond"), Is.False);
            Assert.That(_world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(_world.GetState("sora").ActiveActivity, Is.Null);
            AssertAssets(0, 0, 0, 25);
        }

        [Test]
        public void FundsIncreasingDuringAnOutstandingRequest_CannotExpandItsAllowedOperations()
        {
            CreateWorld(1, 24);
            var pending = new TaskCompletionSource<NpcDecisionReply>();
            var client = new RecordingClient(request => pending.Task);
            using var scheduler = CreateScheduler(client, "sora");
            scheduler.Tick(0);
            var request = client.Requests.Single();
            Assert.That(request.AllowedOperations, Is.EqualTo(new[] { "wait" }));
            Assert.That(request.Social.Resources.Balance, Is.EqualTo(24));
            Assert.That(_store.CommitCharacter(Character("sora", 0, 25)).IsSuccess, Is.True);
            Assert.That(_world.GetState("sora").Revision, Is.EqualTo(request.Self.Revision));

            pending.SetResult(new NpcDecisionReply(NpcDecisionKind.Invite, planId: request.Social.PlanId));
            scheduler.Tick(0.1);

            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.operation_unavailable"));
            Assert.That(request.AllowedOperations, Is.EqualTo(new[] { "wait" }));
            Assert.That(request.Social.Resources.Balance, Is.EqualTo(24));
            Assert.That(_board.GetCurrent("sora"), Is.Null);
            Assert.That(_board.GetCurrent("ren"), Is.Null);
            AssertAssets(1, 0, 0, 25);
        }

        [Test]
        public void ZeroPriceTerms_AllowAZeroBalanceBuyerToInviteAndReceiveTheFish()
        {
            CreateWorld(1, 0, price: 0);
            _board.Observe();
            var resources = _board.GetContext("sora").Resources;
            Assert.That(resources.Balance, Is.Zero);
            Assert.That(resources.Terms.TotalPrice, Is.Zero);
            Assert.That(resources.CanMeetKnownTerms, Is.True);
            Assert.That(resources.MissingCoins, Is.Zero);

            var id = AcceptAndArrive();
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.True);

            Assert.That(_board.GetLatest("sora").DeliveryResultCode, Is.EqualTo("resource.delivered"));
            AssertAssets(0, 1, 0, 0);
        }

        private void CreateWorld(int fish, int balance, int price = 25)
        {
            _arrived = false;
            _world = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora") });
            _world.Observe(Time(720));
            _store = new InMemoryEconomyStateStore(new[] { Character("ren", fish, 0), Character("sora", 0, balance) },
                Array.Empty<ShopEconomySnapshot>());
            var resources = new CharacterResourceTrading(_store,
                new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            var plan = new NpcMeetingPlan("fish-supply", "sora", "ren", "pond", "sora.rest", "ren.rest",
                720, 750, 780, maxTurns: 2, resourceTerms: new CharacterTradeTerms("ren", "sora", "fish", 1, price));
            _board = new NpcMeetingBoard(_world, new[] { plan },
                (npc, location) => _arrived ? NpcMeetingPresence.Arrived : NpcMeetingPresence.Travelling, resources);
        }

        private NpcDecisionScheduler CreateScheduler(INpcDecisionClient client, params string[] ids)
            => new NpcDecisionScheduler(_world, ids.Select(id => new NpcDefinition(id, id, "Resident", "Hello")),
                client, meetings: _board);

        private Guid AcceptAndArrive()
        {
            var invited = _board.Invite(_world.GetState("sora"), "fish-supply");
            Assert.That(invited.IsSuccess, Is.True);
            Assert.That(_board.Respond(_world.GetState("ren"), invited.Value.Id, true).IsSuccess, Is.True);
            _arrived = true;
            _world.Observe(Time(750));
            _board.Observe();
            return invited.Value.Id;
        }

        private void AssertAssets(int renFish, int soraFish, int renCoins, int soraCoins)
        {
            Assert.That(_store.TryGetCharacter("ren", out var ren), Is.True);
            Assert.That(_store.TryGetCharacter("sora", out var sora), Is.True);
            Assert.That(ren.Backpack.Items.Where(item => item.ItemId == "fish").Sum(item => item.Quantity), Is.EqualTo(renFish));
            Assert.That(sora.Backpack.Items.Where(item => item.ItemId == "fish").Sum(item => item.Quantity), Is.EqualTo(soraFish));
            Assert.That(ren.Wallet.Balance, Is.EqualTo(renCoins));
            Assert.That(sora.Wallet.Balance, Is.EqualTo(soraCoins));
        }

        private sealed class RecordingClient : INpcDecisionClient
        {
            private readonly Func<NpcDecisionRequest, Task<NpcDecisionReply>> _respond;
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal RecordingClient(Func<NpcDecisionRequest, Task<NpcDecisionReply>> respond) => _respond = respond;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return _respond(request);
            }
        }

        private static CharacterEconomySnapshot Character(string id, int fish, int coins)
            => new CharacterEconomySnapshot(id,
                new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>() : new[] { new ItemStack("fish", fish) }),
                new WalletSnapshot(coins));

        private static NpcDailySchedule Schedule(string id)
            => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest",
                id + ".afternoon", 360, 480, 720, 810, 1020, 1080);

        private static WorldTimeProgress Time(int minute)
            => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, 1);
    }
}
