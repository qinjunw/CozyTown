using CozyTown.Unity.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class TownInteractionPointEventEditModeTests
    {
        [Test]
        public void Interact_RaisesOneEventOnlyForAValidActor()
        {
            var pointObject = new GameObject("Shop point");
            var actor = new GameObject("Actor");

            try
            {
                var point = pointObject.AddComponent<TownInteractionPoint2D>();
                point.Configure(TownInteractionKind.Shop, "Open shop");
                var eventCount = 0;
                GameObject eventActor = null;
                point.Interacted += context =>
                {
                    eventCount++;
                    eventActor = context.Actor;
                };

                point.Interact(default);
                Assert.That(eventCount, Is.Zero);

                point.Interact(new InteractionContext(actor));
                Assert.That(eventCount, Is.EqualTo(1));
                Assert.That(eventActor, Is.SameAs(actor));
            }
            finally
            {
                Object.DestroyImmediate(pointObject);
                Object.DestroyImmediate(actor);
            }
        }
    }
}
