using System;
using System.IO;
using CozyTown.Unity.Core;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
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
                var shopPoint = CreateWorld();
                CreateHud(bootstrap, playerInteractor, shopPoint);
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
            return interactor;
        }

        private static TownInteractionPoint2D CreateWorld()
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
            CreateInteractionPoint(
                "NPC",
                TownInteractionKind.Npc,
                "Press E to talk",
                new Vector2(3f, 1.6f),
                new Color(0.25f, 0.65f, 1f),
                interactionPoints.transform);
            CreateInteractionPoint(
                "Bed",
                TownInteractionKind.Bed,
                "Press E to sleep until tomorrow",
                new Vector2(-3f, -1.6f),
                new Color(0.7f, 0.4f, 1f),
                interactionPoints.transform);
            CreateInteractionPoint(
                "Farm",
                TownInteractionKind.Farm,
                "Press E to inspect the farm",
                new Vector2(3f, -1.6f),
                new Color(0.35f, 0.85f, 0.35f),
                interactionPoints.transform);
            return shopPoint;
        }

        private static void CreateHud(
            CozyTownBootstrap bootstrap,
            PlayerInteractor2D playerInteractor,
            TownInteractionPoint2D shopPoint)
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
            shopPresenter.Configure(shopPoint, shopView);
            bootstrap.RegisterShopPresenter(shopPresenter);
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
    }
}
