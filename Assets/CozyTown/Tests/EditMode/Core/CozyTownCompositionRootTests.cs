using CozyTown.Runtime.Core;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Core
{
    public sealed class CozyTownCompositionRootTests
    {
        [Test]
        public void CreateEmpty_ReturnsCompleteServiceGraph()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateEmpty();

            Assert.That(services.Time, Is.Not.Null);
            Assert.That(services.Inventory, Is.Not.Null);
            Assert.That(services.Wallet, Is.Not.Null);
            Assert.That(services.Shop, Is.Not.Null);
            Assert.That(services.Farm, Is.Not.Null);
            Assert.That(services.Livestock, Is.Not.Null);
            Assert.That(services.Fishing, Is.Not.Null);
            Assert.That(services.Cooking, Is.Not.Null);
            Assert.That(services.NpcDialogue, Is.Not.Null);
            Assert.That(services.SaveStorage, Is.Not.Null);
            Assert.That(services.Time.Current.Day, Is.EqualTo(1));
            Assert.That(services.Inventory.CapacitySlots, Is.EqualTo(24));
            Assert.That(services.Wallet.Balance, Is.Zero);
        }
    }
}
