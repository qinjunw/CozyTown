namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcObservationFact
    {
        internal NpcObservationFact(string factId, string entityId, string predicate, string value, string valueType,
            string unit, string knowledge, string source, string observerId, double observedAt, string scope,
            bool canExpress = true, string speakerId = null)
        {
            FactId = factId; EntityId = entityId; Predicate = predicate; Value = value; ValueType = valueType;
            Unit = unit; Knowledge = knowledge; Source = source; ObserverId = observerId;
            ObservedAtTotalMinutes = observedAt; Scope = scope; CanExpress = canExpress; SpeakerId = speakerId;
        }
        public string FactId { get; }
        public string EntityId { get; }
        public string Predicate { get; }
        public string Value { get; }
        public string ValueType { get; }
        public string Unit { get; }
        public string Knowledge { get; }
        public string Source { get; }
        public string ObserverId { get; }
        public double ObservedAtTotalMinutes { get; }
        public string Scope { get; }
        public bool CanExpress { get; }
        public string SpeakerId { get; }
    }
}
