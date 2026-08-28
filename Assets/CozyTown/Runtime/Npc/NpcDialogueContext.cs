using System;

namespace CozyTown.Runtime.Npc
{
    [Serializable]
    public sealed class NpcDialogueContext
    {
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
            RecentActivities = recentActivities ?? Array.Empty<string>();
            Memories = memories ?? Array.Empty<string>();
        }

        public string NpcId { get; }

        public string DisplayName { get; }

        public string Persona { get; }

        public int Day { get; }

        public int MinuteOfDay { get; }

        public int Affinity { get; }

        public string[] RecentActivities { get; }

        public string[] Memories { get; }
    }

    [Serializable]
    public sealed class NpcDialogueReply
    {
        public NpcDialogueReply(string text, string emotionTag, string actionTag, bool isFallback)
        {
            Text = text;
            EmotionTag = emotionTag;
            ActionTag = actionTag;
            IsFallback = isFallback;
        }

        public string Text { get; }

        public string EmotionTag { get; }

        public string ActionTag { get; }

        public bool IsFallback { get; }
    }
}
