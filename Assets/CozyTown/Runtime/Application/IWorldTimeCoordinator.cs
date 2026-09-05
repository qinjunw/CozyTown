using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public interface IWorldTimeCoordinator
    {
        GameClockSnapshot Current { get; }

        /// <summary>
        /// Advances 0 through 10,080 game minutes atomically. The per-request limit does not cap calendar days.
        /// </summary>
        OperationResult<GameClockSnapshot> AdvanceMinutes(int gameMinutes);
    }
}
