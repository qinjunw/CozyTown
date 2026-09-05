using System;

namespace CozyTown.Runtime.Time
{
    public sealed class WorldTimeFlow : IWorldTimeFlow
    {
        private bool _elapsedAdvance;
        public WorldTimeFlow(GameClockSnapshot initialClock)
        {
            Current = new WorldTimeProgress(initialClock, 0, true, 1);
        }

        public WorldTimeProgress Current { get; private set; }
        public event Action<WorldTimeProgress> Changed;

        internal void BeginCoordinatedAdvance() => _elapsedAdvance = true;

        internal void CancelCoordinatedAdvance() => _elapsedAdvance = false;

        internal void CompleteExplicitAdvance(GameClockSnapshot clock, int minutes)
        {
            _elapsedAdvance = false;
            double start = ((long)clock.Day - 1) * 1440 + clock.MinuteOfDay - minutes;
            Publish(clock, advanceFromTotalMinutes: start);
        }

        internal void CompleteElapsedAdvance(GameClockSnapshot clock, double fractionalMinute)
        {
            _elapsedAdvance = false;
            Publish(clock, fractionalMinute);
        }

        internal void Publish(GameClockSnapshot clock, double fractionalMinute = 0, bool isRebuild = false,
            double? advanceFromTotalMinutes = null)
        {
            if (_elapsedAdvance) return;
            var next = new WorldTimeProgress(clock, fractionalMinute, isRebuild,
                Current.RebuildVersion + (isRebuild ? 1 : 0),
                advanceFromTotalMinutes ?? Current.TotalMinutes);
            if (!isRebuild && next.TotalMinutes == Current.TotalMinutes) return;
            Current = next;
            Changed?.Invoke(next);
        }
    }
}
