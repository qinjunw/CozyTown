using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class PlayerMovement2DTests
    {
        [Test]
        public void CalculateVelocity_ClampsDiagonalInputToConfiguredSpeed()
        {
            var velocity = PlayerMovement2D.CalculateVelocity(new Vector2(1f, 1f), 4f);

            Assert.That(velocity.magnitude, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(velocity.x, Is.EqualTo(velocity.y).Within(0.0001f));
        }

        [Test]
        public void CalculateVelocity_NegativeSpeedProducesZeroVelocity()
        {
            var velocity = PlayerMovement2D.CalculateVelocity(Vector2.right, -1f);

            Assert.That(velocity, Is.EqualTo(Vector2.zero));
        }
    }
}
