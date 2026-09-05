#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Npc;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class DevelopmentSceneCharacterWorldEditModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private const string WorldNpcPath =
            "Assets/CozyTown/Art/Production/Characters/npc_townsfolk_idle_down_24x32.png";
        private const string PlayerPath =
            "Assets/CozyTown/Art/Production/Characters/chr_player_move_24x32.png";
        private const string PortraitPath =
            "Assets/CozyTown/Art/Production/Characters/npc_portraits_48.png";

        [Test]
        public void DevelopmentScene_HenStandsOnGrassNearFarmAndOutsideSolidObstacles()
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                GameObject world = RequireRoot(scene, "World");
                CozyTownCoopWorldView[] coopViews =
                    world.GetComponentsInChildren<CozyTownCoopWorldView>(true);
                Assert.That(coopViews, Has.Length.EqualTo(1));

                var henRenderer = coopViews[0].GetComponent<SpriteRenderer>();
                Assert.That(henRenderer, Is.Not.Null);
                Vector2 henPosition = henRenderer.transform.position;

                var obstacles = world.transform.Find("Obstacles");
                Assert.That(obstacles, Is.Not.Null);
                Collider2D[] solids = obstacles
                    .GetComponentsInChildren<Collider2D>(true)
                    .Where(collider => !collider.isTrigger)
                    .ToArray();
                Assert.That(solids, Has.Length.EqualTo(10));
                Physics2D.SyncTransforms();
                foreach (Collider2D solid in solids)
                {
                    Assert.That(
                        solid.OverlapPoint(henPosition),
                        Is.False,
                        $"Hen overlaps solid obstacle '{solid.name}' at {henPosition}.");
                }

                var tilemap = world.GetComponentInChildren<Tilemap>(true);
                Assert.That(tilemap, Is.Not.Null);
                TileBase ground = tilemap.GetTile(tilemap.WorldToCell(henPosition));
                Assert.That(ground, Is.Not.Null);
                Assert.That(
                    ground.name,
                    Does.StartWith("tile_grass_"),
                    $"Hen must stand on grass, but found '{ground.name}' at {henPosition}.");

                Collider2D farm = solids.Single(collider => collider.name == "Farm Obstacle");
                float distanceToFarm = Vector2.Distance(
                    henPosition,
                    farm.ClosestPoint(henPosition));
                Assert.That(
                    distanceToFarm,
                    Is.LessThanOrEqualTo(1f),
                    $"Hen must remain near the farm boundary, but was {distanceToFarm:0.###} units away.");
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

        [Test]
        public void DevelopmentScene_EachNpcUsesItsApprovedWorldAndPortraitIdentity()
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                GameObject world = RequireRoot(scene, "World");
                var iconCatalog = RequireRoot(scene, "Debug HUD")
                    .GetComponentInChildren<CozyTownUiIconCatalog>(true);
                Assert.That(iconCatalog, Is.Not.Null);

                var expectations = new[]
                {
                    new NpcAppearanceExpectation(
                        DefaultMvpIds.Npcs.Shopkeeper,
                        "npc_shopkeeper_mina_idle_down",
                        "npc_shopkeeper_mina_portrait"),
                    new NpcAppearanceExpectation(
                        DefaultMvpIds.Npcs.Farmer,
                        "npc_farmer_eli_idle_down",
                        "npc_farmer_eli_portrait"),
                    new NpcAppearanceExpectation(
                        DefaultMvpIds.Npcs.Fisher,
                        "npc_fisher_ren_idle_down",
                        "npc_fisher_ren_portrait"),
                    new NpcAppearanceExpectation(
                        DefaultMvpIds.Npcs.Cook,
                        "npc_cook_sora_idle_down",
                        "npc_cook_sora_portrait")
                };

                TownInteractionPoint2D[] npcPoints = world
                    .GetComponentsInChildren<TownInteractionPoint2D>(true)
                    .Where(point => point.Kind == TownInteractionKind.Npc)
                    .ToArray();
                Assert.That(npcPoints, Has.Length.EqualTo(expectations.Length));

                foreach (NpcAppearanceExpectation expectation in expectations)
                {
                    TownInteractionPoint2D point = npcPoints.Single(candidate =>
                        candidate.GetComponent<CozyTownNpcDebugPresenter>()?.NpcId
                        == expectation.NpcId);
                    Sprite worldSprite = point.transform.Find("Visual")
                        ?.GetComponent<SpriteRenderer>()
                        ?.sprite;
                    Assert.That(worldSprite, Is.Not.Null, expectation.NpcId);
                    Assert.That(worldSprite.name, Is.EqualTo(expectation.WorldSpriteName));
                    Assert.That(AssetDatabase.GetAssetPath(worldSprite), Is.EqualTo(WorldNpcPath));
                    Assert.That(worldSprite.rect.width, Is.EqualTo(24f));
                    Assert.That(worldSprite.rect.height, Is.EqualTo(32f));

                    Sprite portrait = iconCatalog.GetNpcSprite(expectation.NpcId);
                    Assert.That(portrait, Is.Not.Null, expectation.NpcId);
                    Assert.That(portrait.name, Is.EqualTo(expectation.PortraitSpriteName));
                    Assert.That(AssetDatabase.GetAssetPath(portrait), Is.EqualTo(PortraitPath));
                    Assert.That(
                        CalculatePortraitPaletteCoverage(worldSprite, portrait),
                        Is.GreaterThanOrEqualTo(0.7f),
                        $"{expectation.NpcId} world Sprite does not preserve enough portrait identity colors.");
                    AssertFullBodyOccupancy(worldSprite, expectation.NpcId);
                }

                Assert.That(
                    npcPoints
                        .Select(point => point.transform.Find("Visual")
                            ?.GetComponent<SpriteRenderer>()
                            ?.sprite)
                        .Distinct()
                        .Count(),
                    Is.EqualTo(expectations.Length),
                    "Each NPC must use a distinct world Sprite instead of one shared placeholder.");
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

        [Test]
        public void DevelopmentScene_PlayerUsesReadableTwentyFourByThirtyTwoMovementSprites()
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                SpriteRenderer renderer = RequireRoot(scene, "Player")
                    .GetComponentInChildren<SpriteRenderer>(true);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.sprite, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(renderer.sprite), Is.EqualTo(PlayerPath));
                Assert.That(renderer.sprite.rect.width, Is.EqualTo(24f));
                Assert.That(renderer.sprite.rect.height, Is.EqualTo(32f));

                Sprite[] movementSprites = AssetDatabase.LoadAllAssetsAtPath(PlayerPath)
                    .OfType<Sprite>()
                    .ToArray();
                Assert.That(movementSprites, Has.Length.EqualTo(12));
                Assert.That(movementSprites.All(sprite => sprite.rect.width == 24f), Is.True);
                Assert.That(movementSprites.All(sprite => sprite.rect.height == 32f), Is.True);
                Assert.That(movementSprites.Select(sprite => sprite.name), Is.EquivalentTo(new[]
                {
                    "chr_player_idle_down", "chr_player_walk_down_00", "chr_player_walk_down_01",
                    "chr_player_idle_left", "chr_player_walk_left_00", "chr_player_walk_left_01",
                    "chr_player_idle_right", "chr_player_walk_right_00", "chr_player_walk_right_01",
                    "chr_player_idle_up", "chr_player_walk_up_00", "chr_player_walk_up_01"
                }));
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
            return Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == name)
                ?? throw new InvalidOperationException($"Root object '{name}' was not found.");
        }

        private static float CalculatePortraitPaletteCoverage(
            Sprite worldSprite,
            Sprite portraitSprite)
        {
            Color32[] world = LoadSpritePixels(worldSprite, out _, out _);
            Color32[] portrait = LoadSpritePixels(portraitSprite, out _, out _);
            var worldColors = CollectOpaqueColors(world);
            var portraitColors = CollectOpaqueColors(portrait);
            int sharedColorCount = portraitColors.Count(worldColors.Contains);
            return portraitColors.Count == 0
                ? 0f
                : (float)sharedColorCount / portraitColors.Count;
        }

        private static HashSet<int> CollectOpaqueColors(IEnumerable<Color32> pixels)
        {
            var colors = new HashSet<int>();
            foreach (Color32 pixel in pixels)
            {
                if (pixel.a > 0)
                {
                    colors.Add((pixel.r << 16) | (pixel.g << 8) | pixel.b);
                }
            }

            return colors;
        }

        private static void AssertFullBodyOccupancy(Sprite sprite, string npcId)
        {
            Color32[] pixels = LoadSpritePixels(sprite, out int width, out int height);
            int minimumOpaqueY = height;
            int maximumOpaqueY = -1;
            var opaquePixelCount = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a == 0)
                    {
                        continue;
                    }

                    opaquePixelCount++;
                    minimumOpaqueY = Math.Min(minimumOpaqueY, y);
                    maximumOpaqueY = Math.Max(maximumOpaqueY, y);
                }
            }

            Assert.That(minimumOpaqueY, Is.LessThanOrEqualTo(1), npcId);
            Assert.That(maximumOpaqueY, Is.GreaterThanOrEqualTo(height - 2), npcId);
            Assert.That(opaquePixelCount, Is.GreaterThanOrEqualTo(320), npcId);
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
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(assetPath), false))
                {
                    throw new InvalidDataException($"Could not decode sprite texture '{assetPath}'.");
                }

                Rect rect = sprite.rect;
                width = Mathf.RoundToInt(rect.width);
                height = Mathf.RoundToInt(rect.height);
                int startX = Mathf.RoundToInt(rect.x);
                int startY = Mathf.RoundToInt(rect.y);
                Color32[] atlas = texture.GetPixels32();
                var result = new Color32[width * height];
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        result[y * width + x] =
                            atlas[(startY + y) * texture.width + startX + x];
                    }
                }

                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private readonly struct NpcAppearanceExpectation
        {
            public NpcAppearanceExpectation(
                string npcId,
                string worldSpriteName,
                string portraitSpriteName)
            {
                NpcId = npcId;
                WorldSpriteName = worldSpriteName;
                PortraitSpriteName = portraitSpriteName;
            }

            public string NpcId { get; }
            public string WorldSpriteName { get; }
            public string PortraitSpriteName { get; }
        }
    }
}
#endif
