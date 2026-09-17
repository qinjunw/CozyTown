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
        private const double SecondsPerMinute = WorldTimeProgress.EffectiveSecondsPerGameMinute;

        // Frame-by-frame addition can round a completed minute below its boundary.
        private const double MinuteBoundaryToleranceSeconds = 1e-9;

        private readonly IWorldTimeCoordinator _worldTime;
        private readonly IGameSaveCoordinator _gameSave;
        private readonly WorldTimeFlow _timeFlow;
        private double _elapsedSeconds;

        public DaytimeClockCoordinator(
            IWorldTimeCoordinator worldTime,
            IGameSaveCoordinator gameSave,
            WorldTimeFlow timeFlow = null)
        {
            _worldTime = worldTime ?? throw new ArgumentNullException(nameof(worldTime));
            _gameSave = gameSave ?? throw new ArgumentNullException(nameof(gameSave));
            _timeFlow = timeFlow;
        }

        public GameClockSnapshot Current => _worldTime.Current;

        public bool HasSave => _gameSave.HasSave;

        public OperationResult<GameClockSnapshot> AdvanceElapsed(double seconds)
        {
            if (_timeFlow != null && _timeFlow.State != WorldTimeFlowState.Ready)
                return OperationResult<GameClockSnapshot>.Failure("world_time.not_ready");
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

            _timeFlow?.BeginCoordinatedAdvance();
            try
            {
                OperationResult<GameClockSnapshot> result = _worldTime.AdvanceMinutes((int)minutes);
                if (result.IsSuccess)
                {
                    _elapsedSeconds = Math.Max(0, elapsed - minutes * SecondsPerMinute);
                    var published = _timeFlow?.CompleteElapsedAdvance(result.Value, _elapsedSeconds / SecondsPerMinute);
                    if (published.HasValue && !published.Value.IsSuccess)
                        return OperationResult<GameClockSnapshot>.Failure(published.Value.ErrorCode);
                }
                return result;
            }
            finally
            {
                _timeFlow?.CancelCoordinatedAdvance();
            }
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
            if (_timeFlow != null && _timeFlow.State != WorldTimeFlowState.Ready)
                return OperationResult<GameClockSnapshot>.Failure("world_time.not_ready");
            _timeFlow?.BeginCoordinatedAdvance();
            try
            {
                OperationResult<GameClockSnapshot> result = _worldTime.AdvanceMinutes(gameMinutes);
                if (result.IsSuccess)
                {
                    _elapsedSeconds = 0;
                    var published = _timeFlow?.CompleteExplicitAdvance(result.Value, gameMinutes);
                    if (published.HasValue && !published.Value.IsSuccess)
                        return OperationResult<GameClockSnapshot>.Failure(published.Value.ErrorCode);
                }
                return result;
            }
            finally
            {
                _timeFlow?.CancelCoordinatedAdvance();
            }
        }

        public OperationResult Save() => _timeFlow != null && _timeFlow.State != WorldTimeFlowState.Ready
            ? OperationResult.Failure("world_time.not_ready") : _gameSave.Save();

        public OperationResult Load()
        {
            if (_timeFlow?.State == WorldTimeFlowState.Publishing)
                return OperationResult.Failure("world_time.not_ready");
            var previousState = _timeFlow?.BeginRestore();
            OperationResult result;
            try { result = _gameSave.Load(); }
            catch (Exception error)
            {
                _timeFlow?.RequireRecovery("restore:" + error.GetType().Name);
                return OperationResult.Failure("save.restore_exception");
            }
            if (!result.IsSuccess)
            {
                if (result.ErrorCode?.StartsWith("save.rollback_", StringComparison.Ordinal) == true)
                    _timeFlow?.RequireRecovery(result.ErrorCode);
                else if (previousState.HasValue) _timeFlow.RestoreRejected(previousState.Value);
                return result;
            }
            if (result.IsSuccess)
            {
                _elapsedSeconds = 0;
                var published = _timeFlow?.Publish(Current, isRebuild: true);
                if (published.HasValue && !published.Value.IsSuccess)
                    return OperationResult.Failure(_timeFlow.State == WorldTimeFlowState.RecoveryRequired
                        ? "save.loaded_rebuild_required" : "save.loaded_presentation_failed");
            }

            return result;
        }
    }
}
