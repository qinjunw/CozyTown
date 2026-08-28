using System.Collections;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class PlayerModalInputGate2DPlayModeTests
    {
        private GameObject _player;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _player = new GameObject("Player");
            _player.SetActive(false);
            var body = _player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            var input = _player.AddComponent<PlayModePlayerInputSource>();
            var movement = _player.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);
            var probe = _player.AddComponent<InteractionProbe2D>();
            var interactor = _player.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);
            _player.AddComponent<PlayerModalInputGate2D>();
            _player.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_player);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Release_RestoresCapturedEnabledStates()
        {
            var gate = _player.GetComponent<PlayerModalInputGate2D>();
            var movement = _player.GetComponent<PlayerMovement2D>();
            var interactor = _player.GetComponent<PlayerInteractor2D>();
            var owner = new object();

            Assert.That(gate.TryAcquire(owner), Is.True);
            Assert.That(movement.enabled, Is.False);
            Assert.That(interactor.enabled, Is.False);
            Assert.That(gate.Release(owner), Is.True);
            Assert.That(movement.enabled, Is.True);
            Assert.That(interactor.enabled, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Disable_RestoresCapturedEnabledStates()
        {
            var gate = _player.GetComponent<PlayerModalInputGate2D>();
            var movement = _player.GetComponent<PlayerMovement2D>();
            var interactor = _player.GetComponent<PlayerInteractor2D>();
            gate.TryAcquire(new object());

            gate.enabled = false;

            Assert.That(gate.IsAcquired, Is.False);
            Assert.That(movement.enabled, Is.True);
            Assert.That(interactor.enabled, Is.True);
            yield return null;
        }
    }
}
