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
    public sealed class NpcConversationBudgetTests
    {
        private NpcAgentWorld _world;
        private NpcMeetingBoard _board;
        private IEconomyStateStore _store;
        private CharacterResourceTrading _trading;
        private readonly NpcObservationScene _scene = new NpcObservationScene(
            new[] { new NpcObservationRegion("pond", "Pond", -10, -10, 10, 10) },
            Array.Empty<NpcObservationEntity>());
        private readonly NpcObservationBody[] _bodies = {
            new NpcObservationBody("sora", 0, 0), new NpcObservationBody("ren", 2, 0) };

        [SetUp]
        public void SetUp()
        {
            _world = new NpcAgentWorld(new[] { Schedule("sora", 840), Schedule("ren", 795) });
            _world.Observe(Time(735));
            _store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) },
                Array.Empty<ShopEconomySnapshot>());
            _trading = new CharacterResourceTrading(_store,
                new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            _board = new NpcMeetingBoard(_world, new[] {
                new NpcMeetingPlan("fish-supply", "sora", "ren", "pond", "sora.rest", "ren.rest", 735, 780, 795,
                    resourceTerms: new CharacterTradeTerms("ren", "sora", "fish", 1, 25)) },
                (npc, location) => NpcMeetingPresence.Arrived, _trading);
        }

        [Test]
        public void ConversationTurn_ResumesAfterBudgetReturns()
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1));
            Step(scheduler, "old-request", 0, 794.8);
            Assert.That(client.Requests.Single().Social.Transcript.Count, Is.EqualTo(3));

            Step(scheduler, "schedule-cancel", 0.1, 795);
            Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("agent.decision_stale"));
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            client.Replies[0].SetResult(Say(client.Requests[0], "obsolete-fourth"));
            Step(scheduler, "late-reply-drained", 0.2, 795.2);
            Step(scheduler, "before-opportunity-expiry", 15, 824.8);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Step(scheduler, "opportunity-expired", 15.1, 825);
            Step(scheduler, "other-participant-schedule", 22.6, 840);
            Step(scheduler, "rolling-budget-open", 60, 914.8);

            Assert.That(client.Requests.Count, Is.EqualTo(2), "The meeting still has a current speaker and an unfinished fourth turn.");
        }

        private void PrepareFourthTurn()
        {
            var invitation = _board.Invite(_world.GetState("sora"), "fish-supply");
            Assert.That(invitation.IsSuccess, Is.True);
            Assert.That(_board.Respond(_world.GetState("ren"), invitation.Value.Id, true).IsSuccess, Is.True);
            _world.Observe(Time(787));
            _board.Observe();
            Assert.That(_board.Deliver(_world.GetState("sora"), invitation.Value.Id).IsSuccess, Is.True);
            foreach (var speaker in new[] { "sora", "ren", "sora" })
                Assert.That(_board.Speak(_world.GetState(speaker), invitation.Value.Id, "recorded-" + speaker).IsSuccess, Is.True);
            AssertAssets();
        }

        private NpcDecisionScheduler Scheduler(ControlledClient client, NpcDecisionSettings settings)
            => new NpcDecisionScheduler(_world, new[] { Profile("ren") }, client, settings, _board, Observe);

        private NpcLocalObservation Observe(NpcDecisionRequest request)
            => _scene.Read(request.NpcId, _world.GetState(request.NpcId).WorldRunId, _world.TotalMinutes, _bodies,
                social: request.Social, ownResources: _trading.Inspect(request.Social?.Resources?.Terms, request.NpcId),
                memories: _board.GetMemories(request.NpcId));

        private void Step(NpcDecisionScheduler scheduler, string point, double realSeconds, double gameMinutes, long rebuild = 1)
        {
            _world.Observe(Time(gameMinutes, rebuild));
            var outcomes = scheduler.Tick(realSeconds);
            var meeting = _board.GetLatest("ren");
            Write(
                $"BUDGET_TRACE {{\"point\":\"{point}\",\"realSeconds\":{realSeconds},\"gameMinutes\":{gameMinutes},",
                $"\"renRevision\":{_world.GetState("ren").Revision},\"soraRevision\":{_world.GetState("sora").Revision},",
                $"\"requests\":{scheduler.RequestsStarted},\"rolling\":{scheduler.RequestsInLastMinute},",
                $"\"active\":{scheduler.ActiveRequestCount},\"waiting\":{scheduler.WaitingResidentCount},",
                $"\"meeting\":\"{meeting?.State}\",\"speaker\":\"{meeting?.SpeakerId}\",\"lines\":{meeting?.Transcript.Count ?? 0},",
                $"\"renOutcome\":\"{scheduler.GetLastOutcome("ren")?.Code}\",\"finishedThisTick\":{outcomes.Count}}}");
        }

        private static void Write(params FormattableString[] fragments)
            => TestContext.Out.WriteLine(string.Concat(fragments.Select(FormattableString.Invariant)));

        private void AssertAssets()
        {
            foreach (var id in new[] { "ren", "sora" })
            {
                Assert.That(_store.TryGetCharacter(id, out var character), Is.True);
                Assert.That(character.Backpack.Items.Single().Quantity, Is.EqualTo(1));
                Assert.That(character.Wallet.Balance, Is.EqualTo(25));
            }
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
                Write(
                    $"BUDGET_REQUEST {{\"ordinal\":{Requests.Count},\"npcId\":\"{request.NpcId}\",\"decisionId\":\"{request.DecisionId}\",",
                    $"\"step\":{request.Step},\"gameMinutes\":{request.GameTotalMinutes},\"revision\":{request.Self.Revision},",
                    $"\"phase\":\"{request.Social?.Kind}\",\"triggers\":\"{string.Join(",", request.Triggers.Select(item => item.Kind))}\",",
                    $"\"previousResult\":\"{request.PreviousResultCode}\",\"candidateError\":\"{request.CandidateErrorCode}\"}}");
                return reply.Task;
            }
        }

        private static NpcDecisionReply Say(NpcDecisionRequest request, string text)
            => new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: request.Social.MeetingId, text: text);
        private static NpcDefinition Profile(string id) => new NpcDefinition(id, id, "Resident", "Hello");
        private static NpcDailySchedule Schedule(string id, int afternoon)
            => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest",
                id + ".afternoon", 360, 480, 720, afternoon, 1020, 1080);
        private static WorldTimeProgress Time(double minute, long rebuild = 1)
            => new WorldTimeProgress(new GameClockSnapshot(1, (int)Math.Floor(minute)), minute - Math.Floor(minute), false, rebuild);
        private static CharacterEconomySnapshot Character(string id, int fish, int coins)
            => new CharacterEconomySnapshot(id,
                new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>() : new[] { new ItemStack("fish", fish) }),
                new WalletSnapshot(coins));
    }
}
