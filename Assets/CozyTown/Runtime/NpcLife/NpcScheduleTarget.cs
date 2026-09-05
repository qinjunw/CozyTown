namespace CozyTown.Runtime.NpcLife
{
    public sealed class NpcScheduleTarget
    {
        public NpcScheduleTarget(string targetLocationId, NpcActivity expectedActivity)
        {
            TargetLocationId = targetLocationId;
            ExpectedActivity = expectedActivity;
        }

        public string TargetLocationId { get; }

        public NpcActivity ExpectedActivity { get; }
    }
}
