using CozyTown.Runtime.Core;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class WorldTimeFlowTests
    {
        [Test]
        public void AcceptedElapsedTime_ExposesSmoothProgressBetweenClockMinutes()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(360));
            Assert.That(services.WorldTimeFlow.Current.TotalMinutes, Is.EqualTo(360.5).Within(1e-8));
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.5).IsSuccess, Is.True);
            Assert.That(services.WorldTimeFlow.Current.TotalMinutes, Is.EqualTo(361.5).Within(1e-8));
        }

        [Test]
        public void DirectWorldAdvance_PreservesTheDaytimeFractionalBudget()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            services.DaytimeClock.AdvanceElapsed(0.25);
            services.WorldTime.AdvanceMinutes(1);
            Assert.That(services.WorldTimeFlow.Current.TotalMinutes, Is.EqualTo(361.5).Within(1e-8));
            services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(services.WorldTimeFlow.Current.TotalMinutes, Is.EqualTo(362).Within(1e-8));
        }
    }
}
