using System;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class BodyConfigurationPlayModeTests
    {
        private GameObject _world;
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_world);

        [Test]
        public void ResidentCapture_UsesActualJourneyAndRejectsAnotherResidentsBodyBeforeMutation()
        {
            _world = new GameObject("Snapshot resident");
            var map = _world.AddComponent<TownMap2D>();
            map.Configure(new[] { new TownHome("home.mina", "npc.mina", "outside", "entry") },
                new[] { new TownLocation("outside", Vector2.zero),
                    new TownLocation("entry", new Vector2(0, 0.5f)),
                    new TownLocation("work", new Vector2(20, 0)),
                    new TownLocation("rest", new Vector2(20, 10)) },
                new[] { new TownRoad("entry", "outside"), new TownRoad("outside", "work"),
                    new TownRoad("work", "rest") });
            var schedule = new NpcDailySchedule("npc.mina", "home.mina", "outside", "entry",
                "work", "rest", "work", 360, 480, 720, 780, 1020, 1080);
            var actor = new GameObject("Mina");
            actor.transform.SetParent(_world.transform);
            var resident = actor.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, schedule, actor.AddComponent<SpriteRenderer>());
            var services = CozyTownCompositionRoot.CreateDefault();
            var controller = _world.AddComponent<CozyTownTownLifeController>();
            controller.Configure(resident);
            controller.Bind(services.WorldTimeFlow);
            string configuration = resident.CaptureConfiguration();
            services.DaytimeClock.AdvanceElapsed(1.75);
            var saved = resident.CaptureSnapshot();
            var agents = new NpcAgentWorld(new[] { schedule });
            agents.Observe(services.WorldTimeFlow.Current);

            Assert.That(saved.Route.Position.X, Is.EqualTo(3.5f));
            Assert.That(saved.Activity, Is.EqualTo(NpcActivity.Working));
            resident.ValidateSnapshot(saved, agents, agents.TotalMinutes);
            var invalid = new NpcBodySnapshot("another.npc", saved.Route, saved.Activity, saved.NoLegalPosition);
            Assert.Throws<ArgumentException>(() => resident.ValidateSnapshot(invalid, agents, agents.TotalMinutes));
            Assert.That(resident.Position, Is.EqualTo(new Vector2(3.5f, 0f)));
            Assert.That(resident.CaptureConfiguration(), Is.EqualTo(configuration));
            resident.Configure(map, schedule, actor.GetComponent<SpriteRenderer>(), speed: 4f);
            Assert.That(resident.CaptureConfiguration(), Is.Not.EqualTo(configuration));
        }

        [Test]
        public void MapConfiguration_IdentifiesTopologyOrderAndStaticObstacleGeometry()
        {
            _world = new GameObject("Snapshot map");
            var map = _world.AddComponent<TownMap2D>();
            var locations = new[] { new TownLocation("start", Vector2.zero),
                new TownLocation("middle", Vector2.right), new TownLocation("end", Vector2.one) };
            var roads = new[] { new TownRoad("start", "middle"), new TownRoad("middle", "end") };
            map.Configure(Array.Empty<TownHome>(), locations, roads);
            string initial = map.CaptureConfiguration();
            map.Configure(Array.Empty<TownHome>(), locations, new[] { roads[1], roads[0] });
            Assert.That(map.CaptureConfiguration(), Is.Not.EqualTo(initial));
            map.Configure(Array.Empty<TownHome>(), locations, roads);
            Assert.That(map.CaptureConfiguration(), Is.EqualTo(initial));
            var wall = new GameObject("Wall");
            wall.transform.SetParent(_world.transform);
            var box = wall.AddComponent<BoxCollider2D>();
            string obstacle = map.CaptureConfiguration();
            Assert.That(obstacle, Is.Not.EqualTo(initial));
            box.size = new Vector2(2f, 3f);
            Assert.That(map.CaptureConfiguration(), Is.Not.EqualTo(obstacle));
        }
    }
}
