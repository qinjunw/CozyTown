using CozyTown.Runtime.Save;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class PlayerBodySnapshotPlayModeTests
    {
        private GameObject _player;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_player);

        [Test]
        public void RestorePlayer_PreservesPositionAndFacingClearsVelocityAndClosesOldModal()
        {
            _player = new GameObject("Snapshot player");
            _player.SetActive(false);
            _player.transform.position = new Vector2(-3f, 4f);
            var body = _player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0;
            var input = _player.AddComponent<PlayModePlayerInputSource>();
            var movement = _player.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);
            var probe = _player.AddComponent<InteractionProbe2D>();
            var interactor = _player.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);
            var gate = _player.AddComponent<PlayerModalInputGate2D>();
            _player.SetActive(true);
            var initial = movement.CaptureInitialSnapshot();
            movement.RestoreSnapshot(new PlayerBodySnapshot(
                new Position2DSnapshot(3f, 4f), new Position2DSnapshot(1f, 0f)));
            var saved = movement.CaptureSnapshot();
            body.position = new Vector2(20f, 30f);
            Assert.That(gate.TryAcquire(new object()), Is.True);
            bool oldModalClosed = false;
            gate.AcquisitionRevoked += () => oldModalClosed = true;
            body.linearVelocity = new Vector2(2f, 3f);

            PlayerMovement2D.ValidateSnapshot(saved);
            movement.RestoreSnapshot(saved);
            gate.Revoke();

            Assert.That(body.position, Is.EqualTo(new Vector2(3f, 4f)));
            Assert.That((Vector2)_player.transform.position, Is.EqualTo(body.position));
            Assert.That(movement.LastMoveDirection, Is.EqualTo(Vector2.right));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(gate.IsAcquired, Is.False);
            Assert.That(oldModalClosed, Is.True);
            Assert.That(movement.enabled, Is.True);
            Assert.That(interactor.enabled, Is.True);
            Assert.That(initial.Position.X, Is.EqualTo(-3f));
            Assert.That(movement.CaptureInitialSnapshot().Position.X, Is.EqualTo(-3f));
        }
    }
}
