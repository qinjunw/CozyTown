using System;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Lighting;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class NpcLocalObservationPlayModeTests
    {
        private GameObject _world;

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_world);

        [Test]
        public void AdvancingWorldTime_ObservesCommittedPositionBeforeReachingTheDestination()
        {
            _world = new GameObject("Local observation fixture");
            var map = _world.AddComponent<TownMap2D>();
            map.Configure(new[] { new TownHome("home.mina", "npc.mina", "outside", "entry") },
                new[] { new TownLocation("outside", Vector2.zero),
                    new TownLocation("entry", new Vector2(0, 0.5f)),
                    new TownLocation("work", new Vector2(20, 0)),
                    new TownLocation("rest", new Vector2(20, 10)) },
                new[] { new TownRoad("entry", "outside"), new TownRoad("outside", "work"),
                    new TownRoad("work", "rest") });
            var actor = new GameObject("Mina");
            actor.transform.SetParent(_world.transform);
            var resident = actor.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, new NpcDailySchedule("npc.mina", "home.mina", "outside", "entry",
                "work", "rest", "work", 360, 480, 720, 780, 1020, 1080),
                actor.AddComponent<SpriteRenderer>());

            var adapter = _world.AddComponent<TownLocalObservation2D>();
            adapter.Configure(new NpcObservationScene(
                new[] { new NpcObservationRegion("road", "Road", -1, -1, 11, 2),
                    new NpcObservationRegion("workplace", "Workplace", 11, -1, 21, 2) },
                new[] { new NpcObservationEntity("road-lamp", "Road lamp", 8, 0, "decoration"),
                    new NpcObservationEntity("work-sign", "Work sign", 20, 0, "landmark") }, radius: 3),
                Array.Empty<NpcObservationRegion>());
            var services = CozyTownCompositionRoot.CreateDefault();
            var controller = _world.AddComponent<CozyTownTownLifeController>();
            controller.Configure(resident);
            controller.ConfigureObservations(adapter);
            controller.Bind(services.WorldTimeFlow);

            Assert.That(services.DaytimeClock.AdvanceElapsed(5).IsSuccess, Is.True);
            var travelling = controller.GetObservation("npc.mina");
            Assert.That(resident.TargetLocationId, Is.EqualTo("work"));
            Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Travelling));
            Assert.That(travelling.X, Is.EqualTo(10).Within(0.001));
            Assert.That(travelling.RegionId, Is.EqualTo("road"));
            Assert.That(travelling.ObservedAtTotalMinutes, Is.EqualTo(370));
            CollectionAssert.AreEqual(new[] { "road-lamp" }, travelling.NearbyEntityIds);
            actor.transform.rotation = Quaternion.Euler(0, 0, 180);
            CollectionAssert.AreEqual(travelling.NearbyEntityIds, controller.GetObservation("npc.mina").NearbyEntityIds,
                "Region and distance observations do not change with body rotation.");

            Assert.That(services.DaytimeClock.AdvanceElapsed(5).IsSuccess, Is.True);
            var arrived = controller.GetObservation("npc.mina");
            Assert.That(arrived.X, Is.EqualTo(20).Within(0.001));
            Assert.That(arrived.RegionId, Is.EqualTo("workplace"));
            CollectionAssert.AreEqual(new[] { "work-sign" }, arrived.NearbyEntityIds);
            Assert.That(travelling.X, Is.EqualTo(10).Within(0.001),
                "A later body commit must not mutate a previously captured observation.");
            Assert.That(travelling.ObservedAtTotalMinutes, Is.EqualTo(370));
        }

        [TestCase(-3, -1.4)]
        [TestCase(-3, 0.4)]
        public void ReviewedDevelopmentMap_ObservesPondFromBothMeetingStandingLocations(double x, double y)
        {
            var map = CreateReviewedDevelopmentMap();
            var adapter = _world.AddComponent<TownLocalObservation2D>();

            Assert.That(adapter.TryConfigureDevelopmentMap(map), Is.True);
            var observation = adapter.Scene.Read("observer", Guid.NewGuid(), 780,
                new[] { new NpcObservationBody("observer", x, y) });

            Assert.That(observation.RegionId, Is.EqualTo("pond-surroundings"));
            CollectionAssert.AreEquivalent(new[] { "pond", "lamp.shop_main_road", "lamp.player_home" },
                observation.NearbyEntityIds);
        }

        [Test]
        public void LeavingHome_KeepsInteriorSpaceUntilTheBodyCrossesTheDoorway()
        {
            var map = CreateReviewedDevelopmentMap();
            var actor = CreateChild(_world.transform, "Ren", Vector2.zero).gameObject;
            var resident = actor.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, new NpcDailySchedule(DefaultMvpIds.Npcs.Fisher, "home.fisher_ren",
                "home.fisher_ren.doorstep", "home.fisher_ren.entry", "work.fisher_ren.morning",
                "rest.fisher_ren", "work.fisher_ren.morning", 400, 480, 720, 780, 1020, 1080),
                actor.AddComponent<SpriteRenderer>());
            var adapter = _world.AddComponent<TownLocalObservation2D>();
            Assert.That(adapter.TryConfigureDevelopmentMap(map), Is.True);
            var controller = _world.AddComponent<CozyTownTownLifeController>();
            controller.Configure(resident);
            controller.ConfigureObservations(adapter);
            var services = CozyTownCompositionRoot.CreateDefault();
            controller.Bind(services.WorldTimeFlow);

            Assert.That(resident.IsHome, Is.True);
            Assert.That(adapter.CaptureBodies(new[] { resident })[0].Observable, Is.False);
            Assert.That(services.WorldTime.AdvanceMinutes(40).IsSuccess, Is.True);
            Assert.That(resident.IsHome, Is.False);
            Assert.That(resident.TargetLocationId, Is.EqualTo("work.fisher_ren.morning"));
            Assert.That(resident.Position.y, Is.EqualTo(10.25).Within(0.001));
            var atDoor = controller.GetObservation(DefaultMvpIds.Npcs.Fisher);
            Assert.That(atDoor.SpaceId, Is.EqualTo("home.fisher_ren"),
                "Changing activity must not move a body through the doorway.");
            Assert.That(atDoor.RegionId, Is.EqualTo("home.fisher_ren"));

            Assert.That(services.DaytimeClock.AdvanceElapsed(0.2).IsSuccess, Is.True);
            Assert.That(resident.Position.y, Is.LessThan(10));
            var outside = controller.GetObservation(DefaultMvpIds.Npcs.Fisher);
            Assert.That(outside.SpaceId, Is.EqualTo("outdoors"));
            Assert.That(outside.RegionId, Is.EqualTo("town-north"));
            Assert.That(atDoor.SpaceId, Is.EqualTo("home.fisher_ren"));
        }

        [Test]
        public void ScheduledDecision_ReceivesTheCommittedBodyObservationAtDispatch()
        {
            var map = CreateReviewedDevelopmentMap();
            var actor = CreateChild(_world.transform, "Ren", Vector2.zero).gameObject;
            var resident = actor.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, new NpcDailySchedule(DefaultMvpIds.Npcs.Fisher, "home.fisher_ren",
                "home.fisher_ren.doorstep", "home.fisher_ren.entry", "work.fisher_ren.morning",
                "rest.fisher_ren", "work.fisher_ren.morning", 400, 480, 720, 780, 1020, 1080),
                actor.AddComponent<SpriteRenderer>());
            var controller = _world.AddComponent<CozyTownTownLifeController>();
            controller.Configure(resident);
            var services = CozyTownCompositionRoot.CreateDefault();
            controller.Bind(services.WorldTimeFlow);
            Assert.That(services.WorldTime.AdvanceMinutes(360).IsSuccess, Is.True);
            Assert.That(resident.TargetLocationId, Is.EqualTo("rest.fisher_ren"));
            Assert.That(resident.Position, Is.EqualTo(new Vector2(-4.2f, -3)));
            var client = new RecordingDecisionClient();
            controller.ConfigureDecisions(client, new[] {
                new NpcDefinition(DefaultMvpIds.Npcs.Fisher, "Ren", "A town fisher.", "Hello") });

            controller.TickDecisions(0);

            Assert.That(client.Request, Is.Not.Null);
            Assert.That(client.Request.Observation, Is.Not.Null);
            Assert.That(client.Request.Observation.X, Is.EqualTo(-4.2).Within(0.001));
            Assert.That(client.Request.Observation.Y, Is.EqualTo(-3).Within(0.001));
            Assert.That(client.Request.Observation.ObservedAtTotalMinutes, Is.EqualTo(720));
            Assert.That(client.Request.Observation.RegionId, Is.EqualTo("pond-surroundings"));
            CollectionAssert.Contains(client.Request.Observation.NearbyEntityIds, "pond");
            Assert.That(controller.GetObservation(DefaultMvpIds.Npcs.Fisher).X,
                Is.EqualTo(-4.2).Within(0.001));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DefaultCatalog_RefreshesChangedObjectsWithoutMutatingThePreviousObservation(bool move)
        {
            var map = CreateReviewedDevelopmentMap();
            var adapter = _world.AddComponent<TownLocalObservation2D>();
            Assert.That(adapter.TryConfigureDevelopmentMap(map), Is.True);
            var bodies = new[] { new NpcObservationBody("observer", -3, -1.4) };
            var runId = Guid.NewGuid();
            var previous = adapter.Scene.Read("observer", runId, 780, bodies);
            CollectionAssert.Contains(previous.NearbyEntityIds, "lamp.shop_main_road");
            var lamp = _world.transform.Find("Town Lighting/Shop Main Road Lamp");
            if (move) lamp.position = new Vector2(12, 12);
            else lamp.gameObject.SetActive(false);

            var current = adapter.Scene.Read("observer", runId, 781, bodies);

            CollectionAssert.DoesNotContain(current.NearbyEntityIds, "lamp.shop_main_road");
            CollectionAssert.Contains(current.NearbyEntityIds, "pond");
            CollectionAssert.Contains(previous.NearbyEntityIds, "lamp.shop_main_road");
            Assert.That(previous.ObservedAtTotalMinutes, Is.EqualTo(780));
        }

        [Test]
        public void UnrecognizedMap_LeavesTheRegionUnknownWithoutRegisteringDevelopmentEntities()
        {
            _world = new GameObject("Unrecognized map fixture");
            var map = _world.AddComponent<TownMap2D>();
            map.Configure(Array.Empty<TownHome>(), Array.Empty<TownLocation>(), Array.Empty<TownRoad>());
            var adapter = _world.AddComponent<TownLocalObservation2D>();

            Assert.That(adapter.TryConfigureDevelopmentMap(map), Is.False);
            var observation = adapter.Scene.Read("observer", Guid.NewGuid(), 780,
                new[] { new NpcObservationBody("observer", -3, -1.4) });

            Assert.That(observation.RegionId, Is.Null);
            Assert.That(observation.NearbyEntityIds, Is.Empty);
            Assert.That(observation.NearbyComplete, Is.False);
        }

        [Test]
        public void CaptureBodies_DistinguishesVisibleBlockedResidentsFromHiddenReconstructionFailures()
        {
            var map = CreateReviewedDevelopmentMap();
            var actor = CreateChild(_world.transform, "Ren", Vector2.zero).gameObject;
            var resident = actor.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, new NpcDailySchedule(DefaultMvpIds.Npcs.Fisher, "home.fisher_ren",
                "home.fisher_ren.doorstep", "home.fisher_ren.entry", "work.fisher_ren.morning",
                "rest.fisher_ren", "work.fisher_ren.morning", 360, 480, 720, 780, 1020, 1080),
                actor.AddComponent<SpriteRenderer>());
            var adapter = _world.AddComponent<TownLocalObservation2D>();
            Assert.That(adapter.TryConfigureDevelopmentMap(map), Is.True);
            var controller = _world.AddComponent<CozyTownTownLifeController>();
            controller.Configure(resident);
            controller.ConfigureObservations(adapter);
            var services = CozyTownCompositionRoot.CreateDefault();
            controller.Bind(services.WorldTimeFlow);
            Assert.That(services.WorldTime.AdvanceMinutes(120).IsSuccess, Is.True);
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            var workWall = CreateChild(_world.transform, "Work obstacle", new Vector2(-4.2f, -3))
                .gameObject.AddComponent<BoxCollider2D>();
            workWall.size = new Vector2(2, 2);

            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Blocked));
            Assert.That(adapter.CaptureBodies(new[] { resident })[0].Observable, Is.True);

            var allLocationsWall = CreateChild(_world.transform, "All locations obstacle", Vector2.zero)
                .gameObject.AddComponent<BoxCollider2D>();
            allLocationsWall.size = new Vector2(100, 100);
            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Blocked));
            Assert.That(adapter.CaptureBodies(new[] { resident })[0].Observable, Is.False);

            UnityEngine.Object.DestroyImmediate(allLocationsWall.gameObject);
            UnityEngine.Object.DestroyImmediate(workWall.gameObject);
            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(adapter.CaptureBodies(new[] { resident })[0].Observable, Is.True);
            resident.enabled = false;
            Assert.That(adapter.CaptureBodies(new[] { resident })[0].Observable, Is.False);
        }

        private sealed class RecordingDecisionClient : INpcDecisionClient
        {
            public NpcDecisionRequest Request { get; private set; }

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            }
        }

        private TownMap2D CreateReviewedDevelopmentMap()
        {
            _world = new GameObject("Reviewed development map fixture");
            var map = _world.AddComponent<TownMap2D>();
            map.Configure(new[] {
                    new TownHome("home.shopkeeper_mina", DefaultMvpIds.Npcs.Shopkeeper,
                        "home.shopkeeper_mina.doorstep", "home.shopkeeper_mina.entry"),
                    new TownHome("home.fisher_ren", DefaultMvpIds.Npcs.Fisher,
                        "home.fisher_ren.doorstep", "home.fisher_ren.entry"),
                    new TownHome("home.cook_sora", DefaultMvpIds.Npcs.Cook,
                        "home.cook_sora.doorstep", "home.cook_sora.entry"),
                    new TownHome("home.farmer_eli", DefaultMvpIds.Npcs.Farmer,
                        "home.farmer_eli.doorstep", "home.farmer_eli.entry") },
                new[] {
                    new TownLocation("home.shopkeeper_mina.doorstep", new Vector2(-10.15f, 9.5f)),
                    new TownLocation("home.shopkeeper_mina.entry", new Vector2(-10.15f, 10.25f)),
                    new TownLocation("home.fisher_ren.doorstep", new Vector2(-3.15f, 9.5f)),
                    new TownLocation("home.fisher_ren.entry", new Vector2(-3.15f, 10.25f)),
                    new TownLocation("home.cook_sora.doorstep", new Vector2(3.85f, 9.5f)),
                    new TownLocation("home.cook_sora.entry", new Vector2(3.85f, 10.25f)),
                    new TownLocation("home.farmer_eli.doorstep", new Vector2(10.85f, 9.5f)),
                    new TownLocation("home.farmer_eli.entry", new Vector2(10.85f, 10.25f)),
                    new TownLocation("work.fisher_ren.morning", new Vector2(-4.2f, -3)),
                    new TownLocation("rest.fisher_ren", new Vector2(-3, -1.4f)),
                    new TownLocation("road.west_lane", new Vector2(-3, 0.4f)) },
                new[] {
                    new TownRoad("home.fisher_ren.entry", "home.fisher_ren.doorstep"),
                    new TownRoad("home.fisher_ren.doorstep", "work.fisher_ren.morning"),
                    new TownRoad("work.fisher_ren.morning", "rest.fisher_ren") });

            var points = CreateChild(_world.transform, "Interaction Points", Vector2.zero);
            var pond = CreateChild(points, "Pond", new Vector2(0, -4));
            pond.gameObject.AddComponent<TownInteractionPoint2D>().Configure(TownInteractionKind.Pond, "Fish");
            var lighting = CreateChild(_world.transform, "Town Lighting", Vector2.zero);
            CreateChild(lighting, "Shop Main Road Lamp", new Vector2(-4.1875f, -0.25f))
                .gameObject.AddComponent<TownLamp2D>();
            CreateChild(lighting, "Player Home Lamp", new Vector2(-4.8125f, -4.5f))
                .gameObject.AddComponent<TownLamp2D>();
            CreateChild(lighting, "West Lane South Lamp", new Vector2(-3.4375f, 3.625f))
                .gameObject.AddComponent<TownLamp2D>();
            return map;
        }

        private static Transform CreateChild(Transform parent, string name, Vector2 position)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent);
            child.position = position;
            return child;
        }
    }
}
