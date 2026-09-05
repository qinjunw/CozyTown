using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Core
{
    public sealed class ContinuousTimeCompositionTests
    {
        [Test]
        public void Sleep_CrossesMidnightAndMorningUsingTheSharedWorldAndClearsRemainder()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.Time.Restore(new GameClockSnapshot(1, 1380)).IsSuccess, Is.True);
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.4).IsSuccess, Is.True);

            var result = services.Sleep.SleepForMinutes(8 * 60);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.EqualTo(new GameClockSnapshot(2, 420)));
            Assert.That(services.DaytimeClock.Current, Is.EqualTo(result.Value));
            Assert.That(services.WorldTime.Current, Is.EqualTo(result.Value));
            Assert.That(services.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            Assert.That(services.Livestock.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            foreach (var shop in services.EconomyState.CaptureSnapshot().Shops)
            {
                Assert.That(shop.LastRestockedDay, Is.EqualTo(2));
            }
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.1).IsSuccess, Is.True);
            Assert.That(services.Time.Current, Is.EqualTo(result.Value));
            Assert.That(services.DaytimeClock.AdvanceElapsed(0.4).IsSuccess, Is.True);
            Assert.That(services.Time.Current, Is.EqualTo(new GameClockSnapshot(2, 421)));
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
        }

        [Test]
        public void ForegroundClock_CrossesMidnightAndCanSaveTheUnsettledMorning()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.Time.Restore(new GameClockSnapshot(1, 1439)).IsSuccess, Is.True);

            var advanced = services.DaytimeClock.AdvanceElapsed(5);

            Assert.That(advanced.IsSuccess, Is.True, advanced.ErrorCode);
            Assert.That(advanced.Value.Day, Is.EqualTo(2));
            Assert.That(advanced.Value.MinuteOfDay, Is.EqualTo(9));
            Assert.That(services.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(1));
            var saved = services.GameSave.Save();
            Assert.That(saved.IsSuccess, Is.True, saved.ErrorCode);
            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(services.DaytimeClock.Current, Is.EqualTo(new GameClockSnapshot(2, 9)));
        }
    }
}
