using System;
using System.Collections.Generic;
using System.Linq;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcDecisionOutcome
    {
        internal NpcDecisionOutcome(NpcDecisionRequest request, string code, int calls, double startedAt, double finishedAt,
            NpcDecisionReply reply, IEnumerable<string> candidateErrorCodes, NpcLocalObservation executionObservation = null)
        {
            NpcId = request.NpcId;
            DecisionId = request.DecisionId;
            WorldRunId = request.Self.WorldRunId;
            Code = code;
            Calls = calls;
            StartedAtSeconds = startedAt;
            FinishedAtSeconds = finishedAt;
            Context = request;
            Reply = reply;
            ExecutionObservation = executionObservation;
            CandidateErrorCodes = Array.AsReadOnly(candidateErrorCodes.ToArray());
        }

        public string NpcId { get; }
        public Guid DecisionId { get; }
        public Guid WorldRunId { get; }
        public string Code { get; }
        public int Calls { get; }
        public double StartedAtSeconds { get; }
        public double FinishedAtSeconds { get; }
        public bool ActivityAccepted => Code == "agent.activity_accepted";
        public NpcDecisionRequest Context { get; }
        public NpcDecisionReply Reply { get; }
        public NpcLocalObservation ExecutionObservation { get; }
        public IReadOnlyList<string> CandidateErrorCodes { get; }
    }
}
