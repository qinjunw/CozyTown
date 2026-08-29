using System;
using System.IO;
using CozyTown.Runtime.Content;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Core;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Player;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Save;
using CozyTown.Unity.Shop;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownDevSceneMenu
    {
        private const string SceneFolder = "Assets/CozyTown/Scenes";
        private const string ScenePath = SceneFolder + "/CozyTown_Dev.unity";
        private const string InputActionsPath = "Assets/Settings/InputSystem_Actions.inputactions";

        [MenuItem("CozyTown/Create Development Scene")]
        public static void CreateDevelopmentScene()
        {
            var existingScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (existingScene != null || File.Exists(ScenePath))
            {
                EditorGUIUtility.PingObject(existingScene);
                Debug.Log($"Development scene already exists at {ScenePath}. No files were changed.");
                return;
            }

            EnsureFolder(SceneFolder);

            var previousScene = SceneManager.GetActiveScene();
            try
            {
                var scene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    GetSceneCreationMode(previousScene));
                SceneManager.SetActiveScene(scene);

                var bootstrap = CreateBootstrap();
                var playerInteractor = CreatePlayer();
                var points = CreateWorld();
                CreateHud(bootstrap, playerInteractor, points);
                CreateCamera();

                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
                Debug.Log(
                    $"Created development scene at {ScenePath} without modifying open scenes.");
            }
            finally
            {
                if (previousScene.IsValid())
                {
                    SceneManager.SetActiveScene(previousScene);
                }
            }
        }

        [MenuItem("CozyTown/Upgrade Development Scene for M4")]
        public static void UpgradeDevelopmentSceneForM4()
        {
            if (!File.Exists(ScenePath))
            {
                throw new FileNotFoundException("Development scene was not found.", ScenePath);
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool closeWhenFinished = !scene.IsValid() || !scene.isLoaded;
            if (closeWhenFinished)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                var bootstrapObject = RequireRoot(scene, "CozyTown");
                var bootstrap = bootstrapObject.GetComponent<CozyTownBootstrap>();
                if (bootstrap == null)
                {
                    throw new InvalidOperationException("Development scene is missing CozyTownBootstrap.");
                }

                var hud = RequireRoot(scene, "Debug HUD");
                var npcPoint = Array.Find(
                    RequireRoot(scene, "World")
                        .GetComponentsInChildren<TownInteractionPoint2D>(true),
                    point => point.Kind == TownInteractionKind.Npc);
                if (npcPoint == null)
                {
                    throw new InvalidOperationException("Development scene is missing the NPC point.");
                }

                var npcView = GetOrAdd<CozyTownNpcDebugView>(hud);
                var npcPresenter = GetOrAdd<CozyTownNpcDebugPresenter>(hud);
                npcPresenter.Configure(npcPoint, npcView, DefaultMvpIds.Npcs.Shopkeeper);
                bootstrap.RegisterNpcPresenter(npcPresenter);

                var saveView = GetOrAdd<CozyTownSaveDebugView>(hud);
                var savePresenter = GetOrAdd<CozyTownSaveDebugPresenter>(hud);
                savePresenter.Configure(saveView);
                bootstrap.RegisterSavePresenter(savePresenter);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Upgraded development scene for M4 at {ScenePath}.");
            }
            finally
            {
                if (closeWhenFinished && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static NewSceneMode GetSceneCreationMode(Scene previousScene)
        {
            if (!string.IsNullOrEmpty(previousScene.path))
            {
                return NewSceneMode.Additive;
            }

            if (Application.isBatchMode)
            {
                return NewSceneMode.Single;
            }

            throw new InvalidOperationException(
                "Save the active untitled scene before creating the development scene.");
        }

        private static CozyTownBootstrap CreateBootstrap()
        {
            var gameRoot = new GameObject("CozyTown");
            return gameRoot.AddComponent<CozyTownBootstrap>();
        }

        private static PlayerInteractor2D CreatePlayer()
        {
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions == null)
            {
                throw new FileNotFoundException("Default InputActionAsset was not found.", InputActionsPath);
            }

            var playerObject = new GameObject("Player");
            playerObject.transform.position = Vector3.zero;
            ConfigureRenderer(
                playerObject,
                new Vector2(0.7f, 0.7f),
                new Color(0.25f, 0.9f, 1f),
                sortingOrder: 10);

            var body = playerObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            playerObject.AddComponent<CircleCollider2D>().radius = 0.3f;

            var playerInput = playerObject.AddComponent<PlayerInput>();
            playerInput.actions = inputActions;
            playerInput.defaultActionMap = "Player";
            playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

            var inputSource = playerObject.AddComponent<InputSystemPlayerInputSource>();
            var movement = playerObject.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(inputSource);

            var probe = playerObject.AddComponent<InteractionProbe2D>();
            var interactor = playerObject.AddComponent<PlayerInteractor2D>();
            interactor.Configure(inputSource, probe);
            playerObject.AddComponent<PlayerModalInputGate2D>();
            return interactor;
        }

        private static TownInteractionPoint2D[] CreateWorld()
        {
            var world = new GameObject("World");
            var boundaries = new GameObject("Boundaries");
            boundaries.transform.SetParent(world.transform, false);
            CreateBoundary(
                "North Boundary",
                new Vector2(0f, 3f),
                new Vector2(10.4f, 0.4f),
                boundaries.transform);
            CreateBoundary(
                "South Boundary",
                new Vector2(0f, -3f),
                new Vector2(10.4f, 0.4f),
                boundaries.transform);
            CreateBoundary(
                "West Boundary",
                new Vector2(-5f, 0f),
                new Vector2(0.4f, 6.4f),
                boundaries.transform);
            CreateBoundary(
                "East Boundary",
                new Vector2(5f, 0f),
                new Vector2(0.4f, 6.4f),
                boundaries.transform);

            var interactionPoints = new GameObject("Interaction Points");
            interactionPoints.transform.SetParent(world.transform, false);
            var shopPoint = CreateInteractionPoint(
                "Shop",
                TownInteractionKind.Shop,
                "Press E to browse the shop",
                new Vector2(-3f, 1.6f),
                new Color(1f, 0.65f, 0.2f),
                interactionPoints.transform);
            var npcPoint = CreateInteractionPoint(
                "NPC",
                TownInteractionKind.Npc,
                "Press E to talk",
                new Vector2(3f, 1.6f),
                new Color(0.25f, 0.65f, 1f),
                interactionPoints.transform);
            var bedPoint = CreateInteractionPoint(
                "Bed",
                TownInteractionKind.Bed,
                "Press E to sleep until tomorrow",
                new Vector2(-3f, -1.6f),
                new Color(0.7f, 0.4f, 1f),
                interactionPoints.transform);
            var farmPoint = CreateInteractionPoint(
                "Farm",
                TownInteractionKind.Farm,
                "Press E to inspect the farm",
                new Vector2(3f, -1.6f),
                new Color(0.35f, 0.85f, 0.35f),
                interactionPoints.transform);
            var coopPoint = CreateInteractionPoint(
                "Coop", TownInteractionKind.Coop, "Press E to tend the coop",
                new Vector2(0f, 1.6f), new Color(.9f, .8f, .35f), interactionPoints.transform);
            var pondPoint = CreateInteractionPoint(
                "Pond", TownInteractionKind.Pond, "Press E to fish",
                new Vector2(0f, -1.6f), new Color(.2f, .75f, .9f), interactionPoints.transform);
            var kitchenPoint = CreateInteractionPoint(
                "Kitchen", TownInteractionKind.Kitchen, "Press E to cook",
                new Vector2(3f, 0f), new Color(.95f, .45f, .35f), interactionPoints.transform);
            return new[]
            {
                shopPoint, npcPoint, bedPoint, farmPoint, coopPoint, pondPoint, kitchenPoint
            };
        }

        private static void CreateHud(
            CozyTownBootstrap bootstrap,
            PlayerInteractor2D playerInteractor,
            TownInteractionPoint2D[] points)
        {
            var hudObject = new GameObject("Debug HUD");
            var view = hudObject.AddComponent<CozyTownDebugHudView>();
            var presenter = hudObject.AddComponent<CozyTownHudPresenter>();
            presenter.ConfigureView(view);
            bootstrap.RegisterHudPresenter(presenter);

            var interactionView = hudObject.AddComponent<CozyTownInteractionDebugView>();
            interactionView.Configure(playerInteractor);

            var shopView = hudObject.AddComponent<CozyTownShopDebugView>();
            var shopPresenter = hudObject.AddComponent<CozyTownShopDebugPresenter>();
            shopPresenter.Configure(points[0], shopView);
            bootstrap.RegisterShopPresenter(shopPresenter);

            var farmView = hudObject.AddComponent<CozyTownFarmDebugView>();
            var farmPresenter = hudObject.AddComponent<CozyTownFarmDebugPresenter>();
            farmPresenter.Configure(points[3], farmView);
            bootstrap.RegisterFarmPresenter(farmPresenter);
            var bedView = hudObject.AddComponent<CozyTownBedDebugView>();
            var bedPresenter = hudObject.AddComponent<CozyTownBedDebugPresenter>();
            bedPresenter.Configure(points[2], bedView);
            bootstrap.RegisterBedPresenter(bedPresenter);
            var coopView = hudObject.AddComponent<CozyTownCoopDebugView>();
            var coopPresenter = hudObject.AddComponent<CozyTownCoopDebugPresenter>();
            coopPresenter.Configure(points[4], coopView);
            bootstrap.RegisterCoopPresenter(coopPresenter);
            var pondView = hudObject.AddComponent<CozyTownPondDebugView>();
            var pondPresenter = hudObject.AddComponent<CozyTownPondDebugPresenter>();
            pondPresenter.Configure(points[5], pondView);
            bootstrap.RegisterPondPresenter(pondPresenter);
            var kitchenView = hudObject.AddComponent<CozyTownKitchenDebugView>();
            var kitchenPresenter = hudObject.AddComponent<CozyTownKitchenDebugPresenter>();
            kitchenPresenter.Configure(points[6], kitchenView);
            bootstrap.RegisterKitchenPresenter(kitchenPresenter);
            var npcView = hudObject.AddComponent<CozyTownNpcDebugView>();
            var npcPresenter = hudObject.AddComponent<CozyTownNpcDebugPresenter>();
            npcPresenter.Configure(points[1], npcView, DefaultMvpIds.Npcs.Shopkeeper);
            bootstrap.RegisterNpcPresenter(npcPresenter);
            var saveView = hudObject.AddComponent<CozyTownSaveDebugView>();
            var savePresenter = hudObject.AddComponent<CozyTownSaveDebugPresenter>();
            savePresenter.Configure(saveView);
            bootstrap.RegisterSavePresenter(savePresenter);
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3.7f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.11f, 0.12f);
        }

        private static void CreateBoundary(
            string name,
            Vector2 position,
            Vector2 size,
            Transform parent)
        {
            var boundary = new GameObject(name);
            boundary.transform.SetParent(parent, false);
            boundary.transform.position = position;
            boundary.AddComponent<BoxCollider2D>().size = size;
            ConfigureRenderer(
                boundary,
                size,
                new Color(0.18f, 0.23f, 0.25f),
                sortingOrder: 0);
        }

        private static TownInteractionPoint2D CreateInteractionPoint(
            string name,
            TownInteractionKind kind,
            string prompt,
            Vector2 position,
            Color color,
            Transform parent)
        {
            var pointObject = new GameObject(name);
            pointObject.transform.SetParent(parent, false);
            pointObject.transform.position = position;

            var collider = pointObject.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(1.2f, 1.2f);
            collider.isTrigger = true;

            ConfigureRenderer(
                pointObject,
                collider.size,
                color,
                sortingOrder: 1);
            var interactionPoint = pointObject.AddComponent<TownInteractionPoint2D>();
            interactionPoint.Configure(kind, prompt);
            return interactionPoint;
        }

        private static void ConfigureRenderer(
            GameObject target,
            Vector2 size,
            Color color,
            int sortingOrder)
        {
            var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (sprite == null)
            {
                throw new InvalidOperationException("Unity built-in UISprite could not be loaded.");
            }

            var visual = new GameObject("Visual");
            visual.transform.SetParent(target.transform, false);

            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;

            var spriteSize = sprite.bounds.size;
            visual.transform.localScale = new Vector3(
                size.x / spriteSize.x,
                size.y / spriteSize.y,
                1f);
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            var root = Array.Find(
                scene.GetRootGameObjects(),
                candidate => candidate.name == name);
            return root != null
                ? root
                : throw new InvalidOperationException($"Root object '{name}' was not found.");
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
