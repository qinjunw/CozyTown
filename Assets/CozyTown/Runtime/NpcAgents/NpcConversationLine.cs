namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcConversationLine
    {
        internal NpcConversationLine(string speakerId, string text, double totalMinutes)
        {
            SpeakerId = speakerId;
            Text = text;
            TotalMinutes = totalMinutes;
        }

        public string SpeakerId { get; }
        public string Text { get; }
        public double TotalMinutes { get; }
    }
}
