using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Player;
using CozyTown.Unity.Town;
using CozyTown.Unity.Time;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class CompleteWorldSnapshotPlayModeTests
    {
        private GameObject _world;
        private CozyTownServices _services;
        private CozyTownTownLifeController _controller;
        private NpcWorldResident2D[] _residents;
        private PlayerMovement2D _player;
        private PlayerModalInputGate2D _gate;

        [TearDown] public void TearDown() => Object.DestroyImmediate(_world);

        [Test]
        public void SaveLoad_RestoresFourActualJourneysPlayerAndFractionalMinuteWithoutReplayingMovement()
        {
            CreateWorld();
            Assert.That(_services.DaytimeClock.AdvanceElapsed(1.75).IsSuccess, Is.True);
            var positions = _residents.Select(item => item.Position).ToArray();
            var oldRun = _controller.GetAgentState(_residents[0].NpcId).WorldRunId;
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            Assert.That(_services.SaveStorage.Load("main").Value.SchemaVersion, Is.EqualTo(4));
            Assert.That(_services.DaytimeClock.AdvanceElapsed(10).IsSuccess, Is.True);
            _player.GetComponent<Rigidbody2D>().position = new Vector2(100f, 100f);

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            Assert.That(_services.WorldTimeFlow.Current.FractionalMinute, Is.EqualTo(0.5));
            Assert.That(_controller.GetAgentState(_residents[0].NpcId).WorldRunId, Is.Not.EqualTo(oldRun));
            for (int i = 0; i < _residents.Length; i++)
                Assert.That(_residents[i].Position, Is.EqualTo(positions[i]));
            Assert.That(_player.GetComponent<Rigidbody2D>().position, Is.EqualTo(new Vector2(-3f, -3f)));
            Assert.That(_services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            for (int i = 0; i < _residents.Length; i++)
                Assert.That(_residents[i].Position.x, Is.EqualTo(positions[i].x + 0.5f).Within(0.0001f));
            Assert.That(_services.Time.Current.MinuteOfDay, Is.EqualTo(364));
        }

        [Test]
        public void LastBodyPreparationFailure_KeepsLiveWorldAndModalUntilValidLoadCommits()
        {
            CreateWorld();
            _services.DaytimeClock.AdvanceElapsed(1.75);
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var saved = _services.SaveStorage.Load("main").Value;
            var complete = saved.CompleteWorld;
            var bodies = complete.Residents.ToArray();
            var last = bodies[bodies.Length - 1];
            var route = last.Route;
            bodies[bodies.Length - 1] = new NpcBodySnapshot(last.NpcId,
                new TownRouteSnapshot(new Position2DSnapshot(999f, 999f), route.Facing,
                    route.TargetLocationId, route.Waypoints, route.WaypointIndex, route.Status, route.HasReplanned),
                last.Activity, last.NoLegalPosition);
            var corrupt = new CompleteWorldSnapshot(complete.ContentConfiguration, complete.BodyConfiguration,
                complete.World, complete.MeetingsEnabled, complete.Meetings, complete.DecisionsEnabled,
                complete.Decisions, bodies, complete.Player);
            Assert.That(_services.SaveStorage.Save("main", new GameSaveSnapshot(saved.SchemaVersion,
                saved.WorldSeed, saved.Clock, saved.Characters, saved.Shops, saved.Farm, saved.Livestock,
                saved.FractionalMinute, corrupt)).IsSuccess, Is.True);
            _services.DaytimeClock.AdvanceElapsed(1);
            var positions = _residents.Select(item => item.Position).ToArray();
            double time = _services.WorldTimeFlow.Current.TotalMinutes;
            var run = _controller.GetAgentState(_residents[0].NpcId).WorldRunId;
            Assert.That(_gate.TryAcquire(new object()), Is.True);
            bool revoked = false;
            _gate.AcquisitionRevoked += () => revoked = true;

            Assert.That(_services.GameSave.Load().IsSuccess, Is.False);

            Assert.That(_services.WorldTimeFlow.Current.TotalMinutes, Is.EqualTo(time));
            Assert.That(_controller.GetAgentState(_residents[0].NpcId).WorldRunId, Is.EqualTo(run));
            Assert.That(_gate.IsAcquired, Is.True);
            Assert.That(revoked, Is.False);
            for (int i = 0; i < _residents.Length; i++) Assert.That(_residents[i].Position, Is.EqualTo(positions[i]));
            Assert.That(_services.SaveStorage.Save("main", saved).IsSuccess, Is.True);
            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(_gate.IsAcquired, Is.False);
            Assert.That(revoked, Is.True);
        }

        [Test]
        public void ChangedObservationRules_RejectSnapshotBeforeRestoringWorld()
        {
            CreateWorld();
            _services.DaytimeClock.AdvanceElapsed(1.75);
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            _services.DaytimeClock.AdvanceElapsed(1);
            double liveTime = _services.WorldTimeFlow.Current.TotalMinutes;
            var observation = _world.GetComponent<TownLocalObservation2D>();
            observation.Configure(new NpcObservationScene(new[] {
                new NpcObservationRegion("new-region", "Changed neighborhood", -100, -100, 300, 300) },
                System.Array.Empty<NpcObservationEntity>(), radius: 7), System.Array.Empty<NpcObservationRegion>());

            Assert.That(_services.GameSave.Load().IsSuccess, Is.False);
            Assert.That(_services.WorldTimeFlow.Current.TotalMinutes, Is.EqualTo(liveTime));
        }

        [Test]
        public void SuccessfulLoadWithoutModal_DiscardsAccumulatedFrameBeforeResumingFractionalMovement()
        {
            CreateWorld();
            var driver = _world.AddComponent<DaytimeClockDriver>();
            driver.ConfigureInputGate(_gate);
            driver.Bind(_services.DaytimeClock);
            driver.SetApplicationFocus(true);
            _controller.ConfigureSnapshots(_services.WorldSnapshots, _player, _gate, driver, () => 0);
            driver.AdvanceFrame(0);
            driver.AdvanceFrame(1.75);
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            driver.AdvanceFrame(1);
            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);
            Vector2 restored = _residents[0].Position;

            driver.AdvanceFrame(100);
            Assert.That(_residents[0].Position, Is.EqualTo(restored));
            driver.AdvanceFrame(0.25);
            Assert.That(_residents[0].Position.x, Is.EqualTo(restored.x + 0.5f).Within(0.0001f));
        }

        [Test]
        public void LegacySnapshot_RebuildsSchedulesAndInitialPlayerThenRecordsMigrationOnNextSave()
        {
            CreateWorld();
            _services.DaytimeClock.AdvanceElapsed(1.75);
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var saved = _services.SaveStorage.Load("main").Value;
            var legacy = new GameSaveSnapshot(3, saved.WorldSeed, saved.Clock, saved.Characters,
                saved.Shops, saved.Farm, saved.Livestock);
            Assert.That(_services.SaveStorage.Save("main", legacy).IsSuccess, Is.True);
            _services.WorldTime.AdvanceMinutes(120);
            _player.GetComponent<Rigidbody2D>().position = Vector2.one;

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            Assert.That(_services.WorldTimeFlow.Current.FractionalMinute, Is.Zero);
            for (int i = 0; i < _residents.Length; i++)
                Assert.That(_residents[i].Position, Is.EqualTo(new Vector2(i * 50f, 0)));
            Assert.That(_player.GetComponent<Rigidbody2D>().position, Is.EqualTo(new Vector2(-3f, -3f)));
            Assert.That(_services.SaveStorage.Load("main").Value.SchemaVersion, Is.EqualTo(3));
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var migrated = _services.SaveStorage.Load("main").Value;
            Assert.That(migrated.SchemaVersion, Is.EqualTo(4));
            Assert.That(migrated.SourceSchemaVersion, Is.EqualTo(3));
        }

        private void CreateWorld()
        {
            _world = new GameObject("Full snapshot town");
            var map = _world.AddComponent<TownMap2D>();
            var homes = new List<TownHome>();
            var locations = new List<TownLocation>();
            var roads = new List<TownRoad>();
            _residents = new NpcWorldResident2D[4];
            var npcIds = new[] { DefaultMvpIds.Npcs.Shopkeeper, DefaultMvpIds.Npcs.Farmer,
                DefaultMvpIds.Npcs.Fisher, DefaultMvpIds.Npcs.Cook };
            for (int i = 0; i < 4; i++)
            {
                string npc = npcIds[i];
                string home = "home." + i;
                string outside = home + ".outside", entry = home + ".entry";
                string work = npc + ".work", rest = npc + ".rest";
                homes.Add(new TownHome(home, npc, outside, entry));
                locations.Add(new TownLocation(outside, new Vector2(i * 50f, 0)));
                locations.Add(new TownLocation(entry, new Vector2(i * 50f, 1)));
                locations.Add(new TownLocation(work, new Vector2(i * 50f + 20f, 0)));
                locations.Add(new TownLocation(rest, new Vector2(i * 50f + 20f, 10)));
                roads.Add(new TownRoad(entry, outside));
                roads.Add(new TownRoad(outside, work));
                roads.Add(new TownRoad(work, rest));
                var actor = new GameObject(npc);
                actor.transform.SetParent(_world.transform);
                var resident = actor.AddComponent<NpcWorldResident2D>();
                resident.Configure(map, new NpcDailySchedule(npc, home, outside, entry, work, rest, work,
                    360, 480, 720, 780, 1020, 1080), actor.AddComponent<SpriteRenderer>());
                _residents[i] = resident;
            }
            map.Configure(homes.ToArray(), locations.ToArray(), roads.ToArray());
            _services = CozyTownCompositionRoot.CreateDefault();
            _controller = _world.AddComponent<CozyTownTownLifeController>();
            _controller.Configure(_residents);
            _controller.Bind(_services.WorldTimeFlow, _services.ResourceTrading);
            var playerObject = new GameObject("Player");
            playerObject.SetActive(false);
            playerObject.transform.SetParent(_world.transform);
            playerObject.transform.position = new Vector2(-3f, -3f);
            var body = playerObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0;
            var input = playerObject.AddComponent<PlayModePlayerInputSource>();
            _player = playerObject.AddComponent<PlayerMovement2D>();
            _player.SetInputSource(input);
            var probe = playerObject.AddComponent<InteractionProbe2D>();
            playerObject.AddComponent<PlayerInteractor2D>().Configure(input, probe);
            _gate = playerObject.AddComponent<PlayerModalInputGate2D>();
            playerObject.SetActive(true);
            _controller.ConfigureSnapshots(_services.WorldSnapshots, _player, _gate, realSeconds: () => 0);
        }
    }
}
