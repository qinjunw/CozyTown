using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CozyTown.Runtime.Npc
{
    public sealed class ConfiguredFallbackDialogueGenerator : INpcDialogueGenerator
    {
        private readonly Dictionary<string, NpcDefinition> _npcs;
        private readonly string _defaultFallback;

        public ConfiguredFallbackDialogueGenerator(
            IEnumerable<NpcDefinition> npcs,
            string defaultFallback)
        {
            if (string.IsNullOrWhiteSpace(defaultFallback))
            {
                throw new ArgumentException(
                    "Default fallback dialogue must not be empty.",
                    nameof(defaultFallback));
            }

            _npcs = (npcs ?? Array.Empty<NpcDefinition>())
                .Where(npc => npc != null && !string.IsNullOrWhiteSpace(npc.Id))
                .GroupBy(npc => npc.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            _defaultFallback = defaultFallback;
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
            string text = _npcs.TryGetValue(context.NpcId ?? string.Empty, out NpcDefinition npc)
                && !string.IsNullOrWhiteSpace(npc.FallbackDialogue)
                    ? npc.FallbackDialogue
                    : _defaultFallback;
            return Task.FromResult(new NpcDialogueReply(
                text,
                "neutral",
                "idle",
                true));
        }
    }
}
