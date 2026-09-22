using System;
using CozyTown.Runtime.NpcLife;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcAgentSnapshot
    {
        internal NpcAgentSnapshot(string npcId, Guid worldRunId, long revision, NpcScheduleTarget target,
            NpcActivityRequest activeActivity)
        {
            NpcId = npcId;
            WorldRunId = worldRunId;
            Revision = revision;
            Target = target;
            ActiveActivity = activeActivity;
        }

        public string NpcId { get; }
        public Guid WorldRunId { get; }
        public long Revision { get; }
        public NpcScheduleTarget Target { get; }
        public NpcActivityRequest ActiveActivity { get; }
    }
}
