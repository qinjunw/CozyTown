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
    public sealed class NpcLoadRecoveryPlayModeTests
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
            _root = new GameObject("Load recovery fixture");
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

        [TestCase("before")]
        [TestCase("after")]
        [TestCase("body")]
        public void PostCommitFailure_ReportsCommittedSaveAndFreezesDecisionsUntilReload(string fault)
        {
            Action<WorldTimeProgress> broken = progress => { if (progress.IsRebuild) throw new InvalidOperationException("injected synchronization fault"); };
            if (fault == "before")
            {
                var services = CozyTownCompositionRoot.CreateDefault();
                services.WorldTime.AdvanceMinutes(360);
                services.WorldTimeFlow.Changed += broken;
                _services = services;
                _controller.Bind(services.WorldTimeFlow, services.ResourceTrading);
            }
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            StartInvitation();
            var oldRun = _controller.GetAgentState(Sora).WorldRunId;
            var oldMeeting = _controller.GetMeeting(Ren);
            _services.EconomyState.TryGetCharacter(Sora, out var sora);
            Assert.That(_services.EconomyState.CommitCharacter(new CharacterEconomySnapshot(Sora, sora.Backpack,
                new WalletSnapshot(10))).IsSuccess, Is.True);
            _services.DaytimeClock.AdvanceElapsed(0.75);
            if (fault == "after") _services.WorldTimeFlow.Changed += broken;
            var animator = fault == "body" ? _sora.gameObject.AddComponent<CozyTownNpcSpriteAnimator>() : null;
            OperationResult result = default;

            Assert.DoesNotThrow(() => result = _services.GameSave.Load());

            Assert.That(result.ErrorCode, Is.EqualTo("save.loaded_rebuild_required"));
            Assert.That(_services.Time.Current.MinuteOfDay, Is.EqualTo(720));
            _services.EconomyState.TryGetCharacter(Sora, out var restored);
            Assert.That(restored.Wallet.Balance, Is.EqualTo(50));
            Assert.That(_controller.GetAgentState(Sora).WorldRunId, Is.Not.EqualTo(oldRun));
            Assert.That(_controller.GetMeeting(Sora), Is.Null);
            _controller.TickDecisions(1);
            Assert.That(_client.Tokens[1].IsCancellationRequested, Is.True);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(2));
            Assert.That(_client.Requests.Count, Is.EqualTo(2));
            Assert.That(_services.DaytimeClock.AdvanceElapsed(0.5).IsSuccess, Is.False);
            Assert.That(_services.GameSave.Save().IsSuccess, Is.False);
            _services.WorldTimeFlow.Changed -= broken;
            if (animator != null) UnityEngine.Object.DestroyImmediate(animator);

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            Assert.That(_sora.Position, Is.EqualTo(new Vector2(40, 20)));
            _controller.TickDecisions(2);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            _client.Replies[1].SetResult(new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: oldMeeting.Id));
            _controller.TickDecisions(3);
            Assert.That(_controller.GetMeeting(Sora), Is.Null);
            Assert.That(_controller.GetAgentState(Sora).ActiveActivity, Is.Null);
            Assert.That(_client.Requests.Count, Is.EqualTo(2));
            Assert.That(_services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720.5));
            _controller.TickDecisions(30);
            Assert.That(_client.Requests.Count, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Notification_ReentrantCommandsCannotReplaceWorldOrScheduler(bool presentation)
        {
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            StartInvitation();
            var other = CozyTownCompositionRoot.CreateDefault();
            Exception bindError = null, configureError = null;
            var outcomes = new List<string>();
            Action<WorldTimeProgress> reenter = progress =>
            {
                _controller.TickDecisions(1);
                var state = _controller.GetAgentState(Sora);
                outcomes.Add(_services.GameSave.Load().ErrorCode);
                outcomes.Add(_services.GameSave.Save().ErrorCode);
                outcomes.Add(_services.DaytimeClock.AdvanceElapsed(1).ErrorCode);
                outcomes.Add(_services.WorldTime.AdvanceMinutes(1).ErrorCode);
                outcomes.Add(_services.Sleep.SleepForMinutes(60).ErrorCode);
                outcomes.Add(_controller.SubmitActivity(new NpcActivityRequest(Sora, state.WorldRunId,
                    state.Revision, Sora + ".outside", NpcActivity.Resting, 800)).ErrorCode);
                outcomes.Add(_controller.CancelActivity(Sora, state.WorldRunId, state.Revision).ErrorCode);
                try { _controller.Bind(other.WorldTimeFlow, other.ResourceTrading); }
                catch (Exception error) { bindError = error; }
                try { _controller.ConfigureDecisions(_client, new[] { new NpcDefinition(Sora, "Sora", "Cook", "Hi") }); }
                catch (Exception error) { configureError = error; }
            };
            if (presentation) _services.WorldTimeFlow.PresentationChanged += reenter;
            else _services.WorldTimeFlow.Changed += reenter;

            var result = _services.GameSave.Load();

            Assert.That(bindError, Is.TypeOf<InvalidOperationException>());
            Assert.That(configureError, Is.TypeOf<InvalidOperationException>());
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(outcomes, Is.EqualTo(new[] { "world_time.not_ready", "world_time.not_ready", "world_time.not_ready",
                "world_time.not_ready", "world_time.not_ready", "agent.world_not_ready", "agent.world_not_ready" }));
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(2));
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720));
            Assert.That(_client.Requests.Count, Is.EqualTo(2));
            if (presentation) _services.WorldTimeFlow.PresentationChanged -= reenter;
            else _services.WorldTimeFlow.Changed -= reenter;
            _services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720.5));
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
