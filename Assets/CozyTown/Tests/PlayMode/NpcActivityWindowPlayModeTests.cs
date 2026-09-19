using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Player;
using CozyTown.Unity.Time;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class NpcActivityWindowPlayModeTests
    {
        private GameObject _world;
        private CozyTownServices _services;
        private CozyTownTownLifeController _controller;
        private NpcWorldResident2D _resident;
        private ControlledClient _client;

        [SetUp]
        public void SetUp()
        {
            _world = new GameObject("Activity window fixture");
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
            Assert.That(_services.WorldTime.AdvanceMinutes(360).IsSuccess, Is.True);
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 20)));
            _client = new ControlledClient();
            _controller.ConfigureDecisions(_client,
                new[] { new NpcDefinition("npc.mina", "Mina", "A careful shopkeeper.", "Hello") });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_world);
            _client?.Reply.TrySetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
        }

        [Test]
        public void DelayedVisit_ReleasesAtTheDisclosedWindowEvenWhenTheScheduleHasTheSameDestination()
        {
            _controller.TickDecisions(0);
            Assert.That(_services.WorldTime.AdvanceMinutes(10).IsSuccess, Is.True);
            _client.Reply.SetResult(new NpcDecisionReply(NpcDecisionKind.Visit, "work", NpcActivity.Working, 60));
            _controller.TickDecisions(1);

            var activity = _controller.GetAgentState("npc.mina").ActiveActivity;
            Assert.That(activity, Is.Not.Null);
            Assert.That(activity.ExpiresAtTotalMinutes, Is.EqualTo(780));
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 20)));
            Assert.That(_resident.TargetLocationId, Is.EqualTo("work"));
            Assert.That(_services.WorldTime.AdvanceMinutes(40).IsSuccess, Is.True);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(20, 0)));
            Assert.That(_resident.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.SameAs(activity));

            Assert.That(_services.WorldTime.AdvanceMinutes(9).IsSuccess, Is.True);
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.SameAs(activity));
            Assert.That(_services.WorldTime.AdvanceMinutes(1).IsSuccess, Is.True);
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.Null);
            Assert.That(_resident.TargetLocationId, Is.EqualTo("work"));
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(20, 0)));
            Assert.That(_client.Requests, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShortVisit_ExpiresOnTheRoadAndReturnsFromItsActualPositionWithoutAnotherDecision(bool smallSteps)
        {
            AcceptVisit(10);
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity.ExpiresAtTotalMinutes, Is.EqualTo(730));

            if (smallSteps)
            {
                for (int minute = 0; minute < 15; minute++)
                    Assert.That(_services.DaytimeClock.AdvanceElapsed(0.5).IsSuccess, Is.True);
            }
            else Assert.That(_services.WorldTime.AdvanceMinutes(15).IsSuccess, Is.True);

            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(735));
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.Null);
            Assert.That(_resident.TargetLocationId, Is.EqualTo("rest"));
            Assert.That(_resident.Position.x, Is.EqualTo(0).Within(0.001));
            Assert.That(_resident.Position.y, Is.EqualTo(15).Within(0.001),
                "After ten minutes outbound, expiry leaves five minutes to walk back toward rest.");
            Assert.That(_resident.Status, Is.EqualTo(TownRouteStatus.Travelling));
            Assert.That(_client.Requests, Is.EqualTo(1));
        }

        [Test]
        public void ModalPause_PreservesTheOrdinaryActivityAndItsAbsoluteDeadline()
        {
            AcceptVisit(60);
            var activity = _controller.GetAgentState("npc.mina").ActiveActivity;
            var player = new GameObject("Activity window player");
            player.transform.SetParent(_world.transform);
            player.SetActive(false);
            player.AddComponent<Rigidbody2D>().gravityScale = 0;
            var input = player.AddComponent<PlayModePlayerInputSource>();
            var movement = player.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);
            var probe = player.AddComponent<InteractionProbe2D>();
            player.AddComponent<PlayerInteractor2D>().Configure(input, probe);
            var gate = player.AddComponent<PlayerModalInputGate2D>();
            player.SetActive(true);
            var driverObject = new GameObject("Activity window clock");
            driverObject.transform.SetParent(_world.transform);
            driverObject.SetActive(false);
            var driver = driverObject.AddComponent<DaytimeClockDriver>();
            driver.ConfigureInputGate(gate);
            driver.Bind(_services.DaytimeClock);
            driverObject.SetActive(true);
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            driver.AdvanceFrame(5);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(730));
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 10)));

            var modalOwner = new object();
            Assert.That(gate.TryAcquire(modalOwner), Is.True);
            driver.AdvanceFrame(100);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(730));
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 10)));
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.SameAs(activity));
            Assert.That(activity.ExpiresAtTotalMinutes, Is.EqualTo(780));

            Assert.That(gate.Release(modalOwner), Is.True);
            driver.AdvanceFrame(100);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(730));
            driver.AdvanceFrame(25);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(780));
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.Null);
            Assert.That(_resident.TargetLocationId, Is.EqualTo("work"));
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(20, 0)));
            Assert.That(_client.Requests, Is.EqualTo(1));
        }

        private void AcceptVisit(double duration)
        {
            _controller.TickDecisions(0);
            _client.Reply.SetResult(new NpcDecisionReply(NpcDecisionKind.Visit, "work", NpcActivity.Working, duration));
            _controller.TickDecisions(1);
            Assert.That(_controller.GetDecisionOutcome("npc.mina").ActivityAccepted, Is.True);
        }

        private sealed class ControlledClient : INpcDecisionClient
        {
            public int Requests { get; private set; }
            public readonly TaskCompletionSource<NpcDecisionReply> Reply = new TaskCompletionSource<NpcDecisionReply>();

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests++;
                return Reply.Task;
            }
        }
    }
}
