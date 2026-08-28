using System;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public sealed class DayTransitionCoordinator : IDayTransitionCoordinator
    {
        private readonly ITimeService _time;
        private readonly IFarmService _farm;
        private readonly ILivestockService _livestock;

        public DayTransitionCoordinator(
            ITimeService time,
            IFarmService farm,
            ILivestockService livestock)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _farm = farm ?? throw new ArgumentNullException(nameof(farm));
            _livestock = livestock ?? throw new ArgumentNullException(nameof(livestock));
        }

        public OperationResult<GameClockSnapshot> SleepToNextDay()
        {
            GameClockSnapshot clockBefore = _time.Current;
            FarmSnapshot farmBefore = _farm.CaptureSnapshot();
            LivestockSnapshot livestockBefore = _livestock.CaptureSnapshot();

            if (farmBefore == null
                || livestockBefore == null
                || farmBefore.LastProcessedDay != clockBefore.Day
                || livestockBefore.LastProcessedDay != clockBefore.Day)
            {
                return OperationResult<GameClockSnapshot>.Failure(
                    "day_transition.state_misaligned");
            }

            if (clockBefore.Day == int.MaxValue)
            {
                return OperationResult<GameClockSnapshot>.Failure(
                    "day_transition.day_overflow");
            }

            int targetDay = clockBefore.Day + 1;
            GameClockSnapshot clockAfter;
            try
            {
                clockAfter = _time.SleepToNextDay();
            }
            catch (InvalidOperationException)
            {
                return RollBack(
                    clockBefore,
                    farmBefore,
                    livestockBefore,
                    "day_transition.time_failed");
            }

            if (clockAfter.Day != targetDay || _time.Current.Day != targetDay)
            {
                return RollBack(
                    clockBefore,
                    farmBefore,
                    livestockBefore,
                    "day_transition.time_invalid");
            }

            OperationResult farmAdvance = _farm.AdvanceDay(targetDay);
            if (!farmAdvance.IsSuccess)
            {
                return RollBack(
                    clockBefore,
                    farmBefore,
                    livestockBefore,
                    "day_transition.farm_failed");
            }

            OperationResult livestockAdvance = _livestock.AdvanceDay(targetDay);
            if (!livestockAdvance.IsSuccess)
            {
                return RollBack(
                    clockBefore,
                    farmBefore,
                    livestockBefore,
                    "day_transition.livestock_failed");
            }

            return OperationResult<GameClockSnapshot>.Success(clockAfter);
        }

        private OperationResult<GameClockSnapshot> RollBack(
            GameClockSnapshot clock,
            FarmSnapshot farm,
            LivestockSnapshot livestock,
            string originalError)
        {
            OperationResult timeRestore = _time.Restore(clock);
            OperationResult farmRestore = _farm.Restore(farm);
            OperationResult livestockRestore = _livestock.Restore(livestock);

            int failureCount = (timeRestore.IsSuccess ? 0 : 1)
                + (farmRestore.IsSuccess ? 0 : 1)
                + (livestockRestore.IsSuccess ? 0 : 1);
            if (failureCount == 0)
            {
                return OperationResult<GameClockSnapshot>.Failure(originalError);
            }

            if (failureCount > 1)
            {
                return OperationResult<GameClockSnapshot>.Failure(
                    "day_transition.rollback_multiple_failed");
            }

            if (!timeRestore.IsSuccess)
            {
                return OperationResult<GameClockSnapshot>.Failure(
                    "day_transition.rollback_time_failed");
            }

            if (!farmRestore.IsSuccess)
            {
                return OperationResult<GameClockSnapshot>.Failure(
                    "day_transition.rollback_farm_failed");
            }

            return OperationResult<GameClockSnapshot>.Failure(
                "day_transition.rollback_livestock_failed");
        }
    }
}
