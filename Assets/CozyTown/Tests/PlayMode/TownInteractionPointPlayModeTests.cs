using CozyTown.Unity.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class TownInteractionPointPlayModeTests
    {
        [TestCase(TownInteractionKind.Shop, "Open shop")]
        [TestCase(TownInteractionKind.Npc, "Talk")]
        [TestCase(TownInteractionKind.Bed, "Sleep")]
        [TestCase(TownInteractionKind.Farm, "Tend field")]
        [TestCase(TownInteractionKind.Coop, "Tend coop")]
        [TestCase(TownInteractionKind.Pond, "Fish")]
        [TestCase(TownInteractionKind.Kitchen, "Cook")]
        public void Configure_SetsKindAndPrompt_AndRequiresAnActor(
            TownInteractionKind kind,
            string prompt)
        {
            var gameObject = new GameObject("Interaction point");
            var actor = new GameObject("Actor");

            try
            {
                var point = gameObject.AddComponent<TownInteractionPoint2D>();
                point.Configure(kind, prompt);

                Assert.That(point.Kind, Is.EqualTo(kind));
                Assert.That(point.PromptText, Is.EqualTo(prompt));
                Assert.That(point.InteractionCount, Is.Zero);
                Assert.That(point.CanInteract(default(InteractionContext)), Is.False);

                point.Interact(new InteractionContext(actor));
                Assert.That(point.InteractionCount, Is.EqualTo(1));

                point.Interact(default(InteractionContext));
                Assert.That(point.InteractionCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(actor);
            }
        }
    }
}
