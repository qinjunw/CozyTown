using System;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Npc;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Npc
{
    public sealed class FixedFallbackDialogueGeneratorTests
    {
        [Test]
        public async Task GenerateAsync_ReturnsFixedSafeFallbackReply()
        {
            var generator = new FixedFallbackDialogueGenerator("The shop opens at sunrise.");
            var context = new NpcDialogueContext(
                "shopkeeper",
                "Mina",
                "Practical and kind",
                day: 2,
                minuteOfDay: 8 * 60,
                affinity: 3,
                recentActivities: Array.Empty<string>(),
                memories: Array.Empty<string>());

            NpcDialogueReply reply = await generator.GenerateAsync(context, CancellationToken.None);

            Assert.That(reply.Text, Is.EqualTo("The shop opens at sunrise."));
            Assert.That(reply.EmotionTag, Is.EqualTo("neutral"));
            Assert.That(reply.ActionTag, Is.EqualTo("idle"));
            Assert.That(reply.IsFallback, Is.True);
        }
    }
}
