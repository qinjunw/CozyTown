namespace CozyTown.Runtime.Time
{
    internal static class DailySettlementSchedule
    {
        internal const int SettlementMinuteOfDay = 300;

        internal static bool IsValidProgress(GameClockSnapshot clock, int completedDay)
        {
            if (clock.Day < 1
                || clock.MinuteOfDay < 0
                || clock.MinuteOfDay >= InMemoryTimeService.MinutesPerDay
                || completedDay < 1)
            {
                return false;
            }

            if (completedDay == clock.Day)
            {
                return true;
            }

            return clock.MinuteOfDay < SettlementMinuteOfDay
                && completedDay == clock.Day - 1;
        }
    }
}
