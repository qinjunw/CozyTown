using System;
using System.Linq;
using System.Text;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    public sealed class ProxyNpcDecisionJsonCodec
    {
        public const int MaximumRequestBytes = 32768;
        public const int MaximumResponseBytes = 16384;

        public string SerializeRequest(NpcDecisionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string json = JsonUtility.ToJson(new RequestPayload(request));
            if (Encoding.UTF8.GetByteCount(json) > MaximumRequestBytes)
                throw new ArgumentException("Decision context exceeds the 32 KiB request limit.", nameof(request));
            return json;
        }

        public NpcDecisionReply ParseResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumResponseBytes)
                throw new FormatException("Decision response must be JSON within the 16 KiB response limit.");
            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
                throw new FormatException("Decision response must be a JSON object.");
            ResponsePayload payload;
            try { payload = JsonUtility.FromJson<ResponsePayload>(trimmed); }
            catch (ArgumentException exception) { throw new FormatException("Decision response contains invalid JSON.", exception); }
            if (payload == null || payload.schemaVersion != 1)
                throw new FormatException("Decision response requires schemaVersion 1.");
            if (payload.operation == "wait") return new NpcDecisionReply(NpcDecisionKind.Wait);
            if (payload.operation == "inspect_location" && !string.IsNullOrWhiteSpace(payload.locationId))
                return new NpcDecisionReply(NpcDecisionKind.InspectLocation, payload.locationId);
            if (payload.operation == "visit" && !string.IsNullOrWhiteSpace(payload.locationId)
                && (payload.activity == "working" || payload.activity == "resting"))
                return new NpcDecisionReply(NpcDecisionKind.Visit, payload.locationId,
                    payload.activity == "working" ? NpcActivity.Working : NpcActivity.Resting, payload.durationGameMinutes);
            throw new FormatException("Decision response requires a supported operation and its candidate fields.");
        }

        [Serializable]
        private sealed class ResponsePayload
        {
            public int schemaVersion;
            public string operation;
            public string locationId;
            public string activity;
            public double durationGameMinutes;
        }

        [Serializable]
        private sealed class RequestPayload
        {
            public int schemaVersion = 1;
            public string decisionId;
            public string npcId;
            public string displayName;
            public string persona;
            public string worldRunId;
            public long revision;
            public double gameTotalMinutes;
            public string targetLocationId;
            public string activity;
            public int step;
            public int remainingCalls;
            public double maxActivityDurationGameMinutes = NpcAgentWorld.MaximumActivityDurationGameMinutes;
            public string[] allowedOperations = { "wait", "inspect_location", "visit" };
            public string[] allowedActivities = { "working", "resting" };
            public EventPayload[] triggers;
            public string[] knownLocationIds;
            public bool hasLocationDetails;
            public LocationPayload locationDetails;
            public string previousResultCode;

            public RequestPayload(NpcDecisionRequest request)
            {
                decisionId = request.DecisionId.ToString("N");
                npcId = request.NpcId;
                displayName = request.DisplayName;
                persona = request.Persona;
                worldRunId = request.Self.WorldRunId.ToString("N");
                revision = request.Self.Revision;
                gameTotalMinutes = request.GameTotalMinutes;
                targetLocationId = request.Self.Target.TargetLocationId;
                activity = request.Self.Target.ExpectedActivity.ToString().ToLowerInvariant();
                step = request.Step;
                remainingCalls = request.MaxCalls - request.Step;
                triggers = request.Triggers.Select(item => new EventPayload
                    { kind = item.Kind.ToString(), gameTotalMinutes = item.TotalMinutes }).ToArray();
                knownLocationIds = request.KnownLocationIds.ToArray();
                hasLocationDetails = request.LocationDetails != null;
                if (request.LocationDetails != null)
                    locationDetails = new LocationPayload { locationId = request.LocationDetails.LocationId,
                        isReachable = request.LocationDetails.IsReachable };
                previousResultCode = request.PreviousResultCode;
            }
        }

        [Serializable]
        private sealed class EventPayload
        {
            public string kind;
            public double gameTotalMinutes;
        }

        [Serializable]
        private sealed class LocationPayload
        {
            public string locationId;
            public bool isReachable;
        }
    }
}
