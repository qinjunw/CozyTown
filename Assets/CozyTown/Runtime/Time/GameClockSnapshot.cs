using System;

namespace CozyTown.Runtime.Time
{
    [Serializable]
    public readonly struct GameClockSnapshot
    {
        public GameClockSnapshot(int day, int minuteOfDay)
        {
            Day = day;
            MinuteOfDay = minuteOfDay;
        }

        public int Day { get; }

        public int MinuteOfDay { get; }
    }
}
