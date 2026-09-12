using CozyTown.Runtime.Economy;
using CozyTown.Runtime.NpcAgents;

namespace CozyTown.Runtime.Content
{
    public static class DefaultNpcResourcePlans
    {
        public static NpcMeetingPlan[] Create() => new[] {
            new NpcMeetingPlan("sora-ren-fish-supply", DefaultMvpIds.Npcs.Cook, DefaultMvpIds.Npcs.Fisher,
                "pond-walk", "road.west_lane", "rest.fisher_ren", 735, 780, 795,
                resourceTerms: new CharacterTradeTerms(DefaultMvpIds.Npcs.Fisher, DefaultMvpIds.Npcs.Cook, DefaultMvpIds.Items.Carp, 1, 25)) };
    }
}
