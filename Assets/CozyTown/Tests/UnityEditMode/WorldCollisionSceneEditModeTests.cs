using System;
using System.Linq;
using CozyTown.Unity.Interaction;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class WorldCollisionSceneEditModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        [Test]
        public void DevelopmentScene_StoresSixSolidObstaclesOutsideInteractionHierarchy()
        {
            WithDevelopmentScene(scene =>
            {
                var world = RequireRoot(scene, "World");
                var obstacles = world.transform.Find("Obstacles");
                Assert.That(obstacles, Is.Not.Null);

                var obstacleNames = new[]
                {
                    "Shop Obstacle",
                    "Coop Obstacle",
                    "Kitchen Obstacle",
                    "Home Obstacle",
                    "Farm Obstacle",
                    "Pond Obstacle"
                };
                var colliders = obstacles.GetComponentsInChildren<Collider2D>(true);
                Assert.That(colliders, Has.Length.EqualTo(obstacleNames.Length));

                foreach (var obstacleName in obstacleNames)
                {
                    var obstacle = obstacles.Find(obstacleName);
                    Assert.That(obstacle, Is.Not.Null, $"{obstacleName} was not found.");
                    var collider = obstacle.GetComponent<PolygonCollider2D>();
                    Assert.That(collider, Is.Not.Null, $"{obstacleName} must use polygon geometry.");
                    Assert.That(collider.enabled, Is.True);
                    Assert.That(collider.isTrigger, Is.False);
                    Assert.That(collider.GetComponent<Rigidbody2D>(), Is.Null);
                    Assert.That(collider.GetComponentInParent<TownInteractionPoint2D>(), Is.Null);
                }
            });
        }

        [Test]
        public void DevelopmentScene_BuildingWallsLeaveShallowDoorIngressAndDoorOnlyTriggers()
        {
            WithDevelopmentScene(scene =>
            {
                var world = RequireRoot(scene, "World");
                var obstacles = world.transform.Find("Obstacles");
                Assert.That(obstacles, Is.Not.Null);
                Physics2D.SyncTransforms();

                var buildings = new[]
                {
                    new BuildingExpectation(
                        TownInteractionKind.Shop, "Shop Obstacle", new Vector2(-7f, 1.55f)),
                    new BuildingExpectation(
                        TownInteractionKind.Coop, "Coop Obstacle", new Vector2(0f, 1.6f)),
                    new BuildingExpectation(
                        TownInteractionKind.Kitchen, "Kitchen Obstacle", new Vector2(7.25f, 1.55f)),
                    new BuildingExpectation(
                        TownInteractionKind.Bed, "Home Obstacle", new Vector2(-6.65f, -3.45f))
                };

                foreach (var building in buildings)
                {
                    var point = FindUniquePoint(world, building.Kind);
                    var trigger = point.GetComponents<Collider2D>().SingleOrDefault();
                    Assert.That(trigger, Is.TypeOf<BoxCollider2D>());
                    Assert.That(trigger.isTrigger, Is.True);
                    Assert.That(trigger.bounds.center.x, Is.EqualTo(building.TriggerCenter.x).Within(0.01f));
                    Assert.That(trigger.bounds.center.y, Is.EqualTo(building.TriggerCenter.y).Within(0.01f));

                    var solid = obstacles.Find(building.ObstacleName)
                        ?.GetComponent<PolygonCollider2D>();
                    Assert.That(solid, Is.Not.Null);

                    var origin = (Vector2)point.transform.position;
                    var doorCenter = (Vector2)trigger.bounds.center;
                    var shallowIngress = new Vector2(doorCenter.x, origin.y + 0.35f);
                    var backWall = new Vector2(doorCenter.x, origin.y + 1.6f);
                    var sideWall = new Vector2(origin.x - 1.5f, origin.y + 0.6f);
                    var doorApproach = new Vector2(doorCenter.x, origin.y - 0.35f);
                    var nonDoorApproach = new Vector2(origin.x - 1.5f, origin.y - 0.35f);

                    Assert.That(solid.OverlapPoint(shallowIngress), Is.False,
                        $"{building.ObstacleName} closes the door entrance.");
                    Assert.That(solid.OverlapPoint(backWall), Is.True,
                        $"{building.ObstacleName} does not stop the player behind the doorway.");
                    Assert.That(solid.OverlapPoint(sideWall), Is.True,
                        $"{building.ObstacleName} leaves a pass-through wall.");
                    Assert.That(trigger.OverlapPoint(doorCenter), Is.True);
                    Assert.That(trigger.OverlapPoint(sideWall), Is.False,
                        $"{building.ObstacleName} exposes its interaction away from the door.");
                    Assert.That(
                        Physics2D.OverlapCircleAll(doorApproach, 0.75f)
                            .Any(hit => ReferenceEquals(hit, trigger)),
                        Is.True,
                        $"{building.ObstacleName} cannot be reached from its doorway.");
                    Assert.That(
                        Physics2D.OverlapCircleAll(nonDoorApproach, 0.75f)
                            .Any(hit => ReferenceEquals(hit, trigger)),
                        Is.False,
                        $"{building.ObstacleName} exposes E along a non-door wall.");
                }
            });
        }

        [Test]
        public void DevelopmentScene_FarmAndPondFollowTheirVisibleFootprints()
        {
            WithDevelopmentScene(scene =>
            {
                var world = RequireRoot(scene, "World");
                var obstacles = world.transform.Find("Obstacles");
                Assert.That(obstacles, Is.Not.Null);
                Physics2D.SyncTransforms();

                var farm = obstacles.Find("Farm Obstacle")?.GetComponent<PolygonCollider2D>();
                var pond = obstacles.Find("Pond Obstacle")?.GetComponent<PolygonCollider2D>();
                Assert.That(farm, Is.Not.Null);
                Assert.That(pond, Is.Not.Null);
                Assert.That(farm.GetPath(0), Has.Length.GreaterThanOrEqualTo(12));
                Assert.That(pond.GetPath(0), Has.Length.GreaterThanOrEqualTo(16));
                Assert.That(farm.bounds.min.x, Is.EqualTo(3.35f).Within(0.02f));
                Assert.That(farm.bounds.max.x, Is.EqualTo(8.65f).Within(0.02f));
                Assert.That(farm.bounds.min.y, Is.EqualTo(-3.95f).Within(0.02f));
                Assert.That(farm.bounds.max.y, Is.EqualTo(-0.05f).Within(0.02f));
                Assert.That(pond.bounds.min.x, Is.EqualTo(-2.5f).Within(0.02f));
                Assert.That(pond.bounds.max.x, Is.EqualTo(2.4f).Within(0.02f));
                Assert.That(pond.bounds.min.y, Is.EqualTo(-3.95f).Within(0.02f));
                Assert.That(pond.bounds.max.y, Is.EqualTo(-0.1f).Within(0.02f));
                Assert.That(farm.bounds.min.x - pond.bounds.max.x, Is.GreaterThan(0.6f));

                var pondPoint = FindUniquePoint(world, TownInteractionKind.Pond);
                var pondTrigger = pondPoint.GetComponents<Collider2D>().SingleOrDefault();
                Assert.That(pondTrigger, Is.TypeOf<PolygonCollider2D>());
                Assert.That(pondTrigger.isTrigger, Is.True);
                Assert.That(((PolygonCollider2D)pondTrigger).GetPath(0),
                    Is.EqualTo(pond.GetPath(0)));

                var fishingPositions = new[]
                {
                    new Vector2(0f, -4.35f),
                    new Vector2(0f, 0.3f),
                    new Vector2(-2.9f, -2f),
                    new Vector2(2.85f, -2f)
                };
                foreach (var position in fishingPositions)
                {
                    Assert.That(pond.OverlapPoint(position), Is.False,
                        $"Fishing position {position} is inside the solid pond.");
                    var hits = Physics2D.OverlapCircleAll(position, 0.75f);
                    Assert.That(hits.Any(hit => ReferenceEquals(hit, pondTrigger)), Is.True,
                        $"Fishing position {position} cannot reach the pond trigger.");
                }
            });
        }

        private static TownInteractionPoint2D FindUniquePoint(
            GameObject world,
            TownInteractionKind kind)
        {
            var points = world.GetComponentsInChildren<TownInteractionPoint2D>(true)
                .Where(point => point.Kind == kind)
                .ToArray();
            Assert.That(points, Has.Length.EqualTo(1),
                $"Expected one {kind} interaction point.");
            return points[0];
        }

        private static void WithDevelopmentScene(Action<Scene> assertion)
        {
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                assertion(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene);
                }
            }
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            var root = Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == name);
            Assert.That(root, Is.Not.Null, $"Root object '{name}' was not found.");
            return root;
        }

        private readonly struct BuildingExpectation
        {
            public BuildingExpectation(
                TownInteractionKind kind,
                string obstacleName,
                Vector2 triggerCenter)
            {
                Kind = kind;
                ObstacleName = obstacleName;
                TriggerCenter = triggerCenter;
            }

            public TownInteractionKind Kind { get; }
            public string ObstacleName { get; }
            public Vector2 TriggerCenter { get; }
        }
    }
}
