using System;
using System.IO;
using System.Linq;
using CozyTown.Unity.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class WorldCollisionSceneEditModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private const string RoofForegroundPath =
            "Assets/CozyTown/Art/Production/Buildings/bld_town_roof_foregrounds_64.png";

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
                    var leftWall = new Vector2(origin.x - 1.5f, origin.y + 0.6f);
                    var rightWall = new Vector2(origin.x + 1.5f, origin.y + 0.6f);
                    var upperBackArea = new Vector2(origin.x, origin.y + 3.2f);
                    var doorApproach = new Vector2(doorCenter.x, origin.y - 0.35f);
                    var nonDoorApproach = new Vector2(origin.x - 1.5f, origin.y - 0.35f);

                    Assert.That(solid.OverlapPoint(shallowIngress), Is.False,
                        $"{building.ObstacleName} closes the door entrance.");
                    Assert.That(solid.OverlapPoint(backWall), Is.True,
                        $"{building.ObstacleName} does not stop the player behind the doorway.");
                    Assert.That(solid.OverlapPoint(leftWall), Is.True,
                        $"{building.ObstacleName} leaves a pass-through left wall.");
                    Assert.That(solid.OverlapPoint(rightWall), Is.True,
                        $"{building.ObstacleName} leaves a pass-through right wall.");
                    Assert.That(solid.bounds.max.y, Is.EqualTo(origin.y + 2.4f).Within(0.01f),
                        $"{building.ObstacleName} must reserve its upper two-fifths for back-side travel.");
                    Assert.That(solid.OverlapPoint(upperBackArea), Is.False,
                        $"{building.ObstacleName} blocks the upper back-side travel region.");
                    Assert.That(trigger.OverlapPoint(doorCenter), Is.True);
                    Assert.That(trigger.OverlapPoint(leftWall), Is.False,
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
        public void DevelopmentScene_BuildingsKeepOnlyTheirUpperTwoFifthsAboveThePlayer()
        {
            WithDevelopmentScene(scene =>
            {
                var world = RequireRoot(scene, "World");
                var playerRenderer = RequireRoot(scene, "Player")
                    .GetComponentInChildren<SpriteRenderer>(true);
                Assert.That(playerRenderer, Is.Not.Null);

                var buildings = new[]
                {
                    new BuildingForegroundExpectation(
                        TownInteractionKind.Shop, "bld_shop_roof_foreground"),
                    new BuildingForegroundExpectation(
                        TownInteractionKind.Coop, "bld_coop_roof_foreground"),
                    new BuildingForegroundExpectation(
                        TownInteractionKind.Kitchen, "bld_kitchen_roof_foreground"),
                    new BuildingForegroundExpectation(
                        TownInteractionKind.Bed, "bld_home_roof_foreground")
                };

                foreach (BuildingForegroundExpectation building in buildings)
                {
                    TownInteractionPoint2D point = FindUniquePoint(world, building.Kind);
                    SpriteRenderer baseRenderer = point.transform.Find("Visual")
                        ?.GetComponent<SpriteRenderer>();
                    SpriteRenderer foreground = point.transform.Find("Roof Foreground")
                        ?.GetComponent<SpriteRenderer>();
                    Assert.That(baseRenderer, Is.Not.Null, building.Kind.ToString());
                    Assert.That(foreground, Is.Not.Null,
                        $"{building.Kind} is missing its roof foreground renderer.");
                    Assert.That(foreground.sprite, Is.Not.Null);
                    Assert.That(foreground.sprite.name, Is.EqualTo(building.SpriteName));
                    Assert.That(
                        AssetDatabase.GetAssetPath(foreground.sprite),
                        Is.EqualTo(RoofForegroundPath));
                    Assert.That(foreground.sortingOrder, Is.GreaterThan(playerRenderer.sortingOrder));
                    Assert.That(baseRenderer.sortingOrder, Is.LessThan(playerRenderer.sortingOrder));
                    Assert.That(foreground.bounds.center, Is.EqualTo(baseRenderer.bounds.center));
                    Assert.That(foreground.bounds.size, Is.EqualTo(baseRenderer.bounds.size));

                    AssertRoofForegroundPixels(baseRenderer.sprite, foreground.sprite);
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

        private static void AssertRoofForegroundPixels(Sprite baseSprite, Sprite foregroundSprite)
        {
            Color32[] basePixels = LoadSpritePixels(baseSprite, out int width, out int height);
            Color32[] foregroundPixels = LoadSpritePixels(
                foregroundSprite,
                out int foregroundWidth,
                out int foregroundHeight);
            Assert.That(foregroundWidth, Is.EqualTo(width));
            Assert.That(foregroundHeight, Is.EqualTo(height));
            Assert.That(width, Is.EqualTo(64));
            Assert.That(height, Is.EqualTo(64));

            const int transparentBottomRows = 38;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (y < transparentBottomRows)
                    {
                        Assert.That(foregroundPixels[index].a, Is.Zero,
                            $"Roof foreground contains a facade pixel at ({x}, {y}).");
                        continue;
                    }

                    Assert.That(
                        foregroundPixels[index],
                        Is.EqualTo(basePixels[index]),
                        $"Roof foreground diverges from the building roof at ({x}, {y}).");
                }
            }

            Assert.That(
                foregroundPixels.Skip(transparentBottomRows * width).Any(pixel => pixel.a > 0),
                Is.True,
                "Roof foreground does not contain any visible roof pixels.");
        }

        private static Color32[] LoadSpritePixels(
            Sprite sprite,
            out int width,
            out int height)
        {
            string assetPath = AssetDatabase.GetAssetPath(sprite);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(texture, File.ReadAllBytes(assetPath), false),
                    Is.True,
                    assetPath);
                Rect rect = sprite.rect;
                width = Mathf.RoundToInt(rect.width);
                height = Mathf.RoundToInt(rect.height);
                return texture.GetPixels32()
                    .Where((_, index) =>
                    {
                        int x = index % texture.width;
                        int y = index / texture.width;
                        return x >= Mathf.RoundToInt(rect.x)
                            && x < Mathf.RoundToInt(rect.xMax)
                            && y >= Mathf.RoundToInt(rect.y)
                            && y < Mathf.RoundToInt(rect.yMax);
                    })
                    .ToArray();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
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

        private readonly struct BuildingForegroundExpectation
        {
            public BuildingForegroundExpectation(TownInteractionKind kind, string spriteName)
            {
                Kind = kind;
                SpriteName = spriteName;
            }

            public TownInteractionKind Kind { get; }
            public string SpriteName { get; }
        }
    }
}
