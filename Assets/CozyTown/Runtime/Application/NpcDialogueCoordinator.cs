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
        private readonly Dictionary<string, NpcDefinition> _npcs;
        private readonly INpcDialogueGenerator _generator;
        private readonly Func<GameClockSnapshot> _clockSnapshotProvider;

        public NpcDialogueCoordinator(
            IEnumerable<NpcDefinition> npcs,
            INpcDialogueGenerator generator,
            Func<GameClockSnapshot> clockSnapshotProvider)
        {
            _npcs = (npcs ?? Array.Empty<NpcDefinition>())
                .Where(npc => npc != null && !string.IsNullOrWhiteSpace(npc.Id))
                .GroupBy(npc => npc.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _clockSnapshotProvider = clockSnapshotProvider
                ?? throw new ArgumentNullException(nameof(clockSnapshotProvider));
            NpcDialogueOption[] options = _npcs.Values
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
            if (string.IsNullOrWhiteSpace(npcId)
                || !_npcs.TryGetValue(npcId, out NpcDefinition npc))
            {
                throw new ArgumentException("NPC ID is not configured.", nameof(npcId));
            }

            cancellationToken.ThrowIfCancellationRequested();
            GameClockSnapshot clock = _clockSnapshotProvider();
            var context = new NpcDialogueContext(
                npc.Id,
                npc.DisplayName,
                npc.Persona,
                clock.Day,
                clock.MinuteOfDay,
                affinity: 0,
                recentActivities: Array.Empty<string>(),
                memories: Array.Empty<string>());
            NpcDialogueReply reply = await _generator.GenerateAsync(context, cancellationToken);
            if (reply == null)
            {
                reply = new NpcDialogueReply(
                    npc.FallbackDialogue,
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
