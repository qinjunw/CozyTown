using System;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Time
{
    public sealed class InMemoryTimeServiceTests
    {
        [Test]
        public void SleepToNextDay_IncrementsDayOnceAndSetsSixAm()
        {
            var service = new InMemoryTimeService(startingDay: 3, startingMinuteOfDay: 22 * 60);

            GameClockSnapshot result = service.SleepToNextDay();

            Assert.That(result.Day, Is.EqualTo(4));
            Assert.That(result.MinuteOfDay, Is.EqualTo(6 * 60));
            Assert.That(service.Current, Is.EqualTo(result));
        }

        [Test]
        public void SleepToNextDay_AtMaximumDay_ThrowsWithoutChangingClock()
        {
            var service = new InMemoryTimeService(int.MaxValue, startingMinuteOfDay: 10 * 60);

            Assert.Throws<InvalidOperationException>(() => service.SleepToNextDay());
            Assert.That(service.Current.Day, Is.EqualTo(int.MaxValue));
            Assert.That(service.Current.MinuteOfDay, Is.EqualTo(10 * 60));
        }
    }
}
