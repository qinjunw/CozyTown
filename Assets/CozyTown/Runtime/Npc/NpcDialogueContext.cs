using System;

namespace CozyTown.Runtime.Npc
{
    [Serializable]
    public sealed class NpcDialogueContext
    {
        private readonly string[] _recentActivities;
        private readonly string[] _memories;

        public NpcDialogueContext(
            string npcId,
            string displayName,
            string persona,
            int day,
            int minuteOfDay,
            int affinity,
            string[] recentActivities,
            string[] memories)
        {
            NpcId = npcId;
            DisplayName = displayName;
            Persona = persona;
            Day = day;
            MinuteOfDay = minuteOfDay;
            Affinity = affinity;
            _recentActivities = recentActivities == null
                ? Array.Empty<string>()
                : (string[])recentActivities.Clone();
            _memories = memories == null
                ? Array.Empty<string>()
                : (string[])memories.Clone();
        }

        public string NpcId { get; }

        public string DisplayName { get; }

        public string Persona { get; }

        public int Day { get; }

        public int MinuteOfDay { get; }

        public int Affinity { get; }

        public string[] RecentActivities => (string[])_recentActivities.Clone();

        public string[] Memories => (string[])_memories.Clone();
    }

    [Serializable]
    public sealed class NpcDialogueReply
    {
        public NpcDialogueReply(string text, string emotionTag, string actionTag, bool isFallback)
            : this(
                text,
                emotionTag,
                actionTag,
                isFallback,
                string.Empty,
                NpcDialogueFallbackReason.None)
        {
        }

        public NpcDialogueReply(
            string text,
            string emotionTag,
            string actionTag,
            bool isFallback,
            string correlationId,
            NpcDialogueFallbackReason fallbackReason)
        {
            Text = text;
            EmotionTag = emotionTag;
            ActionTag = actionTag;
            IsFallback = isFallback;
            CorrelationId = correlationId ?? string.Empty;
            FallbackReason = fallbackReason;
        }

        public string Text { get; }

        public string EmotionTag { get; }

        public string ActionTag { get; }

        public bool IsFallback { get; }

        public string CorrelationId { get; }

        public NpcDialogueFallbackReason FallbackReason { get; }
    }
}
