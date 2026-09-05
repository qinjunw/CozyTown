using System;

namespace CozyTown.Runtime.NpcLife
{
    public sealed class NpcDailySchedule
    {
        private const int MinutesPerDay = 24 * 60;

        public NpcDailySchedule(
            string npcId,
            string homeId,
            string homeOutsideLocationId,
            string homeEntranceLocationId,
            string morningWorkLocationId,
            string restLocationId,
            string afternoonWorkLocationId,
            int departureMinute,
            int morningArrivalDeadlineMinute,
            int restStartMinute,
            int afternoonStartMinute,
            int returnStartMinute,
            int homeArrivalDeadlineMinute)
        {
            RequireId(npcId, nameof(npcId));
            RequireId(homeId, nameof(homeId));
            RequireId(homeOutsideLocationId, nameof(homeOutsideLocationId));
            RequireId(homeEntranceLocationId, nameof(homeEntranceLocationId));
            RequireId(morningWorkLocationId, nameof(morningWorkLocationId));
            RequireId(restLocationId, nameof(restLocationId));
            RequireId(afternoonWorkLocationId, nameof(afternoonWorkLocationId));
            RequireMinute(departureMinute, nameof(departureMinute));
            RequireMinute(morningArrivalDeadlineMinute, nameof(morningArrivalDeadlineMinute));
            RequireMinute(restStartMinute, nameof(restStartMinute));
            RequireMinute(afternoonStartMinute, nameof(afternoonStartMinute));
            RequireMinute(returnStartMinute, nameof(returnStartMinute));
            RequireMinute(homeArrivalDeadlineMinute, nameof(homeArrivalDeadlineMinute));

            int previousOffset = 0;
            foreach (int boundary in new[]
            {
                morningArrivalDeadlineMinute, restStartMinute, afternoonStartMinute,
                returnStartMinute, homeArrivalDeadlineMinute
            })
            {
                int offset = MinutesForward(departureMinute, boundary);
                if (offset <= previousOffset)
                {
                    throw new ArgumentException(
                        "Daily schedule phases must follow departure in order within one day.");
                }

                previousOffset = offset;
            }

            NpcId = npcId;
            HomeId = homeId;
            HomeOutsideLocationId = homeOutsideLocationId;
            HomeEntranceLocationId = homeEntranceLocationId;
            MorningWorkLocationId = morningWorkLocationId;
            RestLocationId = restLocationId;
            AfternoonWorkLocationId = afternoonWorkLocationId;
            DepartureMinute = departureMinute;
            MorningArrivalDeadlineMinute = morningArrivalDeadlineMinute;
            RestStartMinute = restStartMinute;
            AfternoonStartMinute = afternoonStartMinute;
            ReturnStartMinute = returnStartMinute;
            HomeArrivalDeadlineMinute = homeArrivalDeadlineMinute;
        }

        public string NpcId { get; }
        public string HomeId { get; }
        public string HomeOutsideLocationId { get; }
        public string HomeEntranceLocationId { get; }
        public string MorningWorkLocationId { get; }
        public string RestLocationId { get; }
        public string AfternoonWorkLocationId { get; }
        public int DepartureMinute { get; }
        public int MorningArrivalDeadlineMinute { get; }
        public int RestStartMinute { get; }
        public int AfternoonStartMinute { get; }
        public int ReturnStartMinute { get; }
        public int HomeArrivalDeadlineMinute { get; }

        public NpcScheduleTarget Query(int minuteOfDay)
        {
            RequireMinute(minuteOfDay, nameof(minuteOfDay));
            int sinceDeparture = MinutesForward(DepartureMinute, minuteOfDay);
            if (sinceDeparture >= MinutesForward(DepartureMinute, ReturnStartMinute))
            {
                return new NpcScheduleTarget(HomeEntranceLocationId, NpcActivity.Home);
            }

            if (sinceDeparture < MinutesForward(DepartureMinute, RestStartMinute))
            {
                return new NpcScheduleTarget(MorningWorkLocationId, NpcActivity.Working);
            }

            return sinceDeparture < MinutesForward(DepartureMinute, AfternoonStartMinute)
                ? new NpcScheduleTarget(RestLocationId, NpcActivity.Resting)
                : new NpcScheduleTarget(AfternoonWorkLocationId, NpcActivity.Working);
        }

        // Loading and new games use this location; elapsed-time movement uses Query.
        public NpcReconstruction Rebuild(int minuteOfDay)
        {
            NpcScheduleTarget target = Query(minuteOfDay);
            int sinceDeparture = MinutesForward(DepartureMinute, minuteOfDay);
            if (sinceDeparture >= MinutesForward(DepartureMinute, HomeArrivalDeadlineMinute))
            {
                return new NpcReconstruction(HomeEntranceLocationId, target, true, true);
            }

            if (sinceDeparture < MinutesForward(DepartureMinute, MorningArrivalDeadlineMinute))
            {
                return new NpcReconstruction(HomeOutsideLocationId, target, false, false);
            }

            if (sinceDeparture >= MinutesForward(DepartureMinute, ReturnStartMinute))
            {
                return new NpcReconstruction(AfternoonWorkLocationId, target, false, false);
            }

            return new NpcReconstruction(target.TargetLocationId, target, false, true);
        }

        public int MinutesUntilNextBoundary(int minuteOfDay)
        {
            RequireMinute(minuteOfDay, nameof(minuteOfDay));
            int next = MinutesPerDay;
            foreach (int boundary in new[]
            {
                DepartureMinute, MorningArrivalDeadlineMinute, RestStartMinute,
                AfternoonStartMinute, ReturnStartMinute, HomeArrivalDeadlineMinute
            })
            {
                int distance = MinutesForward(minuteOfDay, boundary);
                if (distance > 0 && distance < next)
                {
                    next = distance;
                }
            }

            return next;
        }

        private static int MinutesForward(int from, int to)
        {
            return (to - from + MinutesPerDay) % MinutesPerDay;
        }

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A stable ID is required.", parameterName);
            }
        }

        private static void RequireMinute(int minute, string parameterName)
        {
            if (minute < 0 || minute >= MinutesPerDay)
            {
                throw new ArgumentOutOfRangeException(parameterName,
                    "Minutes must be between 0 and 1439.");
            }
        }
    }
}
