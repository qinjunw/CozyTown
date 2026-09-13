using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
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

        public NpcDecisionReply ParseResponse(string json, NpcDecisionRequest request = null)
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumResponseBytes)
                throw new FormatException("Decision response must be JSON within the 16 KiB response limit.");
            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
                throw new FormatException("Decision response must be a JSON object.");
            var fields = ReadCandidateFields(trimmed);
            if (!fields.TryGetValue("schemaVersion", out var version) || version.Type != "number"
                || !int.TryParse(version.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int schemaVersion)
                || schemaVersion < 1 || schemaVersion > 4)
                throw new NpcCandidateException("candidate.schema_mismatch");
            if (request != null && schemaVersion != (request.Social == null ? 1 : request.Social.Resources == null ? 2 : 4))
                throw new NpcCandidateException("candidate.schema_mismatch");
            string operation = RequireString(fields, "operation", "candidate.operation_unavailable");
            if (request != null && !request.AllowedOperations.Contains(operation))
                throw new NpcCandidateException("candidate.operation_unavailable");
            if (operation == "wait") return new NpcDecisionReply(NpcDecisionKind.Wait);
            if (operation == "inspect_location" || operation == "visit")
            {
                string location = RequireString(fields, "locationId", "candidate.location_id_required");
                if (request != null && !request.KnownLocationIds.Contains(location))
                    throw new NpcCandidateException("candidate.location_unknown");
                if (operation == "inspect_location") return new NpcDecisionReply(NpcDecisionKind.InspectLocation, location);
                string activity = RequireString(fields, "activity", "candidate.activity_invalid");
                if (activity != "working" && activity != "resting") throw new NpcCandidateException("candidate.activity_invalid");
                if (!fields.TryGetValue("durationGameMinutes", out var duration) || duration.Type != "number"
                    || !double.TryParse(duration.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes)
                    || double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes <= 0 || minutes > NpcAgentWorld.MaximumActivityDurationGameMinutes)
                    throw new NpcCandidateException("candidate.duration_invalid");
                return new NpcDecisionReply(NpcDecisionKind.Visit, location,
                    activity == "working" ? NpcActivity.Working : NpcActivity.Resting, minutes);
            }
            if (schemaVersion < 2) throw new NpcCandidateException("candidate.operation_unavailable");
            if (operation == "invite")
            {
                string plan = RequireString(fields, "planId", "candidate.plan_id_required");
                if (request != null && plan != request.Social?.PlanId) throw new NpcCandidateException("candidate.plan_id_mismatch");
                return new NpcDecisionReply(NpcDecisionKind.Invite, planId: plan);
            }
            NpcDecisionKind kind;
            if (operation == "accept_invite") kind = NpcDecisionKind.AcceptInvitation;
            else if (operation == "decline_invite") kind = NpcDecisionKind.DeclineInvitation;
            else if (operation == "say") kind = NpcDecisionKind.Speak;
            else if (operation == "end_conversation") kind = NpcDecisionKind.EndConversation;
            else if (schemaVersion >= 3 && operation == "deliver") kind = NpcDecisionKind.Deliver;
            else if (schemaVersion >= 3 && operation == "cancel_exchange") kind = NpcDecisionKind.CancelExchange;
            else throw new NpcCandidateException("candidate.operation_unavailable");
            string identifier = RequireString(fields, "meetingId", "candidate.meeting_id_required");
            if (!Guid.TryParse(identifier, out var meetingId) || meetingId == Guid.Empty)
                throw new NpcCandidateException("candidate.meeting_id_invalid");
            if (request != null && meetingId != request.Social?.MeetingId) throw new NpcCandidateException("candidate.meeting_id_mismatch");
            string text = kind == NpcDecisionKind.Speak ? RequireString(fields, "text", "candidate.text_invalid") : null;
            if (text != null && text.Length > 240) throw new NpcCandidateException("candidate.text_invalid");
            return new NpcDecisionReply(kind, meetingId: meetingId, text: text);
        }

        private static string RequireString(Dictionary<string, (string Type, string Text)> fields, string name, string code)
        {
            if (!fields.TryGetValue(name, out var field) || field.Type != "string" || string.IsNullOrWhiteSpace(field.Text))
                throw new NpcCandidateException(code);
            return field.Text;
        }

        private static Dictionary<string, (string Type, string Text)> ReadCandidateFields(string json)
        {
            try
            {
                using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json),
                    new XmlDictionaryReaderQuotas { MaxDepth = 32, MaxStringContentLength = MaximumResponseBytes });
                reader.MoveToContent();
                if (reader.GetAttribute("type") != "object") throw new FormatException("Decision response must be a JSON object.");
                reader.ReadStartElement();
                var fields = new Dictionary<string, (string Type, string Text)>(StringComparer.Ordinal);
                while (reader.MoveToContent() == XmlNodeType.Element)
                {
                    string name = reader.LocalName, type = reader.GetAttribute("type"), value = null;
                    if (type == "object" || type == "array") reader.Skip();
                    else value = reader.ReadElementContentAsString();
                    if (fields.ContainsKey(name)) throw new FormatException("Decision response has a duplicate field.");
                    fields.Add(name, (type, value));
                }
                reader.ReadEndElement();
                if (reader.Read()) throw new FormatException("Decision response contains trailing content.");
                return fields;
            }
            catch (XmlException exception) { throw new FormatException("Decision response contains invalid JSON.", exception); }
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
            public string candidateErrorCode;
            public SocialPayload social;
            public bool hasSelfAssessment;
            public SelfAssessmentPayload selfAssessment;

            public RequestPayload(NpcDecisionRequest request)
            {
                decisionId = request.DecisionId.ToString("N");
                candidateErrorCode = request.CandidateErrorCode;
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
                    schemaVersion = request.Social.Resources == null ? 2 : 4;
                    social = new SocialPayload(request.Social);
                    hasSelfAssessment = request.HasSelfAssessment;
                    if (hasSelfAssessment)
                    {
                        var own = request.Social.Resources;
                        selfAssessment = new SelfAssessmentPayload {
                            role = own.Role, canMeetKnownTerms = own.CanMeetKnownTerms,
                            missingCoins = own.MissingCoins, missingQuantity = own.MissingQuantity,
                            reasonCode = own.MissingCoins > 0 ? "wallet.insufficient_funds"
                                : own.MissingQuantity > 0 ? "inventory.insufficient_quantity" : null,
                            scope = "Own payment or stock only; partner conditions, capacity and receipt overflow remain unchecked."
                        };
                    }
                    allowedActivities = Array.Empty<string>();
                    knownLocationIds = Array.Empty<string>();
                }
            }
        }

        [Serializable]
        private sealed class SelfAssessmentPayload
        {
            public string role, reasonCode, scope;
            public bool canMeetKnownTerms;
            public int missingCoins, missingQuantity;
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
