using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class PlayerModalInputGate2DTests
    {
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            _player = new GameObject("Player");
            _player.SetActive(false);
            var body = _player.AddComponent<Rigidbody2D>();
            var input = _player.AddComponent<EditModePlayerInputSource>();
            var movement = _player.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);
            var probe = _player.AddComponent<InteractionProbe2D>();
            var interactor = _player.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);
            _player.AddComponent<PlayerModalInputGate2D>();
            _player.SetActive(true);
            body.linearVelocity = new Vector2(3f, -2f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_player);
        }

        [Test]
        public void TryAcquire_IsExclusiveAndIdempotentForOwner()
        {
            var gate = _player.GetComponent<PlayerModalInputGate2D>();
            var owner = new object();

            Assert.That(gate.TryAcquire(owner), Is.True);
            Assert.That(gate.TryAcquire(owner), Is.True);
            Assert.That(gate.TryAcquire(new object()), Is.False);
            Assert.That(gate.IsAcquired, Is.True);
            Assert.That(_player.GetComponent<Rigidbody2D>().linearVelocity, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Release_WrongOrRepeatedOwnerDoesNotChangeState()
        {
            var gate = _player.GetComponent<PlayerModalInputGate2D>();
            var owner = new object();
            gate.TryAcquire(owner);

            Assert.That(gate.Release(new object()), Is.False);
            Assert.That(gate.IsAcquired, Is.True);
            Assert.That(gate.Release(owner), Is.True);
            Assert.That(gate.Release(owner), Is.False);
            Assert.That(gate.IsAcquired, Is.False);
        }

        [Test]
        public void TryAcquire_RejectsActorWithoutPlayerMovement()
        {
            Object.DestroyImmediate(_player.GetComponent<PlayerMovement2D>());
            var gate = _player.GetComponent<PlayerModalInputGate2D>();

            Assert.That(gate.TryAcquire(new object()), Is.False);
            Assert.That(gate.IsAcquired, Is.False);
        }

        [Test]
        public void TryAcquire_RejectsActorWithoutPlayerInteractor()
        {
            Object.DestroyImmediate(_player.GetComponent<PlayerInteractor2D>());
            var gate = _player.GetComponent<PlayerModalInputGate2D>();

            Assert.That(gate.TryAcquire(new object()), Is.False);
            Assert.That(gate.IsAcquired, Is.False);
        }
    }

    internal sealed class EditModePlayerInputSource : MonoBehaviour, IPlayerInputSource
    {
        public Vector2 Movement => Vector2.zero;

        public bool InteractPressedThisFrame => false;
    }
}
