using CozyTown.Runtime.NpcAgents;

namespace CozyTown.Runtime.Content
{
    public static class DefaultNpcMeetingPlans
    {
        public static NpcMeetingPlan[] Create() => new[] {
            new NpcMeetingPlan("ren-sora-pond-walk", DefaultMvpIds.Npcs.Fisher, DefaultMvpIds.Npcs.Cook,
                "pond-walk", "rest.fisher_ren", "road.west_lane", 735, 780, 795) };
    }
}
