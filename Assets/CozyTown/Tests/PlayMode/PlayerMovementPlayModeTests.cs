using System.Collections;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class PlayerMovementPlayModeTests
    {
        private GameObject _player;

        [UnityTest]
        public IEnumerator DisablingMovement_ClearsRigidbodyVelocity()
        {
            _player = new GameObject("Player");
            _player.SetActive(false);

            var body = _player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            var input = _player.AddComponent<PlayModePlayerInputSource>();
            var movement = _player.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);

            _player.SetActive(true);
            yield return null;

            body.linearVelocity = new Vector2(2f, -3f);
            movement.enabled = false;

            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null)
            {
                Object.DestroyImmediate(_player);
            }
        }
    }
}
