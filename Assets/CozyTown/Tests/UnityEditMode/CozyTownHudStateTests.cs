using CozyTown.Unity.Hud;
using NUnit.Framework;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class CozyTownHudStateTests
    {
        [Test]
        public void Constructor_ExposesClockAndBalanceValues()
        {
            var state = new CozyTownHudState(day: 3, minuteOfDay: 8 * 60 + 5, balance: 250);

            Assert.That(state.Day, Is.EqualTo(3));
            Assert.That(state.Hour, Is.EqualTo(8));
            Assert.That(state.Minute, Is.EqualTo(5));
            Assert.That(state.Balance, Is.EqualTo(250));
        }
    }
}
