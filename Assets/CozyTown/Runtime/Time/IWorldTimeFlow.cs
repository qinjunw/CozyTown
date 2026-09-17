using System;
using System.Collections.Generic;

namespace CozyTown.Runtime.Time
{
    public interface IWorldTimeFlow
    {
        WorldTimeProgress Current { get; }
        WorldTimeFlowState State { get; }
        IReadOnlyList<string> NotificationFailures { get; }
        // Required world synchronization. A failure suspends coordinated mutations until a successful load.
        event Action<WorldTimeProgress> Changed;
        // Presentation runs after every required subscriber succeeds; failures do not suspend the world.
        event Action<WorldTimeProgress> PresentationChanged;
    }

    public enum WorldTimeFlowState { Ready, Publishing, RecoveryRequired }

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
