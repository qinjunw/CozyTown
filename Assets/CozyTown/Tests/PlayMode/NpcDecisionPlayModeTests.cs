using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Core;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class NpcDecisionPlayModeTests
    {
        private GameObject _world;
        private CozyTownServices _services;
        private CozyTownTownLifeController _controller;
        private NpcWorldResident2D _resident;

        [SetUp]
        public void SetUp()
        {
            _world = new GameObject("Autonomous decision fixture");
            var map = _world.AddComponent<TownMap2D>();
            map.Configure(new[] { new TownHome("home.mina", "npc.mina", "outside", "entry") },
                new[] { new TownLocation("outside", Vector2.zero), new TownLocation("entry", new Vector2(0, 0.5f)),
                    new TownLocation("work", new Vector2(20, 0)), new TownLocation("rest", new Vector2(0, 20)) },
                new[] { new TownRoad("entry", "outside"), new TownRoad("outside", "work"), new TownRoad("outside", "rest") });
            var actor = new GameObject("Mina");
            actor.transform.SetParent(_world.transform);
            _resident = actor.AddComponent<NpcWorldResident2D>();
            _resident.Configure(map, new NpcDailySchedule("npc.mina", "home.mina", "outside", "entry",
                "work", "rest", "work", 360, 480, 720, 780, 1020, 1080), actor.AddComponent<SpriteRenderer>());
            _services = CozyTownCompositionRoot.CreateDefault();
            _controller = _world.AddComponent<CozyTownTownLifeController>();
            _controller.Configure(_resident);
            _controller.Bind(_services.WorldTimeFlow);
            _services.WorldTime.AdvanceMinutes(360);
            _services.GameSave.Save();
            _services.GameSave.Load();
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 20)));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_world);

#if UNITY_EDITOR
        [Test]
        public void BatchBootstrap_DoesNotEnableBackgroundRequestsFromTheEnvironment()
        {
            Assert.That(Application.isBatchMode, Is.True);
            string variable = CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable;
            string previous = System.Environment.GetEnvironmentVariable(variable);
            var content = CozyTown.Unity.Content.CozyTownMvpContentAsset.CreateDefaultForEditor();
            try
            {
                System.Environment.SetEnvironmentVariable(variable, "https://agent.invalid/decide");
                var bootObject = new GameObject("Batch decision bootstrap");
                bootObject.transform.SetParent(_world.transform);
                bootObject.SetActive(false);
                var bootstrap = bootObject.AddComponent<CozyTownBootstrap>();
                bootstrap.ConfigureContentAsset(content);
                bootstrap.RegisterTownLife(_controller);
                bootObject.SetActive(true);
                Assert.That(bootstrap.IsInitialized, Is.True);
                Assert.That(_controller.DecisionsEnabled, Is.False);
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(variable, previous);
                Object.DestroyImmediate(content);
            }
        }
#endif

        [TestCase(true)]
        [TestCase(false)]
        public void Bootstrap_BindsTheConfiguredClientOnceForEarlyOrLateRegistration(bool late)
        {
            var bootObject = new GameObject("Decision bootstrap");
            bootObject.transform.SetParent(_world.transform);
            bootObject.SetActive(false);
            var bootstrap = bootObject.AddComponent<CozyTownBootstrap>();
            var client = new VisitClient();
            bootstrap.ConfigureDecisions(client, Profiles());
            if (!late) bootstrap.RegisterTownLife(_controller);
            bootstrap.Initialize(_services);
            if (late) bootstrap.RegisterTownLife(_controller);

            Assert.That(_controller.DecisionsEnabled, Is.True);
            _controller.TickDecisions(0);
            bootstrap.RegisterTownLife(_controller);
            _controller.TickDecisions(1);
            Assert.That(client.Requests, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsStarted, Is.EqualTo(1));
            Assert.That(_resident.TargetLocationId, Is.EqualTo("outside"));
        }

        [UnityTest]
        public IEnumerator UnityUpdate_DrivesDecisionsWithoutManualTicks()
        {
            var client = new VisitClient();
            _controller.ConfigureDecisions(client, Profiles());
            yield return null;
            yield return null;
            yield return null;
            Assert.That(client.Requests, Is.EqualTo(1));
            Assert.That(_controller.GetDecisionOutcome("npc.mina").ActivityAccepted, Is.True);
            Assert.That(_resident.TargetLocationId, Is.EqualTo("outside"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LoadOrDifferentWorldBinding_DiscardsLateMovementWithoutResettingBudget(bool differentWorld)
        {
            var client = new PendingClient();
            _controller.ConfigureDecisions(client, Profiles(), new NpcDecisionSettings(maxRequestsPerMinute: 1));
            _controller.TickDecisions(0);
            if (differentWorld)
            {
                var otherServices = CozyTownCompositionRoot.CreateDefault();
                otherServices.WorldTime.AdvanceMinutes(360);
                _controller.Bind(otherServices.WorldTimeFlow);
                _controller.Bind(otherServices.WorldTimeFlow);
            }
            else Assert.That(_services.GameSave.Load().IsSuccess, Is.True);
            _controller.TickDecisions(1);
            Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
            Assert.That(_controller.GetDecisionOutcome("npc.mina").Code, Is.EqualTo("agent.world_stale"));
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            client.Replies[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Visit, "outside", NpcActivity.Resting, 60));
            _controller.TickDecisions(2);
            _controller.TickDecisions(59);
            Assert.That(_resident.TargetLocationId, Is.EqualTo("rest"));
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 20)));
            Assert.That(client.Replies.Count, Is.EqualTo(1));
            _controller.TickDecisions(60);
            Assert.That(client.Replies.Count, Is.EqualTo(2));
        }

        [Test]
        public void DestroyingTheController_CancelsItsOutstandingDecision()
        {
            var client = new PendingClient();
            _controller.ConfigureDecisions(client, Profiles());
            _controller.TickDecisions(0);
            Object.DestroyImmediate(_world);
            _world = null;
            Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
            client.Replies[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
        }

        private sealed class PendingClient : INpcDecisionClient
        {
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public readonly List<TaskCompletionSource<NpcDecisionReply>> Replies = new List<TaskCompletionSource<NpcDecisionReply>>();
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                Tokens.Add(token);
                var source = new TaskCompletionSource<NpcDecisionReply>();
                Replies.Add(source);
                return source.Task;
            }
        }

        [Test]
        public void AutomaticDecision_UsesTheExistingBodyAndMovesWithoutPlayerInput()
        {
            var client = new VisitClient();
            _controller.ConfigureDecisions(client, Profiles());

            _controller.TickDecisions(0);
            _controller.TickDecisions(0.1);

            Assert.That(client.Requests, Is.EqualTo(1));
            Assert.That(_controller.GetDecisionOutcome("npc.mina").ActivityAccepted, Is.True);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 20)));
            Assert.That(_resident.TargetLocationId, Is.EqualTo("outside"));
            Assert.That(_services.DaytimeClock.AdvanceElapsed(5).IsSuccess, Is.True);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 10)));
            Assert.That(_resident.Status, Is.EqualTo(TownRouteStatus.Travelling));
            _controller.TickDecisions(5.1);
            Assert.That(client.Requests, Is.EqualTo(1));
        }

        private static NpcDefinition[] Profiles()
            => new[] { new NpcDefinition("npc.mina", "Mina", "A careful shopkeeper.", "Hello") };

        private sealed class VisitClient : INpcDecisionClient
        {
            public int Requests { get; private set; }

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests++;
                return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Visit, "outside", NpcActivity.Resting, 60));
            }
        }
    }
}
