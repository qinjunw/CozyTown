namespace CozyTown.Unity.Hud
{
    public readonly struct CozyTownHudState
    {
        public CozyTownHudState(int day, int minuteOfDay, int balance)
        {
            Day = day;
            MinuteOfDay = minuteOfDay;
            Balance = balance;
        }

        public int Day { get; }

        public int MinuteOfDay { get; }

        public int Balance { get; }

        public int Hour => MinuteOfDay / 60;

        public int Minute => MinuteOfDay % 60;
    }
}
