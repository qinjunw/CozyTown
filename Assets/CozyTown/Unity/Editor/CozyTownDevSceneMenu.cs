using System.IO;
using CozyTown.Unity.Core;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
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
            if (File.Exists(ScenePath))
            {
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
                Debug.Log($"Development scene already exists at {ScenePath}. No files were changed.");
                return;
            }

            EnsureFolder(SceneFolder);

            var previousScene = SceneManager.GetActiveScene();
            try
            {
                var scene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);

                var bootstrap = CreateBootstrap();
                CreatePlayer();
                CreateHud(bootstrap);
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

        private static CozyTownBootstrap CreateBootstrap()
        {
            var gameRoot = new GameObject("CozyTown");
            return gameRoot.AddComponent<CozyTownBootstrap>();
        }

        private static void CreatePlayer()
        {
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions == null)
            {
                throw new FileNotFoundException("Default InputActionAsset was not found.", InputActionsPath);
            }

            var playerObject = new GameObject("Player");
            playerObject.transform.position = Vector3.zero;

            var body = playerObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            playerObject.AddComponent<CircleCollider2D>().radius = 0.35f;

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
        }

        private static void CreateHud(CozyTownBootstrap bootstrap)
        {
            var hudObject = new GameObject("Debug HUD");
            var view = hudObject.AddComponent<CozyTownDebugHudView>();
            var presenter = hudObject.AddComponent<CozyTownHudPresenter>();
            presenter.ConfigureView(view);
            bootstrap.RegisterHudPresenter(presenter);
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.11f, 0.12f);
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
