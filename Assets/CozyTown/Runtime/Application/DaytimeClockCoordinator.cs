using System;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public sealed class DaytimeClockCoordinator :
        IDaytimeClock,
        IDayTransitionCoordinator,
        IGameSaveCoordinator
    {
        private const double SecondsPerTick = 5;
        private const int MinutesPerTick = 10;

        // Frame-by-frame addition can leave a completed tick just below five seconds.
        private const double TickBoundaryToleranceSeconds = 1e-9;

        private readonly ITimeService _time;
        private readonly IDayTransitionCoordinator _dayTransition;
        private readonly IGameSaveCoordinator _gameSave;
        private double _elapsedSeconds;

        public DaytimeClockCoordinator(
            ITimeService time,
            IDayTransitionCoordinator dayTransition,
            IGameSaveCoordinator gameSave)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _dayTransition = dayTransition
                ?? throw new ArgumentNullException(nameof(dayTransition));
            _gameSave = gameSave ?? throw new ArgumentNullException(nameof(gameSave));
        }

        public GameClockSnapshot Current => _time.Current;

        public bool HasSave => _gameSave.HasSave;

        public OperationResult<GameClockSnapshot> AdvanceElapsed(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return OperationResult<GameClockSnapshot>.Failure("time.elapsed_invalid");
            }

            double elapsed = _elapsedSeconds + seconds;
            int remainingMinutes = InMemoryTimeService.MinutesPerDay - 1
                - Current.MinuteOfDay;
            int remainingTicks = (remainingMinutes + MinutesPerTick - 1) / MinutesPerTick;
            int ticks = (int)Math.Min(
                Math.Floor((elapsed + TickBoundaryToleranceSeconds) / SecondsPerTick),
                remainingTicks);
            int minutes = Math.Min(ticks * MinutesPerTick, remainingMinutes);
            OperationResult<GameClockSnapshot> result =
                _time.AdvanceMinutes(minutes);
            if (result.IsSuccess)
            {
                _elapsedSeconds = minutes == remainingMinutes
                    ? 0
                    : Math.Max(0, elapsed - ticks * SecondsPerTick);
            }

            return result;
        }

        public OperationResult<GameClockSnapshot> SleepToNextDay()
        {
            OperationResult<GameClockSnapshot> result = _dayTransition.SleepToNextDay();
            if (result.IsSuccess)
            {
                _elapsedSeconds = 0;
            }

            return result;
        }

        public OperationResult Save() => _gameSave.Save();

        public OperationResult Load()
        {
            OperationResult result = _gameSave.Load();
            if (result.IsSuccess)
            {
                _elapsedSeconds = 0;
            }

            return result;
        }
    }
}
