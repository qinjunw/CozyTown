using System;
using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Npc;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcResourceMeetingTests
    {
        private NpcAgentWorld _world;
        private NpcMeetingBoard _board;
        private IEconomyStateStore _store;
        private bool _arrived;

        [SetUp]
        public void SetUp()
        {
            _arrived = false;
            _world = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora") });
            _world.Observe(Time(720));
            _store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) }, Array.Empty<ShopEconomySnapshot>());
            var trading = new CharacterResourceTrading(_store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            _board = new NpcMeetingBoard(_world, new[] { Plan() }, (npc, location) => _arrived ? NpcMeetingPresence.Arrived : NpcMeetingPresence.Travelling, trading);
        }

        [Test]
        public void RebindingAnotherWorld_UsesThatWorldsAssetsForNewAgreements()
        {
            var previousStore = _store;
            _store = new InMemoryEconomyStateStore(new[] { Character("ren", 4, 0), Character("sora", 0, 100) }, Array.Empty<ShopEconomySnapshot>());
            var next = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora") });
            next.Observe(Time(720));
            _board.BindWorld(next, new CharacterResourceTrading(_store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2));
            _world = next;
            var id = AcceptAndArrive();
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.True);
            AssertAssets(3, 1, 25, 75);
            previousStore.TryGetCharacter("sora", out var previousSora);
            Assert.That(previousSora.Wallet.Balance, Is.EqualTo(50));
            Assert.That(previousSora.Backpack.Items, Is.Empty);
        }

        [Test]
        public void DecliningTheOpportunity_ReleasesThePartnersOrdinaryDecision()
        {
            var client = new ResourceClient(waitForOpportunity: true);
            using var scheduler = new NpcDecisionScheduler(_world, new[] { new NpcDefinition("ren", "Ren", "Fisher", "Hello"),
                new NpcDefinition("sora", "Sora", "Cook", "Hello") }, client, meetings: _board);
            scheduler.Tick(0); scheduler.Tick(0.1); scheduler.Tick(0.2);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests.Last().NpcId, Is.EqualTo("ren"));
            Assert.That(client.Requests.Last().Social, Is.Null);
            Assert.That(_board.GetCurrent("sora"), Is.Null);
        }

        [Test]
        public void PendingResourceContact_DefersThePartnersOrdinaryDecision()
        {
            var client = new ResourceClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { new NpcDefinition("ren", "Ren", "Fisher", "Hello"),
                new NpcDefinition("sora", "Sora", "Cook", "Hello") }, client, meetings: _board);
            scheduler.Tick(0);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(client.Requests[0].NpcId, Is.EqualTo("sora"));
            scheduler.Tick(0.1); scheduler.Tick(0.2); scheduler.Tick(0.3);
            Assert.That(_board.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Scheduled));
            Assert.That(client.Requests.All(item => item.Social != null), Is.True);
        }

        [Test]
        public void ResourceOpportunity_AutomaticallyInvitesAcceptsDeliversAndReturnsToSchedule()
        {
            var client = new ResourceClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { new NpcDefinition("sora", "Sora", "Cook", "Hello"),
                new NpcDefinition("ren", "Ren", "Fisher", "Hello") }, client, meetings: _board);
            scheduler.Tick(0); scheduler.Tick(0.1); scheduler.Tick(0.2); scheduler.Tick(0.3);
            _arrived = true;
            _world.Observe(Time(750));
            for (int i = 0; i < 10; i++) scheduler.Tick(1 + i * 0.1);
            Assert.That(_board.GetLatest("sora").State, Is.EqualTo(NpcMeetingState.Completed));
            AssertAssets(1, 1, 25, 25);
            var delivery = client.Requests.Single(item => item.Social?.Kind == NpcSocialContextKind.Delivery);
            Assert.That(delivery.AllowedOperations, Is.EquivalentTo(new[] { "deliver", "cancel_exchange" }));
            Assert.That(_world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(_world.GetState("sora").ActiveActivity, Is.Null);
        }

        [Test]
        public void ExistingFish_SuppressesTheBuiltInNeedAndAStaleInvitation()
        {
            _board.Observe();
            Assert.That(_board.GetContext("sora").Resources.OwnedQuantity, Is.Zero);
            _store.CommitCharacter(Character("sora", 1, 50));
            Assert.That(_board.GetContext("sora"), Is.Null);
            Assert.That(_board.Invite(_world.GetState("sora"), "fish-supply").ErrorCode, Is.EqualTo("resource.need_satisfied"));
            AssertAssets(2, 1, 0, 50);
        }

        [Test]
        public void Context_DisclosesOnlyTheActorsRelevantOwnedAssets()
        {
            _board.Observe();
            var sora = _board.GetContext("sora").Resources;
            Assert.That(sora.OwnedQuantity, Is.Zero);
            Assert.That(sora.Balance, Is.EqualTo(50));
            _board.Invite(_world.GetState("sora"), "fish-supply");
            var ren = _board.GetContext("ren").Resources;
            Assert.That(ren.OwnedQuantity, Is.EqualTo(2));
            Assert.That(ren.Balance, Is.Zero);
            Assert.That(ren.Terms.TotalPrice, Is.EqualTo(25));
            Assert.That(sora.OwnedQuantity, Is.Zero);
        }

        [Test]
        public void Delivery_RechecksResourcesAndCancelsWithoutPartialTransfer()
        {
            var id = AcceptAndArrive();
            _store.CommitCharacter(Character("ren", 0, 0));
            Assert.That(_board.Deliver(_world.GetState("sora"), id).ErrorCode, Is.EqualTo("inventory.insufficient_quantity"));
            AssertAssets(0, 0, 0, 50);
            Assert.That(_board.GetLatest("sora").State, Is.EqualTo(NpcMeetingState.Cancelled));
            Assert.That(_board.GetLatest("sora").DeliveryResultCode, Is.EqualTo("inventory.insufficient_quantity"));
            Assert.That(_world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(_world.GetState("sora").ActiveActivity, Is.Null);
        }

        [Test]
        public void SpeechBeforeDelivery_CannotClaimOrCompleteATransfer()
        {
            var id = AcceptAndArrive();
            Assert.That(_board.Speak(_world.GetState("sora"), id, "The fish is already in my bag.").ErrorCode, Is.EqualTo("resource.not_delivered"));
            Assert.That(_board.EndConversation(_world.GetState("sora"), id).IsSuccess, Is.False);
            Assert.That(_board.GetLatest("sora").Transcript, Is.Empty);
            AssertAssets(2, 0, 0, 50);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DecliningOrCancelling_TransfersNothing(bool accepted)
        {
            var id = _board.Invite(_world.GetState("sora"), "fish-supply").Value.Id;
            _board.Respond(_world.GetState("ren"), id, accepted);
            if (accepted) Assert.That(_board.CancelExchange(_world.GetState("ren"), id).IsSuccess, Is.True);
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.False);
            AssertAssets(2, 0, 0, 50);
            Assert.That(_world.GetState("ren").ActiveActivity, Is.Null);
        }

        [Test]
        public void InvitationAcceptanceAndTravel_AreInsufficientToTransfer()
        {
            var id = _board.Invite(_world.GetState("sora"), "fish-supply").Value.Id;
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.False);
            _board.Respond(_world.GetState("ren"), id, true);
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.False);
            _world.Observe(Time(750)); _board.Observe();
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.False);
            AssertAssets(2, 0, 0, 50);
        }

        [Test]
        public void OldTimelineAndDepartedBodies_CannotDeliver()
        {
            var id = AcceptAndArrive();
            _arrived = false;
            Assert.That(_board.Deliver(_world.GetState("sora"), id).ErrorCode, Is.EqualTo("meeting.not_arrived"));
            _arrived = true;
            _world.Observe(Time(750, 2));
            Assert.That(_board.Deliver(_world.GetState("sora"), id).ErrorCode, Is.EqualTo("agent.world_stale"));
            _board.BindWorld(_world);
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.False);
            AssertAssets(2, 0, 0, 50);
        }

        [Test]
        public void CompletedMeeting_RetainsReceiptAndRejectsNewTransfersWithItsIdentifier()
        {
            var id = AcceptAndArrive();
            _board.Deliver(_world.GetState("sora"), id);
            _board.Speak(_world.GetState("sora"), id, "I have the fish.");
            _board.Speak(_world.GetState("ren"), id, "The payment arrived.");
            Assert.That(_board.GetLatest("sora").DeliveryResultCode, Is.EqualTo("resource.delivered"));
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.True);
            AssertAssets(1, 1, 25, 25);
        }

        [Test]
        public void AcceptedAndArrivedPair_DeliversOnceAndRecordsTheActualResult()
        {
            var id = AcceptAndArrive();
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.True);
            AssertAssets(1, 1, 25, 25);
            Assert.That(_board.GetMemories("sora").Any(item => item.Kind == "resource.delivered"), Is.True);
            Assert.That(_board.Deliver(_world.GetState("sora"), id).IsSuccess, Is.True);
            AssertAssets(1, 1, 25, 25);
        }

        private sealed class ResourceClient : INpcDecisionClient
        {
            private readonly bool _waitForOpportunity;
            internal ResourceClient(bool waitForOpportunity = false) => _waitForOpportunity = waitForOpportunity;
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                var social = request.Social;
                var reply = social == null || (_waitForOpportunity && social.Kind == NpcSocialContextKind.Opportunity) ? new NpcDecisionReply(NpcDecisionKind.Wait)
                    : social.Kind == NpcSocialContextKind.Opportunity ? new NpcDecisionReply(NpcDecisionKind.Invite, planId: social.PlanId)
                    : social.Kind == NpcSocialContextKind.Invitation ? new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: social.MeetingId)
                    : social.Kind == NpcSocialContextKind.Delivery ? new NpcDecisionReply(NpcDecisionKind.Deliver, meetingId: social.MeetingId)
                    : new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: social.MeetingId, text: "The agreed transfer is recorded.");
                return Task.FromResult(reply);
            }
        }

        private Guid AcceptAndArrive()
        {
            var id = _board.Invite(_world.GetState("sora"), "fish-supply").Value.Id;
            Assert.That(_board.Respond(_world.GetState("ren"), id, true).IsSuccess, Is.True);
            _arrived = true;
            _world.Observe(Time(750));
            _board.Observe();
            return id;
        }

        private void AssertAssets(int renFish, int soraFish, int renCoins, int soraCoins)
        {
            _store.TryGetCharacter("ren", out var ren);
            _store.TryGetCharacter("sora", out var sora);
            Assert.That(ren.Backpack.Items.Sum(item => item.Quantity), Is.EqualTo(renFish));
            Assert.That(sora.Backpack.Items.Sum(item => item.Quantity), Is.EqualTo(soraFish));
            Assert.That(ren.Wallet.Balance, Is.EqualTo(renCoins));
            Assert.That(sora.Wallet.Balance, Is.EqualTo(soraCoins));
        }

        private static NpcMeetingPlan Plan() => new NpcMeetingPlan("fish-supply", "sora", "ren", "pond", "sora.rest", "ren.rest",
            720, 750, 780, maxTurns: 2, resourceTerms: new CharacterTradeTerms("ren", "sora", "fish", 1, 25));
        private static CharacterEconomySnapshot Character(string id, int fish, int coins) => new CharacterEconomySnapshot(id,
            new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>() : new[] { new ItemStack("fish", fish) }), new WalletSnapshot(coins));
        private static NpcDailySchedule Schedule(string id) => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
            id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 810, 1020, 1080);
        private static WorldTimeProgress Time(int minute, long rebuild = 1) => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, rebuild);
    }
}
