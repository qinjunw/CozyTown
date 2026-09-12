using System;
using System.Collections.Generic;
using System.Linq;

namespace CozyTown.Runtime.NpcAgents
{
    public enum NpcSocialContextKind { Opportunity, Invitation, Conversation }

    public sealed class NpcSocialContext
    {
        internal NpcSocialContext(NpcSocialContextKind kind, NpcMeetingPlan plan, string npcId, Guid meetingId,
            double startsAt, double deadline, IEnumerable<NpcConversationLine> transcript, IEnumerable<NpcMeetingMemory> memories)
        {
            Kind = kind;
            PlanId = plan.Id;
            PartnerId = npcId == plan.InitiatorId ? plan.PartnerId : plan.InitiatorId;
            PlaceId = plan.PlaceId;
            LocationId = npcId == plan.InitiatorId ? plan.InitiatorLocationId : plan.PartnerLocationId;
            MeetingId = meetingId;
            StartsAtTotalMinutes = startsAt;
            DeadlineTotalMinutes = deadline;
            MaxTurns = plan.MaxTurns;
            Transcript = Array.AsReadOnly(transcript.ToArray());
            Memories = Array.AsReadOnly(memories.ToArray());
        }

        public NpcSocialContextKind Kind { get; }
        public string PlanId { get; }
        public string PartnerId { get; }
        public string PlaceId { get; }
        public string LocationId { get; }
        public Guid MeetingId { get; }
        public double StartsAtTotalMinutes { get; }
        public double DeadlineTotalMinutes { get; }
        public int MaxTurns { get; }
        public IReadOnlyList<NpcConversationLine> Transcript { get; }
        public IReadOnlyList<NpcMeetingMemory> Memories { get; }
    }
}
