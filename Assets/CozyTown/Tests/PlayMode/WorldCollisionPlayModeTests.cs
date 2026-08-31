#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using CozyTown.Unity.Pond;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class WorldCollisionPlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        private Scene _loadedScene;

        [UnityTest]
        public IEnumerator Player_IsBlockedByEveryBuildingButCanEnterEachDoorSlot()
        {
            yield return LoadScene();

            var world = RequireRoot(_loadedScene, "World");
            var player = RequireRoot(_loadedScene, "Player");
            var body = player.GetComponent<Rigidbody2D>();
            var movement = player.GetComponent<PlayerMovement2D>();
            var input = player.AddComponent<CollisionTestInputSource>();
            movement.SetInputSource(input);

            var expectations = new[]
            {
                new BuildingExpectation(TownInteractionKind.Shop, 0f, 1.25f),
                new BuildingExpectation(TownInteractionKind.Coop, 0f, 1.3f),
                new BuildingExpectation(TownInteractionKind.Kitchen, .75f, 1.25f),
                new BuildingExpectation(TownInteractionKind.Bed, .35f, 1.25f)
            };

            foreach (var expectation in expectations)
            {
                var point = FindUniquePoint(world, expectation.Kind);
                yield return MoveUp(
                    player,
                    body,
                    input,
                    new Vector2(point.transform.position.x - 1.45f, point.transform.position.y - .6f),
                    18);
                Assert.That(
                    body.position.y,
                    Is.LessThan(point.transform.position.y - .1f),
                    $"Player crossed the {expectation.Kind} side wall.");

                yield return MoveUp(
                    player,
                    body,
                    input,
                    new Vector2(
                        point.transform.position.x + expectation.DoorCenterX,
                        point.transform.position.y - .6f),
                    24);
                Assert.That(
                    body.position.y,
                    Is.GreaterThan(point.transform.position.y + .25f),
                    $"Player could not enter the {expectation.Kind} door slot.");
                Assert.That(
                    body.position.y,
                    Is.LessThan(point.transform.position.y + expectation.DoorDepth),
                    $"Player crossed the back of the {expectation.Kind} door slot.");
            }
        }

        [UnityTest]
        public IEnumerator Player_IsBlockedByFarmAndPondFootprints()
        {
            yield return LoadScene();

            var player = RequireRoot(_loadedScene, "Player");
            var body = player.GetComponent<Rigidbody2D>();
            var movement = player.GetComponent<PlayerMovement2D>();
            var input = player.AddComponent<CollisionTestInputSource>();
            movement.SetInputSource(input);

            yield return MoveUp(player, body, input, new Vector2(6f, -4.6f), 18);
            Assert.That(body.position.y, Is.LessThan(-4f),
                "Player crossed the farm footprint from below.");

            yield return MoveUp(player, body, input, new Vector2(0f, -4.6f), 18);
            Assert.That(body.position.y, Is.LessThan(-4f),
                "Player crossed the pond bank from below.");
        }

        [UnityTest]
        public IEnumerator Pond_RemainsTheSelectedInteractionFromAllFourBanks()
        {
            yield return LoadScene();

            var world = RequireRoot(_loadedScene, "World");
            var hud = RequireRoot(_loadedScene, "Debug HUD");
            var player = RequireRoot(_loadedScene, "Player");
            var body = player.GetComponent<Rigidbody2D>();
            var probe = player.GetComponent<InteractionProbe2D>();
            var interactor = player.GetComponent<PlayerInteractor2D>();
            var input = player.AddComponent<CollisionTestInputSource>();
            interactor.Configure(input, probe);
            var pondPoint = FindUniquePoint(world, TownInteractionKind.Pond);
            var pondView = hud.GetComponent<CozyTownPondDebugView>();
            Assert.That(pondView, Is.Not.Null);

            var bankPositions = new[]
            {
                new Vector2(0f, -4.35f),
                new Vector2(0f, .3f),
                new Vector2(-2.9f, -2f),
                new Vector2(2.85f, -2f)
            };
            foreach (var position in bankPositions)
            {
                body.linearVelocity = Vector2.zero;
                body.position = position;
                player.transform.position = position;
                Physics2D.SyncTransforms();
                yield return null;

                Assert.That(interactor.CurrentPrompt, Is.EqualTo(pondPoint.PromptText),
                    $"Pond was not the selected interaction at bank position {position}.");
                var previousCount = pondPoint.InteractionCount;
                input.InteractPressedThisFrame = true;
                yield return null;
                input.InteractPressedThisFrame = false;
                Assert.That(pondPoint.InteractionCount, Is.EqualTo(previousCount + 1));
                Assert.That(pondView.IsVisible, Is.True);
                pondView.RequestClose();
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator UnloadDevelopmentScene()
        {
            if (!_loadedScene.IsValid() || !_loadedScene.isLoaded)
            {
                yield break;
            }

            var unloadOperation = SceneManager.UnloadSceneAsync(_loadedScene);
            if (unloadOperation != null)
            {
                yield return unloadOperation;
            }
        }

        private IEnumerator LoadScene()
        {
            var loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return loadOperation;
            yield return null;
            _loadedScene = SceneManager.GetSceneByPath(ScenePath);
            Assert.That(_loadedScene.IsValid(), Is.True);
            Assert.That(_loadedScene.isLoaded, Is.True);
        }

        private static IEnumerator MoveUp(
            GameObject player,
            Rigidbody2D body,
            CollisionTestInputSource input,
            Vector2 start,
            int fixedFrames)
        {
            input.Movement = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            body.position = start;
            player.transform.position = start;
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();

            input.Movement = Vector2.up;
            for (var frame = 0; frame < fixedFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            input.Movement = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            yield return null;
        }

        private static TownInteractionPoint2D FindUniquePoint(
            GameObject world,
            TownInteractionKind kind)
        {
            return world.GetComponentsInChildren<TownInteractionPoint2D>(true)
                .Single(point => point.Kind == kind);
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            return Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == name)
                ?? throw new InvalidOperationException($"Root object '{name}' was not found.");
        }

        private readonly struct BuildingExpectation
        {
            public BuildingExpectation(
                TownInteractionKind kind,
                float doorCenterX,
                float doorDepth)
            {
                Kind = kind;
                DoorCenterX = doorCenterX;
                DoorDepth = doorDepth;
            }

            public TownInteractionKind Kind { get; }
            public float DoorCenterX { get; }
            public float DoorDepth { get; }
        }

        private sealed class CollisionTestInputSource : MonoBehaviour, IPlayerInputSource
        {
            public Vector2 Movement { get; set; }
            public bool InteractPressedThisFrame { get; set; }
        }
    }
}
#endif
