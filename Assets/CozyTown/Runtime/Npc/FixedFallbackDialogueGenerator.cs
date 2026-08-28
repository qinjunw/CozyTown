using System;
using System.Threading;
using System.Threading.Tasks;

namespace CozyTown.Runtime.Npc
{
    public sealed class FixedFallbackDialogueGenerator : INpcDialogueGenerator
    {
        private readonly string _fallbackText;

        public FixedFallbackDialogueGenerator(string fallbackText)
        {
            if (string.IsNullOrWhiteSpace(fallbackText))
            {
                throw new ArgumentException("Fallback dialogue must not be empty.", nameof(fallbackText));
            }

            _fallbackText = fallbackText;
        }

        public Task<NpcDialogueReply> GenerateAsync(
            NpcDialogueContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new NpcDialogueReply(
                _fallbackText,
                "neutral",
                "idle",
                true));
        }
    }
}
