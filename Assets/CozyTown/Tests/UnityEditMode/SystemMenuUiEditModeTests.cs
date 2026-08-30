using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class SystemMenuUiEditModeTests
    {
        private GameObject _player;
        private GameObject _uiRoot;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_uiRoot);
            Object.DestroyImmediate(_player);
        }

        [Test]
        public void View_GearAndMainPageButtonsExposePublicRequests()
        {
            var fixture = CreateViewFixture();
            var gearRequests = 0;
            var settingsRequests = 0;
            var backRequests = 0;
            var quitRequests = 0;
            fixture.View.GearRequested += () => gearRequests++;
            fixture.View.SettingsRequested += () => settingsRequests++;
            fixture.View.BackRequested += () => backRequests++;
            fixture.View.QuitRequested += () => quitRequests++;

            fixture.GearButton.onClick.Invoke();
            fixture.View.ShowMain();
            fixture.SettingsButton.onClick.Invoke();
            fixture.QuitButton.onClick.Invoke();
            fixture.View.ShowSettings();
            fixture.BackButton.onClick.Invoke();

            Assert.That(gearRequests, Is.EqualTo(1));
            Assert.That(settingsRequests, Is.EqualTo(1));
            Assert.That(backRequests, Is.EqualTo(1));
            Assert.That(quitRequests, Is.EqualTo(1));
        }

        [Test]
        public void View_ShowMainShowSettingsAndHideAreMutuallyExclusive()
        {
            var fixture = CreateViewFixture();

            fixture.View.ShowMain();
            Assert.That(fixture.View.IsVisible, Is.True);
            Assert.That(fixture.View.IsSettingsVisible, Is.False);
            Assert.That(fixture.Panel.activeSelf, Is.True);
            Assert.That(fixture.MainPage.activeSelf, Is.True);
            Assert.That(fixture.SettingsPage.activeSelf, Is.False);

            fixture.View.ShowSettings();
            Assert.That(fixture.View.IsVisible, Is.True);
            Assert.That(fixture.View.IsSettingsVisible, Is.True);
            Assert.That(fixture.MainPage.activeSelf, Is.False);
            Assert.That(fixture.SettingsPage.activeSelf, Is.True);

            fixture.View.Hide();
            Assert.That(fixture.View.IsVisible, Is.False);
            Assert.That(fixture.Panel.activeSelf, Is.False);
        }

        [Test]
        public void Controller_GearOwnsGateAndQuitUsesInjectedAdapter()
        {
            var fixture = CreateViewFixture();
            var gate = CreatePlayerGate();
            var quitter = new RecordingApplicationQuitter();
            var controller = _uiRoot.AddComponent<CozyTownSystemMenuController>();
            controller.Configure(gate, fixture.View, quitter);

            fixture.GearButton.onClick.Invoke();
            Assert.That(gate.IsAcquired, Is.True);
            Assert.That(fixture.View.IsVisible, Is.True);

            fixture.SettingsButton.onClick.Invoke();
            Assert.That(fixture.View.IsSettingsVisible, Is.True);
            fixture.BackButton.onClick.Invoke();
            Assert.That(fixture.View.IsSettingsVisible, Is.False);

            fixture.QuitButton.onClick.Invoke();
            Assert.That(quitter.RequestCount, Is.EqualTo(1));

            fixture.GearButton.onClick.Invoke();
            Assert.That(gate.IsAcquired, Is.False);
            Assert.That(fixture.View.IsVisible, Is.False);
        }

        [Test]
        public void Controller_WhenAnotherModalOwnsGate_DoesNotOpen()
        {
            var fixture = CreateViewFixture();
            var gate = CreatePlayerGate();
            Assert.That(gate.TryAcquire(new object()), Is.True);
            var controller = _uiRoot.AddComponent<CozyTownSystemMenuController>();
            controller.Configure(gate, fixture.View, new RecordingApplicationQuitter());

            fixture.GearButton.onClick.Invoke();

            Assert.That(fixture.View.IsVisible, Is.False);
        }

        [Test]
        public void Controller_WhenGateIsRevoked_HidesMenu()
        {
            var fixture = CreateViewFixture();
            var gate = CreatePlayerGate();
            var controller = _uiRoot.AddComponent<CozyTownSystemMenuController>();
            controller.Configure(gate, fixture.View, new RecordingApplicationQuitter());
            fixture.GearButton.onClick.Invoke();
            Assert.That(fixture.View.IsVisible, Is.True);

            gate.enabled = false;

            Assert.That(fixture.View.IsVisible, Is.False);
            Assert.That(controller.IsOpen, Is.False);
        }

        private SystemMenuFixture CreateViewFixture()
        {
            _uiRoot = new GameObject("System Menu Fixture");
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
            return new SystemMenuFixture(
                view,
                gearButton,
                panel,
                mainPage,
                settingsPage,
                settingsButton,
                backButton,
                quitButton);
        }

        private PlayerModalInputGate2D CreatePlayerGate()
        {
            _player = new GameObject("Player");
            _player.SetActive(false);
            _player.AddComponent<Rigidbody2D>();
            var input = _player.AddComponent<SystemMenuInputSource>();
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
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            return buttonObject.GetComponent<Button>();
        }

        private readonly struct SystemMenuFixture
        {
            public SystemMenuFixture(
                CozyTownSystemMenuView view,
                Button gearButton,
                GameObject panel,
                GameObject mainPage,
                GameObject settingsPage,
                Button settingsButton,
                Button backButton,
                Button quitButton)
            {
                View = view;
                GearButton = gearButton;
                Panel = panel;
                MainPage = mainPage;
                SettingsPage = settingsPage;
                SettingsButton = settingsButton;
                BackButton = backButton;
                QuitButton = quitButton;
            }

            public CozyTownSystemMenuView View { get; }
            public Button GearButton { get; }
            public GameObject Panel { get; }
            public GameObject MainPage { get; }
            public GameObject SettingsPage { get; }
            public Button SettingsButton { get; }
            public Button BackButton { get; }
            public Button QuitButton { get; }
        }

        private sealed class RecordingApplicationQuitter : IApplicationQuitter
        {
            public int RequestCount { get; private set; }

            public void Quit() => RequestCount++;
        }
    }

    internal sealed class SystemMenuInputSource : MonoBehaviour, IPlayerInputSource
    {
        public Vector2 Movement => Vector2.zero;
        public bool InteractPressedThisFrame => false;
    }
}
