using System;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public sealed class DaytimeClockCoordinator :
        IDaytimeClock,
        ISleepCoordinator,
        IDayTransitionCoordinator,
        IGameSaveCoordinator
    {
        private const double SecondsPerMinute = 0.5;

        // Frame-by-frame addition can round a completed minute below its boundary.
        private const double MinuteBoundaryToleranceSeconds = 1e-9;

        private readonly IWorldTimeCoordinator _worldTime;
        private readonly IGameSaveCoordinator _gameSave;
        private double _elapsedSeconds;

        public DaytimeClockCoordinator(
            IWorldTimeCoordinator worldTime,
            IGameSaveCoordinator gameSave)
        {
            _worldTime = worldTime ?? throw new ArgumentNullException(nameof(worldTime));
            _gameSave = gameSave ?? throw new ArgumentNullException(nameof(gameSave));
        }

        public GameClockSnapshot Current => _worldTime.Current;

        public bool HasSave => _gameSave.HasSave;

        public OperationResult<GameClockSnapshot> AdvanceElapsed(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return OperationResult<GameClockSnapshot>.Failure("time.elapsed_invalid");
            }

            double elapsed = _elapsedSeconds + seconds;
            double minutes = Math.Floor(
                (elapsed + MinuteBoundaryToleranceSeconds) / SecondsPerMinute);
            if (minutes > WorldTimeCoordinator.MaximumAdvanceMinutes)
            {
                return OperationResult<GameClockSnapshot>.Failure("time.elapsed_too_large");
            }

            OperationResult<GameClockSnapshot> result =
                _worldTime.AdvanceMinutes((int)minutes);
            if (result.IsSuccess)
            {
                _elapsedSeconds = Math.Max(0, elapsed - minutes * SecondsPerMinute);
            }

            return result;
        }

        public OperationResult<GameClockSnapshot> SleepToNextDay()
        {
            int minutes = InMemoryTimeService.MinutesPerDay - Current.MinuteOfDay + 6 * 60;
            return AdvanceExplicitly(minutes);
        }

        public OperationResult<GameClockSnapshot> SleepForMinutes(int gameMinutes)
        {
            if (gameMinutes < 60 || gameMinutes > 12 * 60 || gameMinutes % 60 != 0)
            {
                return OperationResult<GameClockSnapshot>.Failure("sleep.duration_invalid");
            }

            return AdvanceExplicitly(gameMinutes);
        }

        private OperationResult<GameClockSnapshot> AdvanceExplicitly(int gameMinutes)
        {
            OperationResult<GameClockSnapshot> result = _worldTime.AdvanceMinutes(gameMinutes);
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
