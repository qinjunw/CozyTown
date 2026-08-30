#if UNITY_EDITOR
using System;
using System.Collections;
using CozyTown.Unity.Inventory;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class InventorySceneInputPlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        private Scene _loadedScene;
        private InputTestFixture _inputFixture;
        private Keyboard _keyboard;

        [SetUp]
        public void SetUp()
        {
            _inputFixture = new InputTestFixture();
            _inputFixture.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [UnityTest]
        public IEnumerator DevelopmentScene_KeyboardDrivesHotbarAndBackpack()
        {
            var loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return loadOperation;
            yield return null;

            _loadedScene = SceneManager.GetSceneByPath(ScenePath);
            var player = RequireRoot(_loadedScene, "Player");
            var hud = RequireRoot(_loadedScene, "Debug HUD");
            var presenter = hud.GetComponent<CozyTownInventoryPresenter>();
            var backpack = hud.GetComponent<CozyTownBackpackView>();
            var gate = player.GetComponent<PlayerModalInputGate2D>();
            var movement = player.GetComponent<PlayerMovement2D>();
            var interactor = player.GetComponent<CozyTown.Unity.Interaction.PlayerInteractor2D>();
            Assert.That(presenter, Is.Not.Null);
            Assert.That(backpack, Is.Not.Null);
            Assert.That(gate, Is.Not.Null);

            _inputFixture.Press(_keyboard.digit5Key);
            yield return null;
            _inputFixture.Release(_keyboard.digit5Key);
            yield return null;
            Assert.That(presenter.SelectedHotbarIndex, Is.EqualTo(4));

            _inputFixture.Press(_keyboard.bKey);
            yield return null;
            _inputFixture.Release(_keyboard.bKey);
            yield return null;
            Assert.That(backpack.IsVisible, Is.True);
            Assert.That(gate.IsAcquired, Is.True);
            Assert.That(movement.enabled, Is.False);
            Assert.That(interactor.enabled, Is.False);

            _inputFixture.Press(_keyboard.bKey);
            yield return null;
            _inputFixture.Release(_keyboard.bKey);
            yield return null;
            Assert.That(backpack.IsVisible, Is.False);
            Assert.That(gate.IsAcquired, Is.False);
            Assert.That(movement.enabled, Is.True);
            Assert.That(interactor.enabled, Is.True);
        }

        [UnityTearDown]
        public IEnumerator UnloadDevelopmentScene()
        {
            if (_loadedScene.IsValid() && _loadedScene.isLoaded)
            {
                var unloadOperation = SceneManager.UnloadSceneAsync(_loadedScene);
                if (unloadOperation != null)
                {
                    yield return unloadOperation;
                }
            }

            _inputFixture?.TearDown();
            _inputFixture = null;
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
