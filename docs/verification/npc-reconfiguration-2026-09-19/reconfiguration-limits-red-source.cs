using System;
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
    public sealed class NpcReconfigurationPlayModeTests
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
            _controller.enabled = false;
            _controller.Configure(_resident);
            _controller.Bind(_services.WorldTimeFlow);
            _services.WorldTime.AdvanceMinutes(360);
            _services.GameSave.Save();
            _services.GameSave.Load();
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 20)));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_world);
            foreach (var client in _clients)
                foreach (var reply in client.Replies) reply.TrySetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            _clients.Clear();
        }

        private readonly List<ControlledClient> _clients = new List<ControlledClient>();
        private ControlledClient Client()
        {
            var client = new ControlledClient();
            _clients.Add(client);
            return client;
        }

        [Test]
        public void CompletedRequest_ReconfigurationKeepsTheOriginalCallWindow()
        {
            var first = Client();
            _controller.ConfigureDecisions(first, Profiles(), new NpcDecisionSettings(maxRequestsPerMinute: 1));
            _controller.TickDecisions(0);
            first.Replies[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            _controller.TickDecisions(1);
            var replacement = Client();

            _controller.ConfigureDecisions(replacement, Profiles());

            Assert.That(_controller.DecisionRequestsStarted, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(1));
            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);
            _controller.TickDecisions(2);
            Assert.That(replacement.Requests, Is.Empty);
            _controller.TickDecisions(59.9);
            Assert.That(replacement.Requests, Is.Empty);
            _controller.TickDecisions(60);
            Assert.That(replacement.Requests.Count, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsStarted, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnfinishedRequest_RejectsReplacementUntilTheTaskEnds(bool timedOut)
        {
            var first = Client();
            var replacement = Client();
            _controller.ConfigureDecisions(first, Profiles());
            _controller.TickDecisions(0);
            if (timedOut) _controller.TickDecisions(8);

            Assert.Throws<InvalidOperationException>(() => _controller.ConfigureDecisions(replacement, Profiles()));

            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsStarted, Is.EqualTo(1));
            Assert.That(first.Tokens[0].IsCancellationRequested, Is.EqualTo(timedOut));
            first.Replies[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            _controller.ConfigureDecisions(replacement, Profiles());
            Assert.That(_controller.ActiveDecisionRequests, Is.Zero);
            Assert.That(_controller.DecisionRequestsStarted, Is.EqualTo(1));
            Assert.That(replacement.Requests, Is.Empty);
        }

        [TestCase("rate")]
        [TestCase("concurrency")]
        [TestCase("cooldown")]
        [TestCase("request-timeout")]
        [TestCase("decision-timeout")]
        [TestCase("calls")]
        [TestCase("lifetime")]
        public void StartedSession_RejectsRelaxedLimitsWithoutReplacingTheClient(string changed)
        {
            var first = Client();
            _controller.ConfigureDecisions(first, Profiles(),
                new NpcDecisionSettings(maxRequestsPerMinute: 2, maxConcurrentRequests: 1));
            _controller.TickDecisions(0);
            first.Replies[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            _controller.TickDecisions(1);
            var outcome = _controller.GetDecisionOutcome("npc.mina");
            var relaxed = new NpcDecisionSettings(changed == "rate" ? 3 : 2, changed == "concurrency" ? 2 : 1,
                changed == "cooldown" ? 29 : 30, changed == "request-timeout" ? 9 : 8,
                changed == "decision-timeout" ? 13 : 12, changed == "calls" ? 3 : 2,
                changed == "lifetime" ? 31 : 30);
            var replacement = Client();

            Assert.Throws<InvalidOperationException>(() => _controller.ConfigureDecisions(replacement, Profiles(), relaxed));

            Assert.That(_controller.GetDecisionOutcome("npc.mina"), Is.SameAs(outcome));
            Assert.That(_controller.DecisionRequestsStarted, Is.EqualTo(1));
            _services.GameSave.Load();
            _controller.TickDecisions(30);
            Assert.That(first.Requests.Count, Is.EqualTo(2));
            Assert.That(replacement.Requests, Is.Empty);
        }

        [Test]
        public void StricterReplacement_InheritsItsLimitsWhenSettingsAreOmitted()
        {
            var first = Client();
            _controller.ConfigureDecisions(first, Profiles());
            _controller.TickDecisions(0);
            first.Replies[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            _controller.TickDecisions(1);
            _controller.ConfigureDecisions(Client(), Profiles(), new NpcDecisionSettings(1, 1, 45, 5, 10, 1, 20));
            var final = Client();
            _controller.ConfigureDecisions(final, Profiles());
            _services.GameSave.Load();
            _controller.TickDecisions(59.9);
            Assert.That(final.Requests, Is.Empty);
            _controller.TickDecisions(60);
            Assert.That(final.Requests.Count, Is.EqualTo(1));
            Assert.That(final.Requests[0].MaxCalls, Is.EqualTo(1));
            _controller.TickDecisions(65);
            Assert.That(_controller.GetDecisionOutcome("npc.mina").Code, Is.EqualTo("agent.request_timeout"));
            Assert.That(final.Tokens[0].IsCancellationRequested, Is.True);
        }

        private static NpcDefinition[] Profiles()
            => new[] { new NpcDefinition("npc.mina", "Mina", "A careful shopkeeper.", "Hello") };

        private sealed class ControlledClient : INpcDecisionClient
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
