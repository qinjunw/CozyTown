using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Time
{
    public interface ITimeService
    {
        GameClockSnapshot Current { get; }

        OperationResult<GameClockSnapshot> AdvanceMinutes(int minutes);

        /// <exception cref="System.InvalidOperationException">
        /// The current day is <see cref="int.MaxValue"/> and cannot advance.
        /// </exception>
        GameClockSnapshot SleepToNextDay();

        OperationResult Restore(GameClockSnapshot snapshot);
    }
}
