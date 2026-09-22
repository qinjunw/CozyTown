using CozyTown.Runtime.Core;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Save
{
    public sealed class CompleteWorldSaveTests
    {
        [Test]
        public void RequiredWorldWithoutAnAdapter_RejectsSaveAndKeepsThePreviousSlot()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            services.WorldTime.AdvanceMinutes(10);
            services.WorldSnapshots.Require();

            var result = services.GameSave.Save();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.world_unbound"));
            Assert.That(services.SaveStorage.Load("main").Value.Clock.MinuteOfDay, Is.EqualTo(360));
            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(370));
        }
    }
}
