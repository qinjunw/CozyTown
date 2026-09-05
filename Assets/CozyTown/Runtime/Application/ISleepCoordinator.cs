using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public interface ISleepCoordinator
    {
        /// <summary>Advances the world by one to twelve whole hours, expressed in minutes.</summary>
        OperationResult<GameClockSnapshot> SleepForMinutes(int gameMinutes);
    }
}
