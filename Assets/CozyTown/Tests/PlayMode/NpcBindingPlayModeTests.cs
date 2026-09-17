using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class NpcBindingPlayModeTests
    {
        private const string Ren = DefaultMvpIds.Npcs.Fisher;
        private const string Sora = DefaultMvpIds.Npcs.Cook;
        private GameObject _root;
        private CozyTownServices _services;
        private CozyTownTownLifeController _controller;
        private NpcWorldResident2D _ren, _sora;
        private PendingClient _client;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Binding fixture");
            _ren = CreateResident(Ren, 0);
            _sora = CreateResident(Sora, 40);
            _services = CozyTownCompositionRoot.CreateDefault();
            _services.WorldTime.AdvanceMinutes(360);
            _controller = _root.AddComponent<CozyTownTownLifeController>();
            _controller.enabled = false;
            _controller.Configure(_ren, _sora);
            _controller.Bind(_services.WorldTimeFlow, _services.ResourceTrading);
            _client = new PendingClient();
            _controller.ConfigureDecisions(_client, new[] {
                new NpcDefinition(Sora, "Sora", "Cook", "Hello"),
                new NpcDefinition(Ren, "Ren", "Fisher", "Hello") },
                new NpcDecisionSettings(maxRequestsPerMinute: 3, maxConcurrentRequests: 1),
                new[] { new NpcMeetingPlan("fish", Sora, Ren, "pond", Sora + ".outside", Ren + ".outside",
                    720, 750, 780, resourceTerms: new CharacterTradeTerms(Ren, Sora, DefaultMvpIds.Items.Carp, 1, 25)) });
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            foreach (var reply in _client.Replies) reply.TrySetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
        }

        [Test]
        public void MissingResourceParticipant_PreservesInvitationClockAndPendingReply()
        {
            StartInvitation();
            var before = _controller.GetAgentState(Ren);
            var meeting = _controller.GetMeeting(Ren);
            var position = _ren.Position;
            var other = CozyTownCompositionRoot.CreateDefault();
            var invalidResources = EmptyResources();

            Assert.Throws<ArgumentException>(() => _controller.Bind(other.WorldTimeFlow, invalidResources));

            Assert.That(_controller.GetAgentState(Ren).WorldRunId, Is.EqualTo(before.WorldRunId));
            Assert.That(_controller.GetAgentState(Ren).Revision, Is.EqualTo(before.Revision));
            Assert.That(_controller.GetMeeting(Ren).Id, Is.EqualTo(meeting.Id));
            Assert.That(_controller.GetMeetingResources(Ren).OwnedQuantity, Is.EqualTo(2));
            Assert.That(_ren.Position, Is.EqualTo(position));
            other.WorldTime.AdvanceMinutes(10);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720));
            _services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720.5));
            _controller.TickDecisions(1);
            Assert.That(_client.Tokens[1].IsCancellationRequested, Is.False);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(2));
            _client.Replies[1].SetResult(new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: meeting.Id));
            _controller.TickDecisions(2);
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Scheduled));
            Assert.That(_controller.GetAgentState(Ren).ActiveActivity, Is.Not.Null);
            Assert.That(_controller.GetAgentState(Sora).ActiveActivity, Is.Not.Null);
        }

        private void StartInvitation()
        {
            _controller.TickDecisions(0);
            Assert.That(_client.Requests[0].Social.Kind, Is.EqualTo(NpcSocialContextKind.Opportunity));
            _client.Replies[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Invite, planId: "fish"));
            _controller.TickDecisions(0.1);
            _controller.TickDecisions(0.2);
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Invited));
            Assert.That(_client.Requests[1].Social.Kind, Is.EqualTo(NpcSocialContextKind.Invitation));
        }

        [Test]
        public void MissingDecisionResident_PreservesWorldAndPendingActivityChoice()
        {
            _controller.ConfigureDecisions(_client, new[] { new NpcDefinition(Sora, "Sora", "Cook", "Hello") },
                new NpcDecisionSettings(maxRequestsPerMinute: 1, maxConcurrentRequests: 1));
            _controller.TickDecisions(0);
            var before = _controller.GetAgentState(Sora);
            var other = CozyTownCompositionRoot.CreateDefault();
            _controller.Configure(_ren);

            Assert.Throws<ArgumentException>(() => _controller.Bind(other.WorldTimeFlow, other.ResourceTrading));

            _controller.Configure(_ren, _sora);
            Assert.That(_controller.GetAgentState(Sora).WorldRunId, Is.EqualTo(before.WorldRunId));
            _controller.Bind(_services.WorldTimeFlow, _services.ResourceTrading);
            Assert.That(_controller.GetAgentState(Sora).WorldRunId, Is.EqualTo(before.WorldRunId));
            _controller.TickDecisions(1);
            Assert.That(_client.Tokens[0].IsCancellationRequested, Is.False);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            _client.Replies[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Visit, Sora + ".outside", NpcActivity.Resting, 60));
            _controller.TickDecisions(2);
            Assert.That(_controller.GetDecisionOutcome(Sora).ActivityAccepted, Is.True);
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(1));
            _services.DaytimeClock.AdvanceElapsed(0.5);
            Assert.That(_sora.Position, Is.EqualTo(new Vector2(40, 19)));
        }

        [TestCase("null-clock", typeof(ArgumentNullException))]
        [TestCase("invalid-time", typeof(ArgumentOutOfRangeException))]
        [TestCase("duplicate-resident", typeof(ArgumentException))]
        [TestCase("unconfigured-resident", typeof(InvalidOperationException))]
        [TestCase("missing-meeting-partner", typeof(ArgumentException))]
        public void InvalidBindingInput_LeavesOriginalResourcesAndSubscription(string input, Type error)
        {
            StartInvitation();
            var before = _controller.GetAgentState(Sora);
            var other = CozyTownCompositionRoot.CreateDefault();
            IWorldTimeFlow flow = other.WorldTimeFlow;
            if (input == "null-clock") flow = null;
            if (input == "invalid-time") flow = new InvalidTimeFlow();
            if (input == "duplicate-resident") _controller.Configure(_ren, _sora, _ren);
            if (input == "missing-meeting-partner") _controller.Configure(_sora);
            if (input == "unconfigured-resident")
            {
                var actor = new GameObject("Unconfigured resident");
                actor.transform.SetParent(_root.transform);
                _controller.Configure(_ren, _sora, actor.AddComponent<NpcWorldResident2D>());
            }

            Assert.Throws(error, () => _controller.Bind(flow, other.ResourceTrading));

            _controller.Configure(_ren, _sora);
            Assert.That(_controller.GetAgentState(Sora).WorldRunId, Is.EqualTo(before.WorldRunId));
            _controller.Bind(_services.WorldTimeFlow, _services.ResourceTrading);
            Assert.That(_controller.GetAgentState(Sora).WorldRunId, Is.EqualTo(before.WorldRunId),
                "An unchanged binding remains a no-op after rejected input.");
            other.WorldTime.AdvanceMinutes(10);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720));
            _services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720.5));
            _controller.TickDecisions(1);
            Assert.That(_client.Tokens[1].IsCancellationRequested, Is.False);
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(2));
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_controller.GetMeetingResources(Sora).Balance, Is.EqualTo(50));
        }

        [Test]
        public void FailedBindingDuringTravel_KeepsBothActivitiesAndActualMovement()
        {
            StartInvitation();
            var meeting = _controller.GetMeeting(Ren);
            _client.Replies[1].SetResult(new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: meeting.Id));
            _controller.TickDecisions(1);
            _services.WorldTime.AdvanceMinutes(31);
            var renActivity = _controller.GetAgentState(Ren).ActiveActivity;
            var soraActivity = _controller.GetAgentState(Sora).ActiveActivity;
            Assert.That(_ren.Position, Is.EqualTo(new Vector2(0, 19)));
            Assert.That(_ren.Status, Is.EqualTo(TownRouteStatus.Travelling));

            Assert.Throws<ArgumentException>(() => _controller.Bind(_services.WorldTimeFlow, EmptyResources()));

            Assert.That(_controller.GetAgentState(Ren).ActiveActivity, Is.SameAs(renActivity));
            Assert.That(_controller.GetAgentState(Sora).ActiveActivity, Is.SameAs(soraActivity));
            Assert.That(_controller.GetMeeting(Ren).Id, Is.EqualTo(meeting.Id));
            _controller.Bind(_services.WorldTimeFlow);
            _controller.Bind(_services.WorldTimeFlow, _services.ResourceTrading);
            _services.DaytimeClock.AdvanceElapsed(0.5);
            Assert.That(_ren.Position, Is.EqualTo(new Vector2(0, 18)));
            Assert.That(_sora.Position, Is.EqualTo(new Vector2(40, 18)));
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Travelling));
        }

        private sealed class InvalidTimeFlow : IWorldTimeFlow
        {
            public WorldTimeFlowState State => WorldTimeFlowState.Ready;
            public IReadOnlyList<string> NotificationFailures => Array.Empty<string>();
            public WorldTimeProgress Current => new WorldTimeProgress(new GameClockSnapshot(0, 360), 0, false);
            public event Action<WorldTimeProgress> Changed { add { } remove { } }
            public event Action<WorldTimeProgress> PresentationChanged { add { } remove { } }
        }

        [Test]
        public void UnconfiguredAnimation_PreservesBindingUntilConfigurationIsRepaired()
        {
            StartInvitation();
            var before = _controller.GetAgentState(Ren);
            var animator = _ren.gameObject.AddComponent<CozyTownNpcSpriteAnimator>();
            var other = CozyTownCompositionRoot.CreateDefault();

            var error = Assert.Catch(() => _controller.Bind(other.WorldTimeFlow, other.ResourceTrading));

            Assert.That(_controller.GetAgentState(Ren).WorldRunId, Is.EqualTo(before.WorldRunId));
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            UnityEngine.Object.DestroyImmediate(animator);
            _controller.TickDecisions(1);
            Assert.That(_client.Tokens[1].IsCancellationRequested, Is.False);
            _services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720.5));
            _controller.Bind(other.WorldTimeFlow, other.ResourceTrading);
            Assert.That(_controller.GetAgentState(Ren).WorldRunId, Is.Not.EqualTo(before.WorldRunId));
            other.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(360.5));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedLoad_KeepsActivitiesAssetsAndDeliveryEligibility(bool moduleFailure)
        {
            var storage = new SnapshotStorage();
            _services = CozyTownCompositionRoot.Create(DefaultMvpContent.CreateConfiguration(), null, storage);
            _services.WorldTime.AdvanceMinutes(360);
            _controller.Bind(_services.WorldTimeFlow, _services.ResourceTrading);
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var saved = storage.Snapshot;
            StartDelivery();
            _services.DaytimeClock.AdvanceElapsed(0.25);
            var before = _controller.GetAgentState(Sora);
            var meeting = _controller.GetMeeting(Sora);
            var plots = saved.Farm.Plots.ToArray();
            plots[0] = new FarmPlotSnapshot("unknown-plot", null, 0, false, FarmPlotStatus.Empty);
            var characters = saved.Characters.Select(character => character.CharacterId == Sora
                ? new CharacterEconomySnapshot(Sora, character.Backpack, new WalletSnapshot(1)) : character).ToArray();
            storage.Snapshot = new GameSaveSnapshot(moduleFailure ? saved.SchemaVersion : 999,
                777, new GameClockSnapshot(1, 600), characters, saved.Shops,
                new FarmSnapshot(saved.Farm.LastProcessedDay, plots), saved.Livestock);

            var result = _services.GameSave.Load();

            Assert.That(result.ErrorCode, Is.EqualTo(moduleFailure ? "save.restore_farm_failed" : "save.schema_unsupported"));
            Assert.That(_services.WorldSeed.Value, Is.EqualTo(saved.WorldSeed));
            Assert.That(_services.Time.Current.MinuteOfDay, Is.EqualTo(770));
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(770.5));
            Assert.That(_controller.GetAgentState(Sora).WorldRunId, Is.EqualTo(before.WorldRunId));
            Assert.That(_controller.GetAgentState(Sora).ActiveActivity, Is.SameAs(before.ActiveActivity));
            Assert.That(_controller.GetMeeting(Sora).Id, Is.EqualTo(meeting.Id));
            Assert.That(_sora.Position, Is.EqualTo(new Vector2(40, 0)));
            Assert.That(_controller.GetMeetingResources(Sora).Balance, Is.EqualTo(50));
            _controller.TickDecisions(3);
            Assert.That(_client.Tokens[2].IsCancellationRequested, Is.False);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(3));
            _client.Replies[2].SetResult(new NpcDecisionReply(NpcDecisionKind.Deliver, meetingId: meeting.Id));
            _controller.TickDecisions(4);
            Assert.That(_controller.GetMeeting(Sora).DeliveryResultCode, Is.EqualTo("resource.delivered"));
            Assert.That(_controller.GetMeetingResources(Sora).Balance, Is.EqualTo(25));
            Assert.That(_controller.GetMeetingResources(Ren).OwnedQuantity, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedSuccessfulLoad_RebuildsBodiesAndRejectsEachPreviousTimelineReply()
        {
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            StartDelivery();
            var oldRun = _controller.GetAgentState(Sora).WorldRunId;
            var oldMeeting = _controller.GetMeeting(Sora);

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            var firstLoadRun = _controller.GetAgentState(Sora).WorldRunId;
            Assert.That(firstLoadRun, Is.Not.EqualTo(oldRun));
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720));
            Assert.That(_controller.GetAgentState(Sora).ActiveActivity, Is.Null);
            Assert.That(_controller.GetMeeting(Sora), Is.Null);
            Assert.That(_controller.GetMeetingMemories(Sora), Is.Empty);
            Assert.That(_sora.Position, Is.EqualTo(new Vector2(40, 20)));
            _controller.TickDecisions(3);
            Assert.That(_client.Tokens[2].IsCancellationRequested, Is.True);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(3));
            _client.Replies[2].SetResult(new NpcDecisionReply(NpcDecisionKind.Deliver, meetingId: oldMeeting.Id));
            _controller.TickDecisions(4);
            _services.EconomyState.TryGetCharacter(Sora, out var sora);
            Assert.That(sora.Wallet.Balance, Is.EqualTo(50));
            _controller.TickDecisions(60);
            Assert.That(_client.Requests.Count, Is.EqualTo(4));
            Assert.That(_client.Requests[3].Self.WorldRunId, Is.EqualTo(firstLoadRun));

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            var secondLoadRun = _controller.GetAgentState(Sora).WorldRunId;
            Assert.That(secondLoadRun, Is.Not.EqualTo(firstLoadRun));
            _controller.TickDecisions(61);
            Assert.That(_client.Tokens[3].IsCancellationRequested, Is.True);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_client.Requests.Count, Is.EqualTo(4));
            _client.Replies[3].SetResult(new NpcDecisionReply(NpcDecisionKind.Invite, planId: "fish"));
            _controller.TickDecisions(62);
            Assert.That(_controller.GetMeeting(Sora), Is.Null);
            Assert.That(_controller.GetAgentState(Sora).WorldRunId, Is.EqualTo(secondLoadRun));
            _services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720.5));
        }

        private void StartDelivery()
        {
            StartInvitation();
            _client.Replies[1].SetResult(new NpcDecisionReply(NpcDecisionKind.AcceptInvitation,
                meetingId: _controller.GetMeeting(Ren).Id));
            _controller.TickDecisions(1);
            _services.WorldTime.AdvanceMinutes(50);
            Assert.That(_controller.GetMeeting(Sora).State, Is.EqualTo(NpcMeetingState.Talking));
            _controller.TickDecisions(2);
            Assert.That(_client.Requests[2].Social.Kind, Is.EqualTo(NpcSocialContextKind.Delivery));
        }

        private sealed class SnapshotStorage : ISaveStorage
        {
            public GameSaveSnapshot Snapshot;
            public bool Exists(string slotId) => Snapshot != null;
            public OperationResult Save(string slotId, GameSaveSnapshot snapshot)
            {
                Snapshot = snapshot;
                return OperationResult.Success();
            }
            public OperationResult<GameSaveSnapshot> Load(string slotId)
                => OperationResult<GameSaveSnapshot>.Success(Snapshot);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ValidRetry_ChangesResourcesButKeepsBudgetAndPhysicalRequest(bool sameClock)
        {
            StartInvitation();
            var before = _controller.GetAgentState(Sora);
            var oldMeeting = _controller.GetMeeting(Ren);
            var other = CozyTownCompositionRoot.CreateDefault();
            other.WorldTime.AdvanceMinutes(360);
            other.EconomyState.TryGetCharacter(Sora, out var sora);
            Assert.That(other.EconomyState.CommitCharacter(new CharacterEconomySnapshot(Sora, sora.Backpack,
                new WalletSnapshot(100))).IsSuccess, Is.True);
            var flow = sameClock ? _services.WorldTimeFlow : other.WorldTimeFlow;
            Assert.Throws<ArgumentException>(() => _controller.Bind(flow, EmptyResources()));

            _controller.Bind(flow, other.ResourceTrading);

            var newRun = _controller.GetAgentState(Sora).WorldRunId;
            Assert.That(newRun, Is.Not.EqualTo(before.WorldRunId));
            Assert.That(_controller.GetMeeting(Sora), Is.Null);
            Assert.That(_controller.GetMeetingMemories(Sora), Is.Empty);
            _services.DaytimeClock.AdvanceElapsed(0.5);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(sameClock ? 721 : 720));
            _controller.TickDecisions(1);
            Assert.That(_client.Tokens[1].IsCancellationRequested, Is.True);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_client.Requests.Count, Is.EqualTo(2), "Cancellation does not release the physical slot.");
            _client.Replies[1].SetResult(new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: oldMeeting.Id));
            _controller.TickDecisions(2);
            Assert.That(_controller.ActiveDecisionRequests, Is.Zero);
            Assert.That(_client.Requests.Count, Is.EqualTo(2), "The initiator retains its original cooldown.");
            _controller.TickDecisions(30);
            Assert.That(_client.Requests.Count, Is.EqualTo(3));
            Assert.That(_client.Requests[2].Social.Resources.Balance, Is.EqualTo(100));
            _controller.Bind(flow, other.ResourceTrading);
            _controller.Bind(flow);
            Assert.That(_controller.GetAgentState(Sora).WorldRunId, Is.EqualTo(newRun));
            _client.Replies[2].SetResult(new NpcDecisionReply(NpcDecisionKind.Invite, planId: "fish"));
            _controller.TickDecisions(31);
            Assert.That(_controller.GetMeeting(Sora).Id, Is.Not.EqualTo(oldMeeting.Id));
            Assert.That(_controller.GetMeetingResources(Sora).Balance, Is.EqualTo(100));
            _controller.TickDecisions(59);
            Assert.That(_client.Requests.Count, Is.EqualTo(3));
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(3));
            _controller.TickDecisions(60);
            Assert.That(_client.Requests.Count, Is.EqualTo(4));
            _services.EconomyState.TryGetCharacter(Sora, out var originalSora);
            Assert.That(originalSora.Wallet.Balance, Is.EqualTo(50));
        }

        private NpcWorldResident2D CreateResident(string id, float x)
        {
            var area = new GameObject(id + " map");
            area.transform.SetParent(_root.transform);
            var map = area.AddComponent<TownMap2D>();
            map.Configure(new[] { new TownHome(id + ".home", id, id + ".outside", id + ".entry") },
                new[] { new TownLocation(id + ".outside", new Vector2(x, 0)),
                    new TownLocation(id + ".entry", new Vector2(x, 0.5f)),
                    new TownLocation(id + ".work", new Vector2(x + 20, 0)),
                    new TownLocation(id + ".rest", new Vector2(x, 20)) },
                new[] { new TownRoad(id + ".entry", id + ".outside"),
                    new TownRoad(id + ".outside", id + ".work"), new TownRoad(id + ".outside", id + ".rest") });
            var actor = new GameObject(id);
            actor.transform.SetParent(area.transform);
            var resident = actor.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
                id + ".work", id + ".rest", id + ".work", 360, 480, 720, 810, 1020, 1080),
                actor.AddComponent<SpriteRenderer>());
            return resident;
        }

        private static CharacterResourceTrading EmptyResources()
            => new CharacterResourceTrading(new InMemoryEconomyStateStore(Array.Empty<CharacterEconomySnapshot>(),
                Array.Empty<ShopEconomySnapshot>()), DefaultMvpContent.CreateConfiguration().Items, 8);

        private sealed class PendingClient : INpcDecisionClient
        {
            public readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public readonly List<TaskCompletionSource<NpcDecisionReply>> Replies = new List<TaskCompletionSource<NpcDecisionReply>>();
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                Requests.Add(request);
                Tokens.Add(token);
                var reply = new TaskCompletionSource<NpcDecisionReply>();
                Replies.Add(reply);
                return reply.Task;
            }
        }
    }
}
