#if UNITY_EDITOR
using System;
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
            "Assets/CozyTown/Art/Production/Characters/npc_townsfolk_idle_down_16x24.png";
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
                Assert.That(solids, Has.Length.EqualTo(6));
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

                    Sprite portrait = iconCatalog.GetNpcSprite(expectation.NpcId);
                    Assert.That(portrait, Is.Not.Null, expectation.NpcId);
                    Assert.That(portrait.name, Is.EqualTo(expectation.PortraitSpriteName));
                    Assert.That(AssetDatabase.GetAssetPath(portrait), Is.EqualTo(PortraitPath));
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

        private static GameObject RequireRoot(Scene scene, string name)
        {
            return Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == name)
                ?? throw new InvalidOperationException($"Root object '{name}' was not found.");
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
