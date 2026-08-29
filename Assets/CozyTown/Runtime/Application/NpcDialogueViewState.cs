using System;
using CozyTown.Runtime.Npc;

namespace CozyTown.Runtime.Application
{
    [Serializable]
    public sealed class NpcDialogueOption
    {
        public NpcDialogueOption(string npcId, string displayName)
        {
            NpcId = npcId;
            DisplayName = displayName;
        }

        public string NpcId { get; }

        public string DisplayName { get; }
    }

    [Serializable]
    public sealed class NpcDialogueViewState
    {
        public NpcDialogueViewState(
            string npcId,
            string displayName,
            string text,
            string emotionTag,
            string actionTag,
            bool isFallback,
            string correlationId,
            NpcDialogueFallbackReason fallbackReason)
        {
            NpcId = npcId;
            DisplayName = displayName;
            Text = text;
            EmotionTag = emotionTag;
            ActionTag = actionTag;
            IsFallback = isFallback;
            CorrelationId = correlationId ?? string.Empty;
            FallbackReason = fallbackReason;
        }

        public string NpcId { get; }

        public string DisplayName { get; }

        public string Text { get; }

        public string EmotionTag { get; }

        public string ActionTag { get; }

        public bool IsFallback { get; }

        public string CorrelationId { get; }

        public NpcDialogueFallbackReason FallbackReason { get; }
    }
}
