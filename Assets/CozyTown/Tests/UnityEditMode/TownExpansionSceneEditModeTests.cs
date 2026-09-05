using System;
using System.IO;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Unity.Editor;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Town;
using CozyTown.Unity.Time;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class TownExpansionSceneEditModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        [Test]
        public void DevelopmentSceneAndRepeatedUpgrade_KeepOneDaytimeClockDriver()
        {
            WithDevelopmentScene(scene =>
            {
                var root = RequireRoot(scene, "CozyTown");
                Assert.That(root.GetComponents<DaytimeClockDriver>(), Has.Length.EqualTo(1));
                CozyTownDevSceneMenu.UpgradeTownWorld(scene);
                CozyTownDevSceneMenu.UpgradeTownWorld(scene);
                Assert.That(scene.GetRootGameObjects()
                    .SelectMany(item => item.GetComponentsInChildren<DaytimeClockDriver>(true))
                    .Count(), Is.EqualTo(1));
            });
        }

        [Test]
        public void DevelopmentScene_ProvidesContinuousThirtyTwoByTwentyTwoGroundForResidentialStreet()
        {
            WithDevelopmentScene(scene =>
            {
                var tilemap = RequireRoot(scene, "World").GetComponentInChildren<Tilemap>(true);
                Assert.That(tilemap, Is.Not.Null);
                Assert.That(tilemap.cellBounds.min, Is.EqualTo(new Vector3Int(-16, -6, 0)));
                Assert.That(tilemap.cellBounds.size, Is.EqualTo(new Vector3Int(32, 22, 1)));
                for (var y = -6; y < 16; y++)
                {
                    for (var x = -16; x < 16; x++)
                    {
                        Assert.That(tilemap.HasTile(new Vector3Int(x, y, 0)), Is.True,
                            $"Expanded town ground is missing the tile at ({x}, {y}).");
                    }
                }
            });
        }

        [Test]
        public void DevelopmentScene_ProvidesFourSolidNpcHomesWithoutAddingBusinessInteractions()
        {
            WithDevelopmentScene(scene =>
            {
                var world = RequireRoot(scene, "World");
                var homes = world.transform.Find("NPC Homes");
                Assert.That(homes, Is.Not.Null, "The residential street needs four separate NPC homes.");
                var expectedHomes = new[]
                {
                    "home.shopkeeper_mina", "home.fisher_ren", "home.cook_sora", "home.farmer_eli"
                };
                Assert.That(homes.childCount, Is.EqualTo(4));
                var player = RequireRoot(scene, "Player");
                var foot = player.GetComponent<CircleCollider2D>();
                var playerRenderer = player.GetComponentInChildren<SpriteRenderer>(true);
                Assert.That(foot, Is.Not.Null);
                Assert.That(playerRenderer?.sprite, Is.Not.Null);
                Physics2D.SyncTransforms();
                foreach (var homeId in expectedHomes)
                {
                    var home = homes.Find(homeId);
                    Assert.That(home, Is.Not.Null, homeId);
                    var facade = home.Find("Visual")?.GetComponent<SpriteRenderer>();
                    var roof = home.Find("Roof Foreground")?.GetComponent<SpriteRenderer>();
                    Assert.That(facade?.sprite, Is.Not.Null, homeId);
                    Assert.That(roof?.sprite, Is.Not.Null, homeId);
                    Assert.That(roof.sortingOrder, Is.GreaterThan(20), homeId);
                    Assert.That(facade.sortingOrder, Is.LessThan(20), homeId);
                    Assert.That(home.position.y, Is.GreaterThan(6f), homeId);
                    Assert.That(home.GetComponentsInChildren<TownInteractionPoint2D>(true), Is.Empty,
                        "NPC homes must not become extra beds or business entry points.");
                    var wallPosition = (Vector2)home.position + new Vector2(-1f, 1f);
                    Assert.That(Physics2D.OverlapPointAll(wallPosition)
                        .Any(collider => !collider.isTrigger && collider.transform.IsChildOf(world.transform)),
                        Is.True, $"{homeId} has no physical facade.");
                    var doorway = (Vector2)home.position + new Vector2(0.35f, 0.25f);
                    Assert.That(Physics2D.OverlapCircleAll(doorway, 0.3f)
                        .Any(collider => !collider.isTrigger && collider.transform.IsChildOf(world.transform)),
                        Is.False, $"{homeId} blocks its shallow door endpoint.");

                    var approach = (Vector2)home.position + new Vector2(0.35f, -0.35f);
                    var footOffset = Vector2.Scale(foot.offset, foot.transform.lossyScale);
                    var radius = foot.radius * Mathf.Abs(foot.transform.lossyScale.x);
                    var wallHits = Physics2D.CircleCastAll(
                            approach + footOffset, radius, Vector2.up, 2f)
                        .Where(hit => !hit.collider.isTrigger && hit.collider.transform.IsChildOf(world.transform))
                        .OrderBy(hit => hit.distance)
                        .ToArray();
                    Assert.That(wallHits, Is.Not.Empty, $"{homeId} has no door back stop.");
                    var playerHeadAboveFoot = playerRenderer.transform.position.y - player.transform.position.y
                        + playerRenderer.sprite.bounds.max.y * Mathf.Abs(playerRenderer.transform.lossyScale.y);
                    var deepestHeadY = approach.y + wallHits[0].distance + playerHeadAboveFoot;
                    var roofBottomY = roof.bounds.min.y + LowestOpaquePixelY(roof.sprite) / roof.sprite.pixelsPerUnit;
                    Assert.That(deepestHeadY, Is.LessThanOrEqualTo(roofBottomY - 1f / 16f),
                        $"{homeId} doorway hides the player's head at its deepest physical stop.");
                }

                var points = world.GetComponentsInChildren<TownInteractionPoint2D>(true);
                Assert.That(points.Length, Is.EqualTo(10));
                Assert.That(points.Count(point => point.Kind == TownInteractionKind.Bed), Is.EqualTo(1));
            });
        }

        [Test]
        public void DevelopmentScene_ConnectsEachOwnedHomeToWorkAndRestWithFootClearance()
        {
            WithDevelopmentScene(scene =>
            {
                var world = RequireRoot(scene, "World");
                var map = world.GetComponent<TownMap2D>();
                Assert.That(map, Is.Not.Null, "The expanded town needs a shared home and location map.");
                Assert.That(map.Homes.Count, Is.EqualTo(4));
                Assert.That(map.Homes.Select(home => home.HomeId).Distinct().Count(), Is.EqualTo(4));
                Assert.That(map.Homes.Select(home => home.NpcId).Distinct().Count(), Is.EqualTo(4));
                Physics2D.SyncTransforms();
                var fixtures = new[]
                {
                    new HomeRouteExpectation(DefaultMvpIds.Npcs.Shopkeeper, "home.shopkeeper_mina",
                        "work.shopkeeper_mina", "rest.shopkeeper_mina", new Vector2(-4.2f, 0.35f)),
                    new HomeRouteExpectation(DefaultMvpIds.Npcs.Farmer, "home.farmer_eli",
                        "work.farmer_eli", "rest.farmer_eli", new Vector2(9.1f, -2f)),
                    new HomeRouteExpectation(DefaultMvpIds.Npcs.Fisher, "home.fisher_ren",
                        "work.fisher_ren.morning", "rest.fisher_ren", new Vector2(-4.2f, -3f)),
                    new HomeRouteExpectation(DefaultMvpIds.Npcs.Cook, "home.cook_sora",
                        "work.cook_sora", "rest.cook_sora", new Vector2(3f, 0.35f))
                };
                foreach (var fixture in fixtures)
                {
                    Assert.That(map.TryGetHome(fixture.NpcId, out var home), Is.True, fixture.NpcId);
                    Assert.That(home.HomeId, Is.EqualTo(fixture.HomeId));
                    Assert.That(home.NpcId, Is.EqualTo(fixture.NpcId));
                    Assert.That(map.TryGetLocation(fixture.WorkId, out var work), Is.True);
                    Assert.That(work, Is.EqualTo(fixture.WorkPosition));
                    AssertReachable(map, home.EntryLocationId, home.DoorstepLocationId, world);
                    AssertReachable(map, home.DoorstepLocationId, fixture.WorkId, world);
                    AssertReachable(map, fixture.WorkId, fixture.RestId, world);
                    AssertReachable(map, fixture.RestId, home.EntryLocationId, world);
                }
                AssertReachable(map, "work.fisher_ren.morning", "work.fisher_ren.afternoon", world);
                AssertReachable(map, "road.west", "road.shop", world);
                AssertReachable(map, "road.kitchen", "road.east", world);
                AssertReachable(map, "rest.shopkeeper_mina", "rest.cook_sora", world);
                AssertReachable(map, "home.fisher_ren.street", "road.residential", world);
            });
        }

        private static void AssertReachable(TownMap2D map, string fromId, string toId, GameObject world)
        {
            Assert.That(map.TryGetLocation(fromId, out var start), Is.True, fromId);
            Assert.That(map.TryGetLocation(toId, out var destination), Is.True, toId);
            Assert.That(map.TryFindRoute(fromId, toId, out var route), Is.True, $"{fromId} -> {toId}");
            Assert.That(route.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(route[0], Is.EqualTo(start));
            Assert.That(route[route.Count - 1], Is.EqualTo(destination));
            for (var index = 0; index < route.Count; index++)
            {
                Assert.That(Physics2D.OverlapCircleAll(route[index], 0.3f)
                    .Any(collider => !collider.isTrigger && collider.transform.IsChildOf(world.transform)),
                    Is.False, $"{fromId} -> {toId}: stop {route[index]} has no foot clearance.");
                if (index == 0)
                {
                    continue;
                }
                var delta = route[index] - route[index - 1];
                Assert.That(Physics2D.CircleCastAll(route[index - 1], 0.3f, delta.normalized, delta.magnitude)
                    .Any(hit => !hit.collider.isTrigger && hit.collider.transform.IsChildOf(world.transform)),
                    Is.False, $"{fromId} -> {toId}: route crosses an obstacle between {route[index - 1]} and {route[index]}.");
            }
        }

        [Test]
        public void TownWorldUpgrade_RestoresCurrentLayoutTwiceWithoutDuplicatingHomesOrMovingLandmarks()
        {
            var fixturePath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/CozyTown/Tests/UnityEditMode/TownExpansionSceneFixture.unity");
            var previousScene = SceneManager.GetActiveScene();
            var fixture = default(Scene);
            Assert.That(AssetDatabase.CopyAsset(ScenePath, fixturePath), Is.True);
            try
            {
                fixture = EditorSceneManager.OpenScene(fixturePath, OpenSceneMode.Additive);
                var world = RequireRoot(fixture, "World");
                var ground = world.GetComponentInChildren<Tilemap>(true);
                ground.ClearAllTiles();
                UnityEngine.Object.DestroyImmediate(world.transform.Find("NPC Homes/home.shopkeeper_mina").gameObject);

                CozyTownDevSceneMenu.UpgradeTownWorld(fixture);
                CozyTownDevSceneMenu.UpgradeTownWorld(fixture);

                Assert.That(ground.cellBounds.size, Is.EqualTo(new Vector3Int(32, 22, 1)));
                Assert.That(ground.HasTile(new Vector3Int(-16, 15, 0)), Is.True);
                Assert.That(world.transform.Find("NPC Homes").childCount, Is.EqualTo(4));
                Assert.That(world.GetComponent<TownMap2D>().Homes.Count, Is.EqualTo(4));
                Assert.That(world.transform.Find("Obstacles").GetComponentsInChildren<Collider2D>(true).Length,
                    Is.EqualTo(10));
                var points = world.GetComponentsInChildren<TownInteractionPoint2D>(true);
                Assert.That(points.Length, Is.EqualTo(10));
                AssertLandmark(points, TownInteractionKind.Shop, new Vector2(-7f, 1f));
                AssertLandmark(points, TownInteractionKind.Coop, new Vector2(0f, 1f));
                AssertLandmark(points, TownInteractionKind.Kitchen, new Vector2(6.5f, 1f));
                AssertLandmark(points, TownInteractionKind.Bed, new Vector2(-7f, -4f));
                AssertLandmark(points, TownInteractionKind.Pond, new Vector2(0f, -4f));
                AssertLandmark(points, TownInteractionKind.Farm, new Vector2(6f, -4f));
            }
            finally
            {
                if (fixture.IsValid() && fixture.isLoaded)
                {
                    EditorSceneManager.CloseScene(fixture, true);
                }
                AssetDatabase.DeleteAsset(fixturePath);
                if (previousScene.IsValid() && previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene);
                }
            }
        }

        private static void AssertLandmark(TownInteractionPoint2D[] points, TownInteractionKind kind, Vector2 position)
        {
            var matches = points.Where(point => point.Kind == kind).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), kind.ToString());
            Assert.That((Vector2)matches[0].transform.position, Is.EqualTo(position), kind.ToString());
        }

        [TestCase("North Boundary", 0f, 1f)]
        [TestCase("South Boundary", 0f, -1f)]
        [TestCase("West Boundary", -1f, 0f)]
        [TestCase("East Boundary", 1f, 0f)]
        public void DevelopmentScene_WorldBoundaryKeepsTheWholeCharacterFrameInsideVisibleGround(
            string boundaryName, float directionX, float directionY)
        {
            WithDevelopmentScene(scene =>
            {
                var world = RequireRoot(scene, "World");
                var ground = world.GetComponentInChildren<Tilemap>(true);
                var boundary = world.transform.Find("Boundaries/" + boundaryName).GetComponent<BoxCollider2D>();
                var player = RequireRoot(scene, "Player");
                var foot = player.GetComponent<CircleCollider2D>();
                var visual = player.GetComponentInChildren<SpriteRenderer>(true);
                Physics2D.SyncTransforms();
                var radius = foot.radius * Mathf.Abs(foot.transform.lossyScale.x);
                var footOffset = Vector2.Scale(foot.offset, foot.transform.lossyScale);
                var standingPosition = Vector2.zero;
                if (directionX < 0f) standingPosition.x = boundary.bounds.max.x - footOffset.x + radius;
                if (directionX > 0f) standingPosition.x = boundary.bounds.min.x - footOffset.x - radius;
                if (directionY < 0f) standingPosition.y = boundary.bounds.max.y - footOffset.y + radius;
                if (directionY > 0f) standingPosition.y = boundary.bounds.min.y - footOffset.y - radius;
                var frameMinimum = standingPosition + (Vector2)(visual.bounds.min - player.transform.position);
                var frameMaximum = standingPosition + (Vector2)(visual.bounds.max - player.transform.position);
                var groundMinimum = ground.transform.TransformPoint(ground.localBounds.min);
                var groundMaximum = ground.transform.TransformPoint(ground.localBounds.max);
                const float onePixel = 1f / 16f;
                Assert.That(frameMinimum.x, Is.GreaterThanOrEqualTo(groundMinimum.x + onePixel), boundaryName);
                Assert.That(frameMinimum.y, Is.GreaterThanOrEqualTo(groundMinimum.y + onePixel), boundaryName);
                Assert.That(frameMaximum.x, Is.LessThanOrEqualTo(groundMaximum.x - onePixel), boundaryName);
                Assert.That(frameMaximum.y, Is.LessThanOrEqualTo(groundMaximum.y - onePixel), boundaryName);
            });
        }

        private readonly struct HomeRouteExpectation
        {
            public HomeRouteExpectation(string npcId, string homeId, string workId, string restId, Vector2 workPosition)
            {
                NpcId = npcId;
                HomeId = homeId;
                WorkId = workId;
                RestId = restId;
                WorkPosition = workPosition;
            }

            public string NpcId { get; }
            public string HomeId { get; }
            public string WorkId { get; }
            public string RestId { get; }
            public Vector2 WorkPosition { get; }
        }

        private static int LowestOpaquePixelY(Sprite sprite)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture,
                    File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite))), Is.True);
                var rect = sprite.rect;
                for (var y = 0; y < Mathf.RoundToInt(rect.height); y++)
                {
                    for (var x = 0; x < Mathf.RoundToInt(rect.width); x++)
                    {
                        if (texture.GetPixel(Mathf.RoundToInt(rect.x) + x, Mathf.RoundToInt(rect.y) + y).a > 0f)
                        {
                            return y;
                        }
                    }
                }

                Assert.Fail($"Roof '{sprite.name}' has no opaque pixels.");
                return 0;
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
    }
}
