using System.Collections.Generic;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class NpcAgentActivityPlayModeTests
    {
        private GameObject _world;
        private CozyTownServices _services;
        private CozyTownTownLifeController _controller;
        private NpcWorldResident2D _resident;
        private readonly List<Object> _resources = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _world = new GameObject("Agent activity fixture");
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
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_world);
            foreach (var resource in _resources) Object.DestroyImmediate(resource);
            _resources.Clear();
        }

        [Test]
        public void AcceptedActivity_ChangesDestinationWithoutTeleportingAndSurvivesClockUpdates()
        {
            _services.DaytimeClock.AdvanceElapsed(2.5);
            Vector2 before = _resident.Position;
            Assert.That(Visit("rest", 420).IsSuccess, Is.True);
            Assert.That(_resident.TargetLocationId, Is.EqualTo("rest"));
            Assert.That(_resident.Position, Is.EqualTo(before));

            _services.DaytimeClock.AdvanceElapsed(15);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 20)));
            Assert.That(_resident.TargetLocationId, Is.EqualTo("rest"));
            Assert.That(_resident.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.Not.Null);
        }

        private OperationResult Visit(string location, double expires)
        {
            var state = _controller.GetAgentState("npc.mina");
            return _controller.SubmitActivity(new NpcActivityRequest(state.NpcId, state.WorldRunId,
                state.Revision, location, NpcActivity.Resting, expires));
        }

        [Test]
        public void ExplicitAdvanceAfterPartialMinute_UsesTheAcceptedActivityForTheWholeDuration()
        {
            _services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0.5f, 0)));
            Assert.That(Visit("rest", 380).IsSuccess, Is.True);

            Assert.That(_services.WorldTime.AdvanceMinutes(4).IsSuccess, Is.True);

            Assert.That(_resident.Position.x, Is.EqualTo(0).Within(0.001));
            Assert.That(_resident.Position.y, Is.EqualTo(3.5f).Within(0.001),
                "The accepted four minutes include returning 0.5 units to the road junction and moving 3.5 units toward rest.");
            Assert.That(_resident.TargetLocationId, Is.EqualTo("rest"));
        }

        [Test]
        public void SleepAfterPartialMinute_DoesNotBrieflyResumeThePrecedingSchedule()
        {
            _world.GetComponent<TownMap2D>().Configure(
                new[] { new TownHome("home.mina", "npc.mina", "outside", "entry") },
                new[] { new TownLocation("outside", Vector2.zero), new TownLocation("entry", new Vector2(0, 0.5f)),
                    new TownLocation("work", new Vector2(20, 0)), new TownLocation("rest", new Vector2(0, 200)) },
                new[] { new TownRoad("entry", "outside"), new TownRoad("outside", "work"), new TownRoad("outside", "rest") });
            _services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(Visit("rest", 600).IsSuccess, Is.True);

            Assert.That(_services.Sleep.SleepForMinutes(60).IsSuccess, Is.True);

            Assert.That(_resident.Position.x, Is.EqualTo(0).Within(0.001));
            Assert.That(_resident.Position.y, Is.EqualTo(59.5f).Within(0.001));
        }

        [Test]
        public void OneResidentsActivity_DoesNotResetAnotherResidentsWalkFrame()
        {
            var otherWorld = new GameObject("Ren activity fixture");
            otherWorld.transform.SetParent(_world.transform);
            var map = otherWorld.AddComponent<TownMap2D>();
            map.Configure(new[] { new TownHome("home.ren", "npc.ren", "outside", "entry") },
                new[] { new TownLocation("outside", new Vector2(30, 0)), new TownLocation("entry", new Vector2(30, 0.5f)),
                    new TownLocation("work", new Vector2(50, 0)), new TownLocation("rest", new Vector2(30, 20)) },
                new[] { new TownRoad("entry", "outside"), new TownRoad("outside", "work"), new TownRoad("outside", "rest") });
            var actor = new GameObject("Ren");
            actor.transform.SetParent(otherWorld.transform);
            var renderer = actor.AddComponent<SpriteRenderer>();
            var idle = new Sprite[4];
            var walk = new Sprite[8];
            for (int i = 0; i < idle.Length; i++) idle[i] = Frame("Idle " + i);
            for (int i = 0; i < walk.Length; i++) walk[i] = Frame("Walk " + i);
            actor.AddComponent<CozyTownNpcSpriteAnimator>().Configure(renderer, idle, walk);
            var other = actor.AddComponent<NpcWorldResident2D>();
            other.Configure(map, new NpcDailySchedule("npc.ren", "home.ren", "outside", "entry",
                "work", "rest", "work", 360, 480, 720, 780, 1020, 1080), renderer);
            _controller.Configure(_resident, other);
            _services = CozyTownCompositionRoot.CreateDefault();
            _controller.Bind(_services.WorldTimeFlow);
            _services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(renderer.sprite, Is.SameAs(walk[5]));
            Vector2 position = other.Position;

            Assert.That(Visit("rest", 420).IsSuccess, Is.True);
            Assert.That(renderer.sprite, Is.SameAs(walk[5]));
            var state = _controller.GetAgentState("npc.mina");
            Assert.That(_controller.CancelActivity(state.NpcId, state.WorldRunId, state.Revision).IsSuccess, Is.True);
            Assert.That(renderer.sprite, Is.SameAs(walk[5]));
            Assert.That(other.Position, Is.EqualTo(position));
            _services.DaytimeClock.AdvanceElapsed(0.0625);
            Assert.That(renderer.sprite, Is.SameAs(walk[5]), "The existing walk phase must continue, not restart.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Expiry_SplitsTheJourneyInBothLargeAndSmallAdvances(bool smallSteps)
        {
            Assert.That(Visit("rest", 365).IsSuccess, Is.True);
            if (smallSteps)
            {
                for (int i = 0; i < 20; i++)
                    Assert.That(_services.DaytimeClock.AdvanceElapsed(0.5).IsSuccess, Is.True);
            }
            else
            {
                Assert.That(_services.WorldTime.AdvanceMinutes(20).IsSuccess, Is.True);
            }

            Assert.That(_resident.Position.x, Is.EqualTo(10).Within(0.001));
            Assert.That(_resident.Position.y, Is.EqualTo(0).Within(0.001));
            Assert.That(_resident.TargetLocationId, Is.EqualTo("work"));
            Assert.That(_resident.Status, Is.EqualTo(TownRouteStatus.Travelling));
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.Null);
        }

        [Test]
        public void CancelAfterScheduleChange_ResumesFromTheActualPosition()
        {
            Assert.That(Visit("rest", 900).IsSuccess, Is.True);
            Assert.That(_services.WorldTime.AdvanceMinutes(430).IsSuccess, Is.True);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 20)));
            var state = _controller.GetAgentState("npc.mina");

            Assert.That(_controller.CancelActivity(state.NpcId, state.WorldRunId, state.Revision).IsSuccess, Is.True);

            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 20)));
            Assert.That(_resident.TargetLocationId, Is.EqualTo("work"));
            Assert.That(_services.DaytimeClock.AdvanceElapsed(0.5).IsSuccess, Is.True);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 19)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SameClockBinding_PreservesActivityAndContinuesWhileDisabled(bool disablePresentation)
        {
            Assert.That(Visit("rest", 420).IsSuccess, Is.True);
            _services.DaytimeClock.AdvanceElapsed(2.5);
            var state = _controller.GetAgentState("npc.mina");
            Vector2 position = _resident.Position;
            _controller.enabled = !disablePresentation;
            _resident.enabled = !disablePresentation;

            _controller.Bind(_services.WorldTimeFlow);

            Assert.That(_resident.Position, Is.EqualTo(position));
            Assert.That(_controller.GetAgentState("npc.mina").WorldRunId, Is.EqualTo(state.WorldRunId));
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.SameAs(state.ActiveActivity));
            Assert.That(_services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            Assert.That(_resident.Position.y, Is.EqualTo(5.5).Within(0.001));
            Assert.That(_resident.GetComponent<SpriteRenderer>().enabled, Is.EqualTo(!disablePresentation));
        }

        [Test]
        public void SuccessfulLoad_ClearsActivityAndRejectsTheOldWorldRequest()
        {
            var initial = _controller.GetAgentState("npc.mina");
            var oldRequest = new NpcActivityRequest(initial.NpcId, initial.WorldRunId, initial.Revision,
                "rest", NpcActivity.Resting, 420);
            Assert.That(_controller.SubmitActivity(oldRequest).IsSuccess, Is.True);
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            _services.WorldTime.AdvanceMinutes(20);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 20)));

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            var rebuilt = _controller.GetAgentState("npc.mina");
            Assert.That(rebuilt.ActiveActivity, Is.Null);
            Assert.That(rebuilt.WorldRunId, Is.Not.EqualTo(initial.WorldRunId));
            Assert.That(_resident.Position, Is.EqualTo(Vector2.zero));
            Assert.That(_resident.TargetLocationId, Is.EqualTo("work"));
            Assert.That(_controller.SubmitActivity(oldRequest).ErrorCode, Is.EqualTo("agent.world_stale"));
        }

        [Test]
        public void DifferentWorldBinding_DetachesTheOldClockAndClearsItsActivity()
        {
            Assert.That(Visit("rest", 420).IsSuccess, Is.True);
            _services.DaytimeClock.AdvanceElapsed(2.5);
            var previous = _controller.GetAgentState("npc.mina");
            var nextServices = CozyTownCompositionRoot.CreateDefault();

            _controller.Bind(nextServices.WorldTimeFlow);

            Assert.That(_controller.GetAgentState("npc.mina").WorldRunId, Is.Not.EqualTo(previous.WorldRunId));
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.Null);
            Assert.That(_resident.Position, Is.EqualTo(Vector2.zero));
            Assert.That(_services.DaytimeClock.AdvanceElapsed(5).IsSuccess, Is.True);
            Assert.That(_resident.Position, Is.EqualTo(Vector2.zero));
            Assert.That(nextServices.DaytimeClock.AdvanceElapsed(0.5).IsSuccess, Is.True);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(1, 0)));
        }

        [Test]
        public void UnreachableActivity_DoesNotChangePositionTargetOrRevision()
        {
            _world.GetComponent<TownMap2D>().Configure(
                new[] { new TownHome("home.mina", "npc.mina", "outside", "entry") },
                new[] { new TownLocation("outside", Vector2.zero), new TownLocation("entry", new Vector2(0, 0.5f)),
                    new TownLocation("work", new Vector2(20, 0)), new TownLocation("rest", new Vector2(0, 20)),
                    new TownLocation("island", new Vector2(100, 100)) },
                new[] { new TownRoad("entry", "outside"), new TownRoad("outside", "work"), new TownRoad("outside", "rest") });
            _services.DaytimeClock.AdvanceElapsed(2.5);
            var initial = _controller.GetAgentState("npc.mina");
            Vector2 position = _resident.Position;

            Assert.That(Visit("island", 420).ErrorCode, Is.EqualTo("agent.target_unavailable"));
            Assert.That(Visit("missing", 420).ErrorCode, Is.EqualTo("agent.target_unavailable"));

            Assert.That(_controller.GetAgentState("npc.mina").Revision, Is.EqualTo(initial.Revision));
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.Null);
            Assert.That(_resident.TargetLocationId, Is.EqualTo("work"));
            Assert.That(_resident.Position, Is.EqualTo(position));
        }

        [Test]
        public void FailedTimeAndLoad_PreserveTheActiveActivityAndFractionalProgress()
        {
            Assert.That(Visit("rest", 420).IsSuccess, Is.True);
            _services.DaytimeClock.AdvanceElapsed(2.25);
            Vector2 position = _resident.Position;
            var state = _controller.GetAgentState("npc.mina");
            var farm = _services.Farm.CaptureSnapshot();
            _services.Farm.AdvanceDay(2);

            Assert.That(_services.DaytimeClock.AdvanceElapsed(100).IsSuccess, Is.False);
            Assert.That(_services.Sleep.SleepForMinutes(60).IsSuccess, Is.False);
            Assert.That(_services.GameSave.Load().IsSuccess, Is.False);

            Assert.That(_controller.GetAgentState("npc.mina").WorldRunId, Is.EqualTo(state.WorldRunId));
            Assert.That(_controller.GetAgentState("npc.mina").Revision, Is.EqualTo(state.Revision));
            Assert.That(_controller.GetAgentState("npc.mina").ActiveActivity, Is.SameAs(state.ActiveActivity));
            Assert.That(_resident.Position, Is.EqualTo(position));
            _services.Farm.Restore(farm);
            Assert.That(_services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            Assert.That(_resident.Position, Is.EqualTo(new Vector2(0, 5)));
        }

        private Sprite Frame(string name)
        {
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = name;
            _resources.Add(sprite);
            _resources.Add(texture);
            return sprite;
        }
    }
}
