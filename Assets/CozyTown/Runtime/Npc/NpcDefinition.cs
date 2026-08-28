using System;

namespace CozyTown.Runtime.Npc
{
    [Serializable]
    public sealed class NpcDefinition
    {
        public NpcDefinition(
            string id,
            string displayName,
            string persona,
            string fallbackDialogue)
        {
            Id = id;
            DisplayName = displayName;
            Persona = persona;
            FallbackDialogue = fallbackDialogue;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Persona { get; }

        public string FallbackDialogue { get; }
    }
}
