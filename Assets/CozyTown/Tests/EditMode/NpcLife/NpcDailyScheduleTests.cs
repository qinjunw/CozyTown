using System;
using CozyTown.Runtime.NpcLife;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcLife
{
    public sealed class NpcDailyScheduleTests
    {
        [TestCase(0, "mina_home_entrance", NpcActivity.Home)]
        [TestCase(359, "mina_home_entrance", NpcActivity.Home)]
        [TestCase(360, "shop_work", NpcActivity.Working)]
        [TestCase(479, "shop_work", NpcActivity.Working)]
        [TestCase(480, "shop_work", NpcActivity.Working)]
        [TestCase(719, "shop_work", NpcActivity.Working)]
        [TestCase(720, "square_rest", NpcActivity.Resting)]
        [TestCase(779, "square_rest", NpcActivity.Resting)]
        [TestCase(780, "shop_afternoon", NpcActivity.Working)]
        [TestCase(1019, "shop_afternoon", NpcActivity.Working)]
        [TestCase(1020, "mina_home_entrance", NpcActivity.Home)]
        [TestCase(1079, "mina_home_entrance", NpcActivity.Home)]
        [TestCase(1080, "mina_home_entrance", NpcActivity.Home)]
        [TestCase(1439, "mina_home_entrance", NpcActivity.Home)]
        public void Query_UsesHalfOpenConfiguredActivityPeriods(
            int minuteOfDay, string locationId, NpcActivity activity)
        {
            NpcScheduleTarget target = MinaSchedule().Query(minuteOfDay);

            Assert.That(target.TargetLocationId, Is.EqualTo(locationId));
            Assert.That(target.ExpectedActivity, Is.EqualTo(activity));
        }

        [TestCase(0, "mina_home_entrance", "mina_home_entrance", true, true)]
        [TestCase(359, "mina_home_entrance", "mina_home_entrance", true, true)]
        [TestCase(360, "mina_home_outside", "shop_work", false, false)]
        [TestCase(479, "mina_home_outside", "shop_work", false, false)]
        [TestCase(480, "shop_work", "shop_work", false, true)]
        [TestCase(719, "shop_work", "shop_work", false, true)]
        [TestCase(720, "square_rest", "square_rest", false, true)]
        [TestCase(779, "square_rest", "square_rest", false, true)]
        [TestCase(780, "shop_afternoon", "shop_afternoon", false, true)]
        [TestCase(1019, "shop_afternoon", "shop_afternoon", false, true)]
        [TestCase(1020, "shop_afternoon", "mina_home_entrance", false, false)]
        [TestCase(1079, "shop_afternoon", "mina_home_entrance", false, false)]
        [TestCase(1080, "mina_home_entrance", "mina_home_entrance", true, true)]
        [TestCase(1439, "mina_home_entrance", "mina_home_entrance", true, true)]
        public void Rebuild_UsesLegalLoadingLocationWithoutClaimingCommuteArrival(
            int minuteOfDay, string locationId, string targetLocationId, bool isHome, bool hasArrived)
        {
            NpcReconstruction result = MinaSchedule().Rebuild(minuteOfDay);

            Assert.That(result.LocationId, Is.EqualTo(locationId));
            Assert.That(result.Target.TargetLocationId, Is.EqualTo(targetLocationId));
            Assert.That(result.IsHome, Is.EqualTo(isHome));
            Assert.That(result.HasArrived, Is.EqualTo(hasArrived));
        }

        [TestCase(0, 360)]
        [TestCase(359, 1)]
        [TestCase(360, 120)]
        [TestCase(479, 1)]
        [TestCase(480, 240)]
        [TestCase(719, 1)]
        [TestCase(720, 60)]
        [TestCase(779, 1)]
        [TestCase(780, 240)]
        [TestCase(1019, 1)]
        [TestCase(1020, 60)]
        [TestCase(1079, 1)]
        [TestCase(1080, 720)]
        [TestCase(1439, 361)]
        public void MinutesUntilNextBoundary_ReturnsStrictlyFutureBoundaryIncludingNextDay(
            int minuteOfDay, int minutes)
        {
            Assert.That(MinaSchedule().MinutesUntilNextBoundary(minuteOfDay), Is.EqualTo(minutes));
        }

        [TestCase(0, "shop_work", "shop_work", false, true)]
        [TestCase(239, "shop_work", "shop_work", false, true)]
        [TestCase(240, "square_rest", "square_rest", false, true)]
        [TestCase(299, "square_rest", "square_rest", false, true)]
        [TestCase(300, "shop_afternoon", "shop_afternoon", false, true)]
        [TestCase(539, "shop_afternoon", "shop_afternoon", false, true)]
        [TestCase(540, "shop_afternoon", "mina_home_entrance", false, false)]
        [TestCase(599, "shop_afternoon", "mina_home_entrance", false, false)]
        [TestCase(600, "mina_home_entrance", "mina_home_entrance", true, true)]
        [TestCase(1319, "mina_home_entrance", "mina_home_entrance", true, true)]
        [TestCase(1320, "mina_home_outside", "shop_work", false, false)]
        [TestCase(1439, "mina_home_outside", "shop_work", false, false)]
        public void Rebuild_PersonalScheduleCanCrossMidnight(
            int minuteOfDay, string locationId, string targetLocationId, bool isHome, bool hasArrived)
        {
            var schedule = new NpcDailySchedule(
                "npc_mina", "home_mina", "mina_home_outside", "mina_home_entrance",
                "shop_work", "square_rest", "shop_afternoon",
                1320, 0, 240, 300, 540, 600);

            NpcReconstruction result = schedule.Rebuild(minuteOfDay);

            Assert.That(result.LocationId, Is.EqualTo(locationId));
            Assert.That(result.Target.TargetLocationId, Is.EqualTo(targetLocationId));
            Assert.That(result.IsHome, Is.EqualTo(isHome));
            Assert.That(result.HasArrived, Is.EqualTo(hasArrived));
        }

        [TestCase(0, -1, 0)]
        [TestCase(1, -1, 0)]
        [TestCase(2, -1, 0)]
        [TestCase(3, -1, 0)]
        [TestCase(4, -1, 0)]
        [TestCase(5, -1, 0)]
        [TestCase(6, -1, 0)]
        [TestCase(-1, 0, -1)]
        [TestCase(-1, 5, 1440)]
        [TestCase(-1, 1, 360)]
        [TestCase(-1, 4, 760)]
        public void Construction_RejectsMissingIdentityAndInvalidPhaseBoundaries(
            int blankIdIndex, int invalidMinuteIndex, int invalidMinute)
        {
            string[] ids = { "npc_mina", "home_mina", "mina_home_outside", "mina_home_entrance",
                "shop_work", "square_rest", "shop_afternoon" };
            int[] minutes = { 360, 480, 720, 780, 1020, 1080 };
            if (blankIdIndex >= 0)
            {
                ids[blankIdIndex] = " ";
            }
            if (invalidMinuteIndex >= 0)
            {
                minutes[invalidMinuteIndex] = invalidMinute;
            }

            Assert.That(() => new NpcDailySchedule(
                    ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], ids[6],
                    minutes[0], minutes[1], minutes[2], minutes[3], minutes[4], minutes[5]),
                Throws.InstanceOf<ArgumentException>());
        }

        [TestCase(-1)]
        [TestCase(1440)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void ScheduleQueries_RejectMinutesOutsideOneDay(int minuteOfDay)
        {
            NpcDailySchedule schedule = MinaSchedule();

            Assert.Throws<ArgumentOutOfRangeException>(() => schedule.Query(minuteOfDay));
            Assert.Throws<ArgumentOutOfRangeException>(() => schedule.Rebuild(minuteOfDay));
            Assert.Throws<ArgumentOutOfRangeException>(() => schedule.MinutesUntilNextBoundary(minuteOfDay));
        }

        private static NpcDailySchedule MinaSchedule()
        {
            return new NpcDailySchedule(
                "npc_mina", "home_mina", "mina_home_outside", "mina_home_entrance",
                "shop_work", "square_rest", "shop_afternoon",
                360, 480, 720, 780, 1020, 1080);
        }
    }
}
