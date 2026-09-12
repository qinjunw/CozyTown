using System;
using CozyTown.Runtime.NpcLife;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcActivityRequest
    {
        public NpcActivityRequest(string npcId, Guid worldRunId, long expectedRevision,
            string targetLocationId, NpcActivity activity, double expiresAtTotalMinutes)
        {
            NpcId = npcId;
            WorldRunId = worldRunId;
            ExpectedRevision = expectedRevision;
            TargetLocationId = targetLocationId;
            Activity = activity;
            ExpiresAtTotalMinutes = expiresAtTotalMinutes;
        }

        public string NpcId { get; }
        public Guid WorldRunId { get; }
        public long ExpectedRevision { get; }
        public string TargetLocationId { get; }
        public NpcActivity Activity { get; }
        public double ExpiresAtTotalMinutes { get; }
    }
}
