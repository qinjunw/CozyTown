#if UNITY_EDITOR
using System;
using System.Collections;
using CozyTown.Unity.Core;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class DevelopmentScenePlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        private Scene _loadedScene;

        [UnityTest]
        public IEnumerator DevelopmentScene_StartsTheM2WalkingSlice()
        {
            var loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return loadOperation;
            yield return null;

            _loadedScene = SceneManager.GetSceneByPath(ScenePath);
            Assert.That(_loadedScene.IsValid(), Is.True);
            Assert.That(_loadedScene.isLoaded, Is.True);

            var bootstrap = RequireRoot(_loadedScene, "CozyTown")
                .GetComponent<CozyTownBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.IsInitialized, Is.True);

            var player = RequireRoot(_loadedScene, "Player");
            Assert.That(player.GetComponent<InputSystemPlayerInputSource>()?.enabled, Is.True);
            var body = player.GetComponent<Rigidbody2D>();
            var movement = player.GetComponent<PlayerMovement2D>();
            var probe = player.GetComponent<InteractionProbe2D>();
            var interactor = player.GetComponent<PlayerInteractor2D>();
            Assert.That(body, Is.Not.Null);
            Assert.That(movement?.enabled, Is.True);
            Assert.That(probe?.enabled, Is.True);
            Assert.That(interactor?.enabled, Is.True);

            var testInput = player.AddComponent<PlayModePlayerInputSource>();
            movement.SetInputSource(testInput);
            interactor.Configure(testInput, probe);

            var startPosition = body.position;
            testInput.Movement = Vector2.right;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            testInput.Movement = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            Assert.That(body.position.x, Is.GreaterThan(startPosition.x));

            body.position = new Vector2(4f, 0f);
            player.transform.position = body.position;
            Physics2D.SyncTransforms();
            testInput.Movement = Vector2.right;
            for (var fixedFrame = 0; fixedFrame < 20; fixedFrame++)
            {
                yield return new WaitForFixedUpdate();
            }

            testInput.Movement = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            Assert.That(body.position.x, Is.LessThan(4.8f));

            var points = RequireRoot(_loadedScene, "World")
                .GetComponentsInChildren<TownInteractionPoint2D>(true);
            Assert.That(points, Has.Length.EqualTo(4));
            foreach (var point in points)
            {
                var targetPosition = (Vector2)point.transform.position;
                body.position = targetPosition;
                player.transform.position = targetPosition;
                Physics2D.SyncTransforms();
                yield return null;

                Assert.That(interactor.CurrentPrompt, Is.EqualTo(point.PromptText));
                var previousCount = point.InteractionCount;
                testInput.PressInteract();
                yield return null;

                Assert.That(point.InteractionCount, Is.EqualTo(previousCount + 1));
                Assert.That(interactor.LastInteractionFeedback, Does.Contain(point.PromptText));
            }

            var hud = RequireRoot(_loadedScene, "Debug HUD");
            Assert.That(hud.GetComponent<CozyTownHudPresenter>()?.enabled, Is.True);
            Assert.That(hud.GetComponent<CozyTownInteractionDebugView>()?.enabled, Is.True);
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

        private static GameObject RequireRoot(Scene scene, string name)
        {
            var root = Array.Find(
                scene.GetRootGameObjects(),
                candidate => candidate.name == name);
            Assert.That(root, Is.Not.Null, $"Root object '{name}' was not found.");
            return root;
        }
    }
}
#endif
