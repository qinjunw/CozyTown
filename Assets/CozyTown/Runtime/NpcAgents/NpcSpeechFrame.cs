namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcSpeechFrame
    {
        public NpcSpeechFrame(string intent, string factId, string tone)
        {
            Intent = intent;
            FactId = factId;
            Tone = tone;
        }

        public string Intent { get; }
        public string FactId { get; }
        public string Tone { get; }
    }
}
