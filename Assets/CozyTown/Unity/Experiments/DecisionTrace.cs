using System;
using System.Collections.Generic;

namespace CozyTown.Unity.Experiments
{
    [Serializable]
    public sealed class DecisionTrace
    {
        public int schemaVersion = 1;
        public string configuration, clientConfiguration;
        public bool completed;
        public List<DecisionTraceTick> ticks = new List<DecisionTraceTick>();
        public List<DecisionTraceCall> calls = new List<DecisionTraceCall>();
    }

    [Serializable]
    public sealed class DecisionTraceTick
    {
        public int tick;
        public double realSeconds, gameMinutes;
    }

    [Serializable]
    public sealed class DecisionTraceCall
    {
        public int callOrdinal, dispatchTick, deliveryTick = -1, completionOrdinal = -1, cancellationTick = -1;
        public double dispatchRealSeconds, deliveryRealSeconds, completedRealSeconds;
        public bool hasCompletedRealSeconds;
        public string requestJson, normalizedRequest, responseJson, rawResponseJson, completionKind, candidateErrorCode;
        public string replayRequestJson, replayResponseJson;
        public string requestedModel, returnedModel, usageJson;
    }
}
