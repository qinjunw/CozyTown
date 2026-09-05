using System.Collections;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using CozyTown.Unity.Time;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class DaytimeClockPlayModeTests
    {
        private GameObject _player;
        private GameObject _replacementPlayer;
        private GameObject _driverObject;
        private PlayerModalInputGate2D _gate;
        private DaytimeClockDriver _driver;
        private IDaytimeClock _clock;

        [Test]
        public void ModalPause_StopsClockAndDiscardsFirstResumeFrame()
        {
            CreateFixture();
            _driver.AdvanceFrame(0);
            _driver.AdvanceFrame(5);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));

            var owner = new object();
            Assert.That(_gate.TryAcquire(owner), Is.True);
            Assert.That(_driver.IsSimulationPaused, Is.True);
            _driver.AdvanceFrame(30);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));

            Assert.That(_gate.Release(owner), Is.True);
            Assert.That(_driver.IsSimulationPaused, Is.False);
            _driver.AdvanceFrame(30);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));

            _driver.AdvanceFrame(5);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 380)));
        }

        [Test]
        public void ModalAndFocusPause_RequireBothReasonsToClearBeforeClockResumes()
        {
            CreateFixture();
            _driver.AdvanceFrame(0);
            _driver.AdvanceFrame(5);

            var owner = new object();
            Assert.That(_gate.TryAcquire(owner), Is.True);
            _driver.SetApplicationFocus(false);
            _driver.AdvanceFrame(30);

            Assert.That(_gate.Release(owner), Is.True);
            Assert.That(_driver.IsSimulationPaused, Is.True);
            _driver.AdvanceFrame(300);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));

            _driver.SetApplicationFocus(true);
            Assert.That(_driver.IsSimulationPaused, Is.False);
            _driver.AdvanceFrame(300);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));

            _driver.AdvanceFrame(5);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 380)));
        }

        [Test]
        public void Reenable_DiscardsResumeFrameAndPreservesPartialTick()
        {
            CreateFixture();
            _driver.AdvanceFrame(0);
            _driver.AdvanceFrame(4.9);

            _driver.enabled = false;
            Assert.That(_driver.IsSimulationPaused, Is.True);
            _driver.AdvanceFrame(300);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 369)));

            _driver.enabled = true;
            _driver.SetApplicationFocus(true);
            _driver.AdvanceFrame(300);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 369)));

            _driver.AdvanceFrame(0.1);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));
        }

        [Test]
        public void RepeatedBindingsAndFocus_KeepPartialTickAndAcceptNextFrame()
        {
            CreateFixture();
            _driver.AdvanceFrame(0);
            _driver.AdvanceFrame(4.9);

            _driver.Bind(_clock);
            _driver.ConfigureInputGate(_gate);
            _driver.SetApplicationFocus(true);
            _driver.AdvanceFrame(0.1);

            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));
        }

        [UnityTest]
        public IEnumerator LateUpdate_RespectsModalOpenedInUpdateAndResumesAfterRelease()
        {
            CreateFixture();
            _driver.SetApplicationFocus(false);
            var opener = _player.AddComponent<DaytimeModalOnUpdate>();
            opener.InputGate = _gate;
            opener.ClockDriver = _driver;

            yield return null;
            yield return null;

            Assert.That(_gate.IsAcquired, Is.True);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 369)));
            Assert.That(_gate.Release(opener), Is.True);

            for (var frame = 0; frame < 8 && _clock.Current.MinuteOfDay == 369; frame++)
            {
                yield return null;
            }

            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));
        }

        [Test]
        public void GateDisable_RevokesPauseAndPreservesPartialTick()
        {
            CreateFixture();
            _driver.AdvanceFrame(0);
            _driver.AdvanceFrame(4.9);
            Assert.That(_gate.TryAcquire(new object()), Is.True);
            _driver.AdvanceFrame(300);

            _gate.enabled = false;
            Assert.That(_gate.IsAcquired, Is.False);
            Assert.That(_driver.IsSimulationPaused, Is.False);
            _driver.AdvanceFrame(300);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 369)));

            _driver.AdvanceFrame(0.1);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));
        }

        [Test]
        public void GateReconfiguration_StopsFollowingOldGateAndUsesReplacement()
        {
            CreateFixture();
            _driver.AdvanceFrame(0);
            _driver.AdvanceFrame(4.9);
            var replacementGate = CreatePlayerGate(out _replacementPlayer);
            _driver.ConfigureInputGate(replacementGate);
            _driver.AdvanceFrame(300);

            Assert.That(_gate.TryAcquire(new object()), Is.True);
            Assert.That(_driver.IsSimulationPaused, Is.False);
            _driver.AdvanceFrame(0.1);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));

            Assert.That(replacementGate.TryAcquire(new object()), Is.True);
            Assert.That(_driver.IsSimulationPaused, Is.True);
            _driver.AdvanceFrame(300);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));
        }

        [Test]
        public void InvalidGateReconfiguration_PreservesOriginalPauseSubscription()
        {
            CreateFixture();
            _driver.AdvanceFrame(0);
            _driver.AdvanceFrame(4.9);

            Assert.Throws<System.ArgumentNullException>(() => _driver.ConfigureInputGate(null));
            var owner = new object();
            Assert.That(_gate.TryAcquire(owner), Is.True);
            Assert.That(_gate.Release(owner), Is.True);
            _driver.AdvanceFrame(300);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 369)));

            _driver.AdvanceFrame(0.1);
            Assert.That(_clock.Current, Is.EqualTo(new GameClockSnapshot(1, 370)));
        }

        [TearDown]
        public void TearDown()
        {
            if (_driverObject != null)
            {
                Object.DestroyImmediate(_driverObject);
            }

            if (_player != null)
            {
                Object.DestroyImmediate(_player);
            }

            if (_replacementPlayer != null)
            {
                Object.DestroyImmediate(_replacementPlayer);
            }
        }

        private void CreateFixture()
        {
            var services = CozyTownCompositionRoot.CreateEmpty();
            _clock = services.DaytimeClock;

            _gate = CreatePlayerGate(out _player);
            _driverObject = new GameObject("Daytime Clock");
            _driverObject.SetActive(false);
            _driver = _driverObject.AddComponent<DaytimeClockDriver>();
            _driver.ConfigureInputGate(_gate);
            _driver.Bind(_clock);
            _driverObject.SetActive(true);
            _driver.SetApplicationFocus(true);
        }

        private static PlayerModalInputGate2D CreatePlayerGate(out GameObject player)
        {
            player = new GameObject("Daytime Clock Player");
            player.SetActive(false);
            player.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var input = player.AddComponent<PlayModePlayerInputSource>();
            var movement = player.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);
            var probe = player.AddComponent<InteractionProbe2D>();
            var interactor = player.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);
            var gate = player.AddComponent<PlayerModalInputGate2D>();
            player.SetActive(true);
            return gate;
        }
    }

    internal sealed class DaytimeModalOnUpdate : MonoBehaviour
    {
        public PlayerModalInputGate2D InputGate { get; set; }

        public DaytimeClockDriver ClockDriver { get; set; }

        private void Update()
        {
            if (InputGate != null && ClockDriver != null)
            {
                ClockDriver.SetApplicationFocus(true);
                ClockDriver.AdvanceFrame(0);
                ClockDriver.AdvanceFrame(4.999999);
                InputGate.TryAcquire(this);
                enabled = false;
            }
        }
    }
}
