using System.Collections;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CozyTown.Tests.PlayMode
{
    public sealed class SystemMenuPlayModeTests
    {
        private GameObject _player;
        private GameObject _uiRoot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_uiRoot != null)
            {
                Object.Destroy(_uiRoot);
            }

            if (_player != null)
            {
                Object.Destroy(_player);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Controller_WhenGateIsRevoked_HidesMenu()
        {
            var fixture = CreateViewFixture();
            var gate = CreatePlayerGate();
            var controller = _uiRoot.AddComponent<CozyTownSystemMenuController>();
            controller.Configure(gate, fixture.View);

            fixture.GearButton.onClick.Invoke();
            Assert.That(fixture.View.IsVisible, Is.True);

            gate.enabled = false;
            yield return null;

            Assert.That(fixture.View.IsVisible, Is.False);
            Assert.That(controller.IsOpen, Is.False);
        }

        private SystemMenuFixture CreateViewFixture()
        {
            _uiRoot = new GameObject("System Menu PlayMode Fixture");
            var gearButton = CreateButton("Gear", _uiRoot.transform);
            var panel = new GameObject("System Menu Panel");
            panel.transform.SetParent(_uiRoot.transform, false);
            var mainPage = new GameObject("Main Page");
            mainPage.transform.SetParent(panel.transform, false);
            var settingsPage = new GameObject("Settings Page");
            settingsPage.transform.SetParent(panel.transform, false);
            var settingsButton = CreateButton("Settings", mainPage.transform);
            var backButton = CreateButton("Back", settingsPage.transform);
            var quitButton = CreateButton("Quit", mainPage.transform);
            var view = _uiRoot.AddComponent<CozyTownSystemMenuView>();
            view.ConfigureUi(
                gearButton,
                panel,
                mainPage,
                settingsPage,
                settingsButton,
                backButton,
                quitButton);
            return new SystemMenuFixture(view, gearButton);
        }

        private PlayerModalInputGate2D CreatePlayerGate()
        {
            _player = new GameObject("Player");
            _player.SetActive(false);
            _player.AddComponent<Rigidbody2D>();
            var input = _player.AddComponent<SystemMenuPlayModeInputSource>();
            var movement = _player.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);
            var probe = _player.AddComponent<InteractionProbe2D>();
            var interactor = _player.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);
            var gate = _player.AddComponent<PlayerModalInputGate2D>();
            _player.SetActive(true);
            return gate;
        }

        private static Button CreateButton(string name, Transform parent)
        {
            var buttonObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            return buttonObject.GetComponent<Button>();
        }

        private readonly struct SystemMenuFixture
        {
            public SystemMenuFixture(CozyTownSystemMenuView view, Button gearButton)
            {
                View = view;
                GearButton = gearButton;
            }

            public CozyTownSystemMenuView View { get; }
            public Button GearButton { get; }
        }
    }

    internal sealed class SystemMenuPlayModeInputSource : MonoBehaviour, IPlayerInputSource
    {
        public Vector2 Movement => Vector2.zero;
        public bool InteractPressedThisFrame => false;
    }
}
