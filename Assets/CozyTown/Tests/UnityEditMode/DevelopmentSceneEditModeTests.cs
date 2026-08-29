using System;
using CozyTown.Unity.Core;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using CozyTown.Unity.Shop;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Save;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class DevelopmentSceneEditModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private const string ProductionRoot = "Assets/CozyTown/Art/Production/";

        [Test]
        public void DevelopmentScene_UsesProductionWorldVisualsAtPixelPerfectResolution()
        {
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                var cameraObject = RequireRoot(scene, "Main Camera");
                var pixelPerfectCamera = cameraObject.GetComponent<PixelPerfectCamera>();
                Assert.That(pixelPerfectCamera, Is.Not.Null);
                Assert.That(pixelPerfectCamera.assetsPPU, Is.EqualTo(16));
                Assert.That(pixelPerfectCamera.refResolutionX, Is.EqualTo(320));
                Assert.That(pixelPerfectCamera.refResolutionY, Is.EqualTo(180));
                Assert.That(
                    pixelPerfectCamera.gridSnapping,
                    Is.EqualTo(PixelPerfectCamera.GridSnapping.UpscaleRenderTexture));

                var world = RequireRoot(scene, "World");
                Assert.That(world.GetComponentInChildren<Grid>(true), Is.Not.Null);
                var tilemap = world.GetComponentInChildren<Tilemap>(true);
                Assert.That(tilemap, Is.Not.Null);
                Assert.That(tilemap.GetUsedTilesCount(), Is.GreaterThanOrEqualTo(2));
                Assert.That(tilemap.GetComponent<TilemapRenderer>(), Is.Not.Null);

                var points = world.GetComponentsInChildren<TownInteractionPoint2D>(true);
                Assert.That(points, Has.Length.EqualTo(7));
                foreach (var point in points)
                {
                    var visual = point.transform.Find("Visual");
                    Assert.That(visual, Is.Not.Null, $"{point.name} is missing its Visual child.");
                    Assert.That(visual.localScale, Is.EqualTo(Vector3.one));
                    AssertProductionSprite(
                        visual.GetComponent<SpriteRenderer>(),
                        $"{point.name} visual");
                }

                var player = RequireRoot(scene, "Player");
                AssertProductionSprite(
                    player.GetComponentInChildren<SpriteRenderer>(true),
                    "Player visual");

                foreach (var renderer in world.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    AssertProductionSprite(renderer, renderer.gameObject.name);
                }
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
        public void DevelopmentScene_ContainsWalkingAndShopTradingSlice()
        {
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                var bootstrap = RequireRoot(scene, "CozyTown");
                Assert.That(bootstrap.GetComponent<CozyTownBootstrap>(), Is.Not.Null);
                Assert.That(typeof(CozyTownBootstrap).GetProperty("Services"), Is.Null);

                var player = RequireRoot(scene, "Player");
                Assert.That(player.GetComponent<Rigidbody2D>(), Is.Not.Null);
                Assert.That(player.GetComponent<Collider2D>(), Is.Not.Null);
                Assert.That(player.GetComponent<InputSystemPlayerInputSource>(), Is.Not.Null);
                Assert.That(player.GetComponent<PlayerMovement2D>(), Is.Not.Null);
                Assert.That(player.GetComponent<PlayerInteractor2D>(), Is.Not.Null);
                Assert.That(player.GetComponent<PlayerModalInputGate2D>(), Is.Not.Null);
                Assert.That(player.GetComponentInChildren<SpriteRenderer>(true), Is.Not.Null);

                var world = RequireRoot(scene, "World");
                var boundaries = world.transform.Find("Boundaries");
                Assert.That(boundaries, Is.Not.Null);
                Assert.That(
                    boundaries.GetComponentsInChildren<BoxCollider2D>(true),
                    Has.Length.EqualTo(4));

                var points = world.GetComponentsInChildren<TownInteractionPoint2D>(true);
                Assert.That(points, Has.Length.EqualTo(7));
                foreach (var point in points)
                {
                    Assert.That(point.PromptText, Is.Not.Empty);
                    Assert.That(point.GetComponent<SpriteRenderer>(), Is.Null);
                    var visual = point.transform.Find("Visual");
                    Assert.That(visual, Is.Not.Null, $"{point.name} is missing its Visual child.");
                    var renderer = visual.GetComponent<SpriteRenderer>();
                    Assert.That(renderer, Is.Not.Null);
                    Assert.That(renderer.sprite, Is.Not.Null);
                }

                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        TownInteractionKind.Shop,
                        TownInteractionKind.Npc,
                        TownInteractionKind.Bed,
                        TownInteractionKind.Farm,
                        TownInteractionKind.Coop,
                        TownInteractionKind.Pond,
                        TownInteractionKind.Kitchen
                    },
                    Array.ConvertAll(points, point => point.Kind));

                var hud = RequireRoot(scene, "Debug HUD");
                Assert.That(hud.GetComponent<CozyTownHudPresenter>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownInteractionDebugView>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownShopDebugView>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownShopDebugPresenter>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownFarmDebugView>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownFarmDebugPresenter>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownBedDebugPresenter>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownCoopDebugPresenter>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownPondDebugPresenter>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownKitchenDebugPresenter>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownNpcDebugView>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownNpcDebugPresenter>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownSaveDebugView>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownSaveDebugPresenter>(), Is.Not.Null);

                var camera = RequireRoot(scene, "Main Camera");
                Assert.That(camera.GetComponent<Camera>()?.orthographic, Is.True);
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
            var root = Array.Find(
                scene.GetRootGameObjects(),
                candidate => candidate.name == name);
            Assert.That(root, Is.Not.Null, $"Root object '{name}' was not found.");
            return root;
        }

        private static void AssertProductionSprite(SpriteRenderer renderer, string context)
        {
            Assert.That(renderer, Is.Not.Null, $"{context} is missing SpriteRenderer.");
            Assert.That(renderer.sprite, Is.Not.Null, $"{context} is missing a Sprite.");
            Assert.That(
                AssetDatabase.GetAssetPath(renderer.sprite),
                Does.StartWith(ProductionRoot),
                $"{context} still uses a non-Production Sprite.");
        }
    }
}
