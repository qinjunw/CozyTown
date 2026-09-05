using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public sealed class NpcDialogueCoordinator : INpcDialogueCoordinator
    {
        private readonly NpcContentCatalog _npcContent;
        private readonly INpcDialogueGenerator _generator;
        private readonly Func<GameClockSnapshot> _clockSnapshotProvider;

        public NpcDialogueCoordinator(
            NpcContentCatalog npcContent,
            INpcDialogueGenerator generator,
            Func<GameClockSnapshot> clockSnapshotProvider)
        {
            _npcContent = npcContent ?? throw new ArgumentNullException(nameof(npcContent));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _clockSnapshotProvider = clockSnapshotProvider
                ?? throw new ArgumentNullException(nameof(clockSnapshotProvider));
            NpcDialogueOption[] options = _npcContent.Definitions
                .Select(npc => new NpcDialogueOption(npc.Id, npc.DisplayName))
                .OrderBy(option => option.NpcId, StringComparer.Ordinal)
                .ToArray();
            Npcs = Array.AsReadOnly(options);
        }

        public IReadOnlyList<NpcDialogueOption> Npcs { get; }

        public async Task<NpcDialogueViewState> GenerateAsync(
            string npcId,
            CancellationToken cancellationToken)
        {
            if (!_npcContent.TryGetDefinition(npcId, out NpcDefinition npc))
            {
                throw new ArgumentException("NPC ID is not configured.", nameof(npcId));
            }

            cancellationToken.ThrowIfCancellationRequested();
            GameClockSnapshot clock = _clockSnapshotProvider();
            NpcDialogueContext context = _npcContent.CreateDialogueContext(
                npc.Id,
                clock.Day,
                clock.MinuteOfDay);
            NpcDialogueReply reply = await _generator.GenerateAsync(context, cancellationToken);
            if (reply == null)
            {
                reply = new NpcDialogueReply(
                    _npcContent.ResolveFallback(npc.Id),
                    "neutral",
                    "idle",
                    true,
                    Guid.NewGuid().ToString("N"),
                    NpcDialogueFallbackReason.EmptyResponse);
            }

            return new NpcDialogueViewState(
                npc.Id,
                npc.DisplayName,
                reply.Text,
                reply.EmotionTag,
                reply.ActionTag,
                reply.IsFallback,
                reply.CorrelationId,
                reply.FallbackReason);
        }
    }
}
