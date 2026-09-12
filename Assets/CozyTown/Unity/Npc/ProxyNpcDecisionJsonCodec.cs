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
            if (payload == null || (payload.schemaVersion != 1 && payload.schemaVersion != 2 && payload.schemaVersion != 3))
                throw new FormatException("Decision response requires schemaVersion 1, 2 or 3.");
            if (payload.schemaVersion >= 2)
            {
                if (payload.operation == "invite" && !string.IsNullOrWhiteSpace(payload.planId))
                    return new NpcDecisionReply(NpcDecisionKind.Invite, planId: payload.planId);
                if (Guid.TryParse(payload.meetingId, out var meetingId) && meetingId != Guid.Empty)
                {
                    if (payload.schemaVersion == 3 && payload.operation == "deliver") return new NpcDecisionReply(NpcDecisionKind.Deliver, meetingId: meetingId);
                    if (payload.schemaVersion == 3 && payload.operation == "cancel_exchange") return new NpcDecisionReply(NpcDecisionKind.CancelExchange, meetingId: meetingId);
                    if (payload.operation == "accept_invite") return new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: meetingId);
                    if (payload.operation == "decline_invite") return new NpcDecisionReply(NpcDecisionKind.DeclineInvitation, meetingId: meetingId);
                    if (payload.operation == "end_conversation") return new NpcDecisionReply(NpcDecisionKind.EndConversation, meetingId: meetingId);
                    if (payload.operation == "say" && !string.IsNullOrWhiteSpace(payload.text) && payload.text.Length <= 240)
                        return new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: meetingId, text: payload.text);
                }
            }
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
            public string planId;
            public string meetingId;
            public string text;
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
            public SocialPayload social;

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
                allowedOperations = request.AllowedOperations.ToArray();
                if (request.Social != null)
                {
                    schemaVersion = request.Social.Resources == null ? 2 : 3;
                    social = new SocialPayload(request.Social);
                    allowedActivities = Array.Empty<string>();
                    knownLocationIds = Array.Empty<string>();
                }
            }
        }

        [Serializable]
        private sealed class SocialPayload
        {
            public ResourcePayload resources;
            public string kind, planId, partnerId, placeId, locationId, meetingId;
            public double startsAtTotalMinutes, deadlineTotalMinutes;
            public int maxTurns;
            public LinePayload[] transcript;
            public MemoryPayload[] memories;
            public SocialPayload(NpcSocialContext context)
            {
                kind = context.Kind.ToString().ToLowerInvariant();
                if (context.Resources != null) resources = new ResourcePayload {
                    sellerId = context.Resources.Terms.SellerId, buyerId = context.Resources.Terms.BuyerId,
                    itemId = context.Resources.Terms.ItemId, quantity = context.Resources.Terms.Quantity,
                    totalPrice = context.Resources.Terms.TotalPrice, ownedQuantity = context.Resources.OwnedQuantity,
                    balance = context.Resources.Balance, deliveryResultCode = context.DeliveryResultCode };
                planId = context.PlanId; partnerId = context.PartnerId; placeId = context.PlaceId;
                locationId = context.LocationId;
                meetingId = context.MeetingId == Guid.Empty ? null : context.MeetingId.ToString("N");
                startsAtTotalMinutes = context.StartsAtTotalMinutes; deadlineTotalMinutes = context.DeadlineTotalMinutes;
                maxTurns = context.MaxTurns;
                transcript = context.Transcript.Select(item => new LinePayload { speakerId = item.SpeakerId, text = item.Text,
                    gameTotalMinutes = item.TotalMinutes }).ToArray();
                memories = context.Memories.Select(item => new MemoryPayload { kind = item.Kind, partnerId = item.PartnerId,
                    speakerId = item.SpeakerId, text = item.Text, gameTotalMinutes = item.TotalMinutes }).ToArray();
            }
        }

        [Serializable]
        private sealed class ResourcePayload
        {
            public string sellerId, buyerId, itemId, deliveryResultCode;
            public int quantity, totalPrice, ownedQuantity, balance;
        }

        [Serializable]
        private sealed class LinePayload
        {
            public string speakerId, text;
            public double gameTotalMinutes;
        }

        [Serializable]
        private sealed class MemoryPayload
        {
            public string kind, partnerId, speakerId, text;
            public double gameTotalMinutes;
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
