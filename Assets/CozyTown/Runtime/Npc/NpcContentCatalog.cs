using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Npc
{
    public sealed class NpcContentCatalog
    {
        private readonly ReadOnlyCollection<NpcDefinition> _definitions;
        private readonly Dictionary<string, NpcDefinition> _definitionsById;
        private readonly string _defaultFallback;

        private NpcContentCatalog(
            string defaultFallback,
            NpcDefinition[] definitions)
        {
            _defaultFallback = defaultFallback;
            _definitions = Array.AsReadOnly((NpcDefinition[])definitions.Clone());
            _definitionsById = definitions.ToDictionary(
                definition => definition.Id,
                StringComparer.Ordinal);
        }

        public IReadOnlyList<NpcDefinition> Definitions => _definitions;

        public static OperationResult<NpcContentCatalog> Create(
            string defaultFallback,
            IEnumerable<NpcDefinition> definitions)
        {
            if (string.IsNullOrWhiteSpace(defaultFallback))
            {
                return OperationResult<NpcContentCatalog>.Failure(
                    "content.configuration_invalid");
            }

            NpcDefinition[] copy = definitions == null
                ? Array.Empty<NpcDefinition>()
                : definitions.ToArray();
            if (copy.Any(definition =>
                    definition == null
                    || string.IsNullOrWhiteSpace(definition.Id)
                    || string.IsNullOrWhiteSpace(definition.DisplayName)
                    || string.IsNullOrWhiteSpace(definition.Persona)
                    || string.IsNullOrWhiteSpace(definition.FallbackDialogue)))
            {
                return OperationResult<NpcContentCatalog>.Failure("content.npc_invalid");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (copy.Any(definition => !ids.Add(definition.Id)))
            {
                return OperationResult<NpcContentCatalog>.Failure(
                    "content.npc_id_duplicate");
            }

            return OperationResult<NpcContentCatalog>.Success(
                new NpcContentCatalog(defaultFallback, copy));
        }

        public bool TryGetDefinition(string npcId, out NpcDefinition definition)
        {
            return _definitionsById.TryGetValue(npcId ?? string.Empty, out definition);
        }

        public NpcDialogueContext CreateDialogueContext(
            string npcId,
            int day,
            int minuteOfDay)
        {
            if (!TryGetDefinition(npcId, out NpcDefinition definition))
            {
                throw new ArgumentException("NPC ID is not configured.", nameof(npcId));
            }

            return new NpcDialogueContext(
                definition.Id,
                definition.DisplayName,
                definition.Persona,
                day,
                minuteOfDay,
                affinity: 0,
                recentActivities: Array.Empty<string>(),
                memories: Array.Empty<string>());
        }

        public string ResolveFallback(string npcId)
        {
            return TryGetDefinition(npcId, out NpcDefinition definition)
                ? definition.FallbackDialogue
                : _defaultFallback;
        }
    }
}
