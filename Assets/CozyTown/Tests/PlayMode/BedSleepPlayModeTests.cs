using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Core;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Tests.PlayMode
{
    public sealed class BedSleepPlayModeTests
    {
        private GameObject _root;
        private CozyTownServices _services;
        private CozyTownBedDebugView _view;
        private PlayerModalInputGate2D _gate;
        private GameObject _actor;
        private TownInteractionPoint2D _point;
        private Text _feedback;
        private Text _sleepHoursText;
        private Button _sleepButton;
        private Button _decreaseSleepButton;
        private Button _increaseSleepButton;
        private Button _closeButton;

        [Test]
        public void ConfirmDefaultSleep_RealButtonAdvancesSharedClockByEightHours()
        {
            CreateFixture();

            _sleepButton.onClick.Invoke();

            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 840)));
            Assert.That(_feedback.text, Is.EqualTo("Slept to Day 1 14:00."));
            Assert.That(_view.IsVisible, Is.True);
            Assert.That(_gate.IsAcquired, Is.True);
        }

        [Test]
        public void SelectTwoHours_RealButtonsOnlyAdvanceClockAfterConfirmation()
        {
            CreateFixture();

            for (int click = 0; click < 6; click++)
            {
                _decreaseSleepButton.onClick.Invoke();
            }

            Assert.That(_view.SelectedSleepHours, Is.EqualTo(2));
            Assert.That(_sleepHoursText.text, Is.EqualTo("2 hours"));
            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));

            _sleepButton.onClick.Invoke();

            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 480)));
            Assert.That(_feedback.text, Is.EqualTo("Slept to Day 1 08:00."));
            Assert.That(_view.SelectedSleepHours, Is.EqualTo(2));
            Assert.That(_sleepHoursText.text, Is.EqualTo("2 hours"));
        }

        [TestCase(1)]
        [TestCase(12)]
        public void SleepSelection_StopsAtHourBoundsAndCancelDoesNotAdvanceClock(int targetHours)
        {
            CreateFixture();
            Button direction = targetHours == 1 ? _decreaseSleepButton : _increaseSleepButton;
            for (int click = 0; click < 20; click++)
            {
                direction.onClick.Invoke();
            }

            Assert.That(_view.SelectedSleepHours, Is.EqualTo(targetHours));
            Assert.That(_sleepHoursText.text, Is.EqualTo(targetHours == 1 ? "1 hour" : "12 hours"));
            Assert.That(_decreaseSleepButton.interactable, Is.EqualTo(targetHours != 1));
            Assert.That(_increaseSleepButton.interactable, Is.EqualTo(targetHours != 12));
            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));

            _closeButton.onClick.Invoke();

            Assert.That(_view.IsVisible, Is.False);
            Assert.That(_gate.IsAcquired, Is.False);
            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));
        }

        [Test]
        public void ReopenAfterCancel_StartsWithEightHoursWithoutAdvancingClock()
        {
            CreateFixture();
            for (int click = 0; click < 6; click++)
            {
                _decreaseSleepButton.onClick.Invoke();
            }

            _closeButton.onClick.Invoke();

            Assert.That(_gate.IsAcquired, Is.False);
            Assert.That(_view.IsVisible, Is.False);
            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));

            _point.Interact(new InteractionContext(_actor));

            Assert.That(_view.SelectedSleepHours, Is.EqualTo(8));
            Assert.That(_sleepHoursText.text, Is.EqualTo("8 hours"));
            Assert.That(_gate.IsAcquired, Is.True);
            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));
        }

        [Test]
        public void DisableView_ReleasesModalGateWithoutAdvancingClock()
        {
            CreateFixture();

            _view.enabled = false;

            Assert.That(_gate.IsAcquired, Is.False);
            Assert.That(_gate.GetComponent<PlayerMovement2D>().enabled, Is.True);
            Assert.That(_gate.GetComponent<PlayerInteractor2D>().enabled, Is.True);
            Assert.That(_view.IsVisible, Is.False);
            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));

            _view.RequestSleep();
            _sleepButton.onClick.Invoke();

            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));
        }

        [Test]
        public void DisabledView_RejectsInteractionAndReenabledViewCanSleep()
        {
            CreateFixture();
            _view.enabled = false;

            _point.Interact(new InteractionContext(_actor));

            Assert.That(_gate.IsAcquired, Is.False);
            Assert.That(_view.IsVisible, Is.False);
            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 360)));

            _view.enabled = true;
            _point.Interact(new InteractionContext(_actor));
            Assert.That(_gate.IsAcquired, Is.True);
            Assert.That(_view.IsVisible, Is.True);

            _sleepButton.onClick.Invoke();

            Assert.That(_services.Time.Current, Is.EqualTo(new GameClockSnapshot(1, 840)));
        }

        private void CreateFixture()
        {
            _services = CozyTownCompositionRoot.CreateDefault();
            _root = new GameObject("Bed Sleep Fixture");
            _root.SetActive(false);

            _actor = CreateChild("Player", _root.transform);
            _actor.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var input = _actor.AddComponent<PlayModePlayerInputSource>();
            _actor.AddComponent<PlayerMovement2D>().SetInputSource(input);
            var probe = _actor.AddComponent<InteractionProbe2D>();
            _actor.AddComponent<PlayerInteractor2D>().Configure(input, probe);
            _gate = _actor.AddComponent<PlayerModalInputGate2D>();

            _point = CreateChild("Bed", _root.transform)
                .AddComponent<TownInteractionPoint2D>();
            _point.Configure(TownInteractionKind.Bed, "Sleep");

            GameObject hud = CreateChild("Bed HUD", _root.transform);
            _view = hud.AddComponent<CozyTownBedDebugView>();
            GameObject panel = CreateUiChild("Bed Panel", hud.transform);
            _feedback = CreateUiChild("Feedback Text", panel.transform).AddComponent<Text>();
            _closeButton = CreateUiChild("Close Button", panel.transform).AddComponent<Button>();
            _sleepButton = CreateUiChild("Sleep Button", panel.transform).AddComponent<Button>();
            _sleepHoursText = CreateUiChild("Sleep Hours Text", panel.transform).AddComponent<Text>();
            _decreaseSleepButton = CreateUiChild("Decrease Sleep Button", panel.transform).AddComponent<Button>();
            _increaseSleepButton = CreateUiChild("Increase Sleep Button", panel.transform).AddComponent<Button>();
            _view.ConfigureUi(
                panel,
                _feedback,
                _closeButton,
                _sleepButton,
                _sleepHoursText,
                _decreaseSleepButton,
                _increaseSleepButton);
            var presenter = hud.AddComponent<CozyTownBedDebugPresenter>();
            presenter.Configure(_point, _view);

            var bootstrap = _root.AddComponent<CozyTownBootstrap>();
            bootstrap.RegisterBedPresenter(presenter);
            bootstrap.Initialize(_services);
            _root.SetActive(true);
            _point.Interact(new InteractionContext(_actor));
            Assert.That(presenter.IsOpen, Is.True);
            Assert.That(_gate.IsAcquired, Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static GameObject CreateUiChild(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
        }
    }
}
