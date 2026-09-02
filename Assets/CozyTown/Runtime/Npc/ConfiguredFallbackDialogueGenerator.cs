using System;
using System.Threading;
using System.Threading.Tasks;

namespace CozyTown.Runtime.Npc
{
    public sealed class ConfiguredFallbackDialogueGenerator : INpcDialogueGenerator
    {
        private readonly NpcContentCatalog _catalog;

        public ConfiguredFallbackDialogueGenerator(NpcContentCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
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
                _catalog.ResolveFallback(context.NpcId),
                "neutral",
                "idle",
                true));
        }
    }
}
