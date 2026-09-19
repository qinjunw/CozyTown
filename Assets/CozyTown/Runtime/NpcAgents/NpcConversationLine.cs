using System.Runtime.Serialization;

namespace CozyTown.Runtime.NpcAgents
{
    [DataContract]
    public sealed class NpcConversationLine
    {
        internal NpcConversationLine(string speakerId, string text, double totalMinutes)
        {
            SpeakerId = speakerId;
            Text = text;
            TotalMinutes = totalMinutes;
        }

        [field: DataMember(Name = "speakerId", IsRequired = true)]
        public string SpeakerId { get; }
        [field: DataMember(Name = "text", IsRequired = true)]
        public string Text { get; }
        [field: DataMember(Name = "totalMinutes", IsRequired = true)]
        public double TotalMinutes { get; }
    }
}
