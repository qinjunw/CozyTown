using System;
using System.Collections.Generic;

namespace CozyTown.Runtime.Npc
{
    [Serializable]
    public sealed class NpcDialogueRequest
    {
        public NpcDialogueRequest(NpcDialogueContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            NpcId = context.NpcId;
            DisplayName = context.DisplayName;
            Persona = context.Persona;
            Day = context.Day;
            MinuteOfDay = context.MinuteOfDay;
            Affinity = context.Affinity;
            RecentActivities = Copy(context.RecentActivities);
            Memories = Copy(context.Memories);
        }

        public string NpcId { get; }

        public string DisplayName { get; }

        public string Persona { get; }

        public int Day { get; }

        public int MinuteOfDay { get; }

        public int Affinity { get; }

        public IReadOnlyList<string> RecentActivities { get; }

        public IReadOnlyList<string> Memories { get; }

        private static IReadOnlyList<string> Copy(IEnumerable<string> source)
        {
            string[] copy = source == null
                ? Array.Empty<string>()
                : new List<string>(source).ToArray();
            return Array.AsReadOnly(copy);
        }
    }
}
