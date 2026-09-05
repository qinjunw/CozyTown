using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Time
{
    public sealed class InMemoryTimeService : ITimeService
    {
        public const int MinutesPerDay = 24 * 60;

        private GameClockSnapshot _current;

        public InMemoryTimeService(int startingDay = 1, int startingMinuteOfDay = 6 * 60)
        {
            _current = IsValid(startingDay, startingMinuteOfDay)
                ? new GameClockSnapshot(startingDay, startingMinuteOfDay)
                : new GameClockSnapshot(1, 6 * 60);
        }

        public GameClockSnapshot Current => _current;

        public OperationResult<GameClockSnapshot> AdvanceMinutes(int minutes)
        {
            OperationResult<GameClockSnapshot> candidate = CreateAdvanceCandidate(minutes);
            if (candidate.IsSuccess)
            {
                CommitPrepared(candidate.Value);
            }

            return candidate;
        }

        internal OperationResult<GameClockSnapshot> CreateAdvanceCandidate(int minutes)
        {
            if (minutes < 0)
            {
                return OperationResult<GameClockSnapshot>.Failure("time.minutes_negative");
            }

            long absoluteMinutes = ((long)_current.Day - 1) * MinutesPerDay
                + _current.MinuteOfDay
                + minutes;
            long day = absoluteMinutes / MinutesPerDay + 1;
            if (day > int.MaxValue)
            {
                return OperationResult<GameClockSnapshot>.Failure("time.day_overflow");
            }

            return OperationResult<GameClockSnapshot>.Success(
                new GameClockSnapshot((int)day, (int)(absoluteMinutes % MinutesPerDay)));
        }

        internal void CommitPrepared(GameClockSnapshot candidate)
        {
            _current = candidate;
        }

        public GameClockSnapshot SleepToNextDay()
        {
            if (_current.Day == int.MaxValue)
            {
                throw new System.InvalidOperationException("The game day cannot advance beyond Int32.MaxValue.");
            }

            _current = new GameClockSnapshot(_current.Day + 1, 6 * 60);
            return _current;
        }

        public OperationResult Restore(GameClockSnapshot snapshot)
        {
            if (!IsValid(snapshot.Day, snapshot.MinuteOfDay))
            {
                return OperationResult.Failure("time.snapshot_invalid");
            }

            _current = snapshot;
            return OperationResult.Success();
        }

        private static bool IsValid(int day, int minuteOfDay)
        {
            return day >= 1 && minuteOfDay >= 0 && minuteOfDay < MinutesPerDay;
        }
    }
}
