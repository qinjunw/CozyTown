using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public interface IDayTransitionCoordinator
    {
        OperationResult<GameClockSnapshot> SleepToNextDay();
    }
}
