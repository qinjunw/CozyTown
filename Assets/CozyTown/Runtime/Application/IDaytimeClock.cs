using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public interface IDaytimeClock
    {
        GameClockSnapshot Current { get; }

        OperationResult<GameClockSnapshot> AdvanceElapsed(double seconds);
    }
}
