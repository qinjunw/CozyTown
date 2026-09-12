using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Npc;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcDecisionRequest
    {
        internal NpcDecisionRequest(NpcDefinition profile, NpcAgentSnapshot self,
            double gameTotalMinutes, IEnumerable<NpcAgentEvent> triggers, IEnumerable<string> knownLocationIds,
            int maxCalls, string previousResultCode, NpcSocialContext social = null)
        {
            NpcId = profile.Id;
            DisplayName = profile.DisplayName;
            Persona = profile.Persona;
            Self = self;
            GameTotalMinutes = gameTotalMinutes;
            Triggers = Array.AsReadOnly(triggers.ToArray());
            DecisionId = Guid.NewGuid();
            KnownLocationIds = Array.AsReadOnly(knownLocationIds.ToArray());
            Step = 1;
            MaxCalls = maxCalls;
            PreviousResultCode = previousResultCode;
            Social = social;
        }

        internal NpcDecisionRequest(NpcDecisionRequest previous, NpcLocationDetails details)
        {
            NpcId = previous.NpcId;
            DisplayName = previous.DisplayName;
            Persona = previous.Persona;
            Self = previous.Self;
            GameTotalMinutes = previous.GameTotalMinutes;
            Triggers = previous.Triggers;
            DecisionId = previous.DecisionId;
            KnownLocationIds = previous.KnownLocationIds;
            LocationDetails = details;
            Step = previous.Step + 1;
            MaxCalls = previous.MaxCalls;
            PreviousResultCode = previous.PreviousResultCode;
            Social = previous.Social;
        }

        public string NpcId { get; }
        public string DisplayName { get; }
        public string Persona { get; }
        public NpcAgentSnapshot Self { get; }
        public double GameTotalMinutes { get; }
        public IReadOnlyList<NpcAgentEvent> Triggers { get; }
        public Guid DecisionId { get; }
        public IReadOnlyList<string> KnownLocationIds { get; }
        public NpcLocationDetails LocationDetails { get; }
        public int Step { get; }
        public int MaxCalls { get; }
        public string PreviousResultCode { get; }
        public NpcSocialContext Social { get; }
        public IReadOnlyList<string> AllowedOperations => Array.AsReadOnly(Social == null
            ? new[] { "wait", "inspect_location", "visit" }
            : Social.Kind == NpcSocialContextKind.Opportunity ? new[] { "invite", "wait" }
            : Social.Kind == NpcSocialContextKind.Invitation ? new[] { "accept_invite", "decline_invite" }
            : Social.Kind == NpcSocialContextKind.Delivery ? new[] { "deliver", "cancel_exchange" }
            : Social.Transcript.Count >= 2 ? new[] { "say", "end_conversation" } : new[] { "say" });
    }
}
