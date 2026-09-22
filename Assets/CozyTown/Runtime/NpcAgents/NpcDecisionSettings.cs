using System;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcDecisionSettings
    {
        public NpcDecisionSettings(int maxRequestsPerMinute = 8, int maxConcurrentRequests = 2,
            double residentCooldownSeconds = 30, double requestTimeoutSeconds = 8,
            double decisionTimeoutSeconds = 12, int maxCallsPerDecision = 2,
            double opportunityLifetimeGameMinutes = 30)
        {
            if (maxRequestsPerMinute < 1) throw new ArgumentOutOfRangeException(nameof(maxRequestsPerMinute));
            if (maxConcurrentRequests < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrentRequests));
            if (maxCallsPerDecision < 1) throw new ArgumentOutOfRangeException(nameof(maxCallsPerDecision));
            RequirePositive(residentCooldownSeconds, nameof(residentCooldownSeconds));
            RequirePositive(requestTimeoutSeconds, nameof(requestTimeoutSeconds));
            RequirePositive(decisionTimeoutSeconds, nameof(decisionTimeoutSeconds));
            RequirePositive(opportunityLifetimeGameMinutes, nameof(opportunityLifetimeGameMinutes));
            MaxRequestsPerMinute = maxRequestsPerMinute;
            MaxConcurrentRequests = maxConcurrentRequests;
            ResidentCooldownSeconds = residentCooldownSeconds;
            RequestTimeoutSeconds = requestTimeoutSeconds;
            DecisionTimeoutSeconds = decisionTimeoutSeconds;
            MaxCallsPerDecision = maxCallsPerDecision;
            OpportunityLifetimeGameMinutes = opportunityLifetimeGameMinutes;
        }

        public int MaxRequestsPerMinute { get; }
        public int MaxConcurrentRequests { get; }
        public double ResidentCooldownSeconds { get; }
        public double RequestTimeoutSeconds { get; }
        public double DecisionTimeoutSeconds { get; }
        public int MaxCallsPerDecision { get; }
        public double OpportunityLifetimeGameMinutes { get; }

        private static void RequirePositive(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
