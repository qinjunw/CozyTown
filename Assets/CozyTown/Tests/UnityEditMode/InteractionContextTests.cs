using CozyTown.Unity.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class InteractionContextTests
    {
        [Test]
        public void PublicSurface_ExposesActorWithoutServiceBag()
        {
            var contextType = typeof(InteractionContext);

            Assert.That(contextType.GetProperty("Actor"), Is.Not.Null);
            Assert.That(contextType.GetProperty("Services"), Is.Null);
            Assert.That(contextType.GetConstructor(new[] { typeof(GameObject) }), Is.Not.Null);
        }
    }
}
