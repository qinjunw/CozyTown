using System;

namespace CozyTown.Runtime.Time
{
    public interface IWorldTimeFlow
    {
        WorldTimeProgress Current { get; }
        event Action<WorldTimeProgress> Changed;
    }

    public readonly struct WorldTimeProgress
    {
        public const double EffectiveSecondsPerGameMinute = 0.5;

        public WorldTimeProgress(GameClockSnapshot clock, double fractionalMinute, bool isRebuild,
            long rebuildVersion = 0, double? advanceFromTotalMinutes = null)
        {
            Clock = clock;
            FractionalMinute = fractionalMinute;
            IsRebuild = isRebuild;
            RebuildVersion = rebuildVersion;
            AdvanceFromTotalMinutes = advanceFromTotalMinutes ?? ((long)clock.Day - 1) * 1440 + clock.MinuteOfDay + fractionalMinute;
        }

        public GameClockSnapshot Clock { get; }
        public double FractionalMinute { get; }
        public bool IsRebuild { get; }
        public long RebuildVersion { get; }
        public double AdvanceFromTotalMinutes { get; }
        public double TotalMinutes => ((long)Clock.Day - 1) * 1440 + Clock.MinuteOfDay + FractionalMinute;
    }
}
