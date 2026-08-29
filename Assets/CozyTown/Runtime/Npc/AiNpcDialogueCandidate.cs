using System;

namespace CozyTown.Runtime.Npc
{
    [Serializable]
    public sealed class AiNpcDialogueCandidate
    {
        public AiNpcDialogueCandidate(
            string text,
            string emotionTag,
            string actionTag)
        {
            Text = text;
            EmotionTag = emotionTag;
            ActionTag = actionTag;
        }

        public string Text { get; }

        public string EmotionTag { get; }

        public string ActionTag { get; }

    }
}
