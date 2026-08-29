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
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

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

                var emptyCropRenderers = 0;
                foreach (var renderer in world.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.sprite == null)
                    {
                        Assert.That(renderer.gameObject.name, Is.EqualTo("Crop"));
                        emptyCropRenderers++;
                        continue;
                    }

                    AssertProductionSprite(renderer, renderer.gameObject.name);
                }
                Assert.That(emptyCropRenderers, Is.EqualTo(6));
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

        [Test]
        public void DevelopmentScene_UsesProductionCanvasAndCompleteUiSkin()
        {
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                var hud = RequireRoot(scene, "Debug HUD");
                var canvas = hud.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(canvas.pixelPerfect, Is.True);

                var scaler = canvas.GetComponent<CanvasScaler>();
                Assert.That(scaler, Is.Not.Null);
                Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
                Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(320f, 180f)));
                Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null);

                var eventSystem = RequireRoot(scene, "EventSystem");
                Assert.That(eventSystem.GetComponent<EventSystem>(), Is.Not.Null);
                Assert.That(eventSystem.GetComponent<InputSystemUIInputModule>(), Is.Not.Null);

                var uiSpriteNames = new System.Collections.Generic.HashSet<string>();
                foreach (var image in canvas.GetComponentsInChildren<Image>(true))
                {
                    if (image.sprite != null
                        && AssetDatabase.GetAssetPath(image.sprite)
                            == ProductionRoot + "UI/ui_mvp_16.png")
                    {
                        uiSpriteNames.Add(image.sprite.name);
                    }

                    if (image.sprite != null && image.sprite.name == "ui_panel")
                    {
                        Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
                    }
                }

                foreach (var button in canvas.GetComponentsInChildren<Button>(true))
                {
                    Assert.That(button.transition, Is.EqualTo(Selectable.Transition.SpriteSwap));
                    Assert.That(button.targetGraphic, Is.TypeOf<Image>());
                    var targetImage = (Image)button.targetGraphic;
                    Assert.That(targetImage.sprite?.name, Is.EqualTo("ui_button_normal"));
                    Assert.That(targetImage.type, Is.EqualTo(Image.Type.Sliced));

                    var spriteState = button.spriteState;
                    Assert.That(spriteState.highlightedSprite?.name, Is.EqualTo("ui_button_hover"));
                    Assert.That(spriteState.pressedSprite?.name, Is.EqualTo("ui_button_pressed"));
                    Assert.That(spriteState.disabledSprite?.name, Is.EqualTo("ui_button_disabled"));
                    uiSpriteNames.Add(targetImage.sprite.name);
                    uiSpriteNames.Add(spriteState.highlightedSprite.name);
                    uiSpriteNames.Add(spriteState.pressedSprite.name);
                    uiSpriteNames.Add(spriteState.disabledSprite.name);
                }

                Assert.That(
                    uiSpriteNames,
                    Is.EquivalentTo(new[]
                    {
                        "ui_panel",
                        "ui_button_normal",
                        "ui_button_hover",
                        "ui_button_pressed",
                        "ui_button_disabled",
                        "ui_icon_coin",
                        "ui_icon_clock",
                        "ui_icon_save",
                        "ui_icon_load",
                        "ui_icon_close",
                        "ui_marker_selection",
                        "ui_marker_interact"
                    }));
                Assert.That(canvas.GetComponentsInChildren<Text>(true), Is.Not.Empty);
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
        public void DevelopmentScene_ProductionPanelsFitAllTargetResolutions()
        {
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                var hud = RequireRoot(scene, "Debug HUD");
                var canvas = hud.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);
                var panelRects = Array.FindAll(
                    canvas.GetComponentsInChildren<Image>(true),
                    image => image.transform.parent == canvas.transform
                        && image.sprite != null
                        && image.sprite.name == "ui_panel");
                Assert.That(panelRects, Has.Length.EqualTo(10));

                var reference = new Vector2(320f, 180f);
                var targetResolutions = new[]
                {
                    reference,
                    new Vector2(640f, 360f),
                    new Vector2(1280f, 720f)
                };
                foreach (var panelImage in panelRects)
                {
                    var rect = panelImage.rectTransform;
                    Assert.That(rect.anchorMin, Is.EqualTo(rect.anchorMax));
                    var pivotPoint = Vector2.Scale(rect.anchorMin, reference) + rect.anchoredPosition;
                    var referenceMin = pivotPoint - Vector2.Scale(rect.pivot, rect.sizeDelta);
                    var referenceMax = referenceMin + rect.sizeDelta;
                    foreach (var resolution in targetResolutions)
                    {
                        var scale = resolution.x / reference.x;
                        var minimum = referenceMin * scale;
                        var maximum = referenceMax * scale;
                        Assert.That(minimum.x, Is.GreaterThanOrEqualTo(0f), panelImage.name);
                        Assert.That(minimum.y, Is.GreaterThanOrEqualTo(0f), panelImage.name);
                        Assert.That(maximum.x, Is.LessThanOrEqualTo(resolution.x), panelImage.name);
                        Assert.That(maximum.y, Is.LessThanOrEqualTo(resolution.y), panelImage.name);
                    }
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
