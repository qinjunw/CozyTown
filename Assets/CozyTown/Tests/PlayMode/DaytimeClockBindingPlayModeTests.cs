using CozyTown.Runtime.Core;
using CozyTown.Unity.Core;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using CozyTown.Unity.Time;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class DaytimeClockBindingPlayModeTests
    {
        private GameObject _player;
        private GameObject _root;

        [TestCase(false)]
        [TestCase(true)]
        public void RegisterDaytimeClock_BeforeOrAfterInitialization_BindsSharedClock(bool registerAfter)
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            var gate = CreateGate();
            _root = new GameObject("Clock Bootstrap");
            _root.SetActive(false);
            var driver = _root.AddComponent<DaytimeClockDriver>();
            driver.ConfigureInputGate(gate);
            var bootstrap = _root.AddComponent<CozyTownBootstrap>();
            if (!registerAfter)
            {
                bootstrap.RegisterDaytimeClock(driver);
            }
            bootstrap.Initialize(services);
            if (registerAfter)
            {
                bootstrap.RegisterDaytimeClock(driver);
            }
            _root.SetActive(true);
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            driver.AdvanceFrame(5);

            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(370));
            bootstrap.RegisterDaytimeClock(driver);
            driver.AdvanceFrame(5);
            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(380));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
            if (_player != null)
            {
                Object.DestroyImmediate(_player);
            }
        }

        private PlayerModalInputGate2D CreateGate()
        {
            _player = new GameObject("Clock Player");
            _player.SetActive(false);
            _player.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var input = _player.AddComponent<PlayModePlayerInputSource>();
            var movement = _player.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);
            var probe = _player.AddComponent<InteractionProbe2D>();
            _player.AddComponent<PlayerInteractor2D>().Configure(input, probe);
            var gate = _player.AddComponent<PlayerModalInputGate2D>();
            _player.SetActive(true);
            return gate;
        }
    }
}
