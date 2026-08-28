using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Economy
{
    public sealed class InMemoryShopServiceTests
    {
        [Test]
        public void Buy_WhenInventoryCannotAcceptItem_RefundsWalletAndAddsNothing()
        {
            var inventory = new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("filler", "Filler", ItemCategory.Material, maxStack: 99),
                    new ItemDefinition("potato-seed", "Potato Seed", ItemCategory.Seed, maxStack: 99)
                },
                capacitySlots: 1);
            Assert.That(inventory.Add("filler", 1).IsSuccess, Is.True);
            var wallet = new InMemoryWallet(startingBalance: 10);
            var shop = new InMemoryShopService(
                new[] { new ShopOffer("potato-seed", buyPrice: 4, sellPrice: 2) },
                wallet,
                inventory);

            OperationResult<ShopReceipt> result = shop.Buy("potato-seed", 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("inventory.capacity_exceeded"));
            Assert.That(wallet.Balance, Is.EqualTo(10));
            Assert.That(inventory.Count("potato-seed"), Is.Zero);
            Assert.That(inventory.Count("filler"), Is.EqualTo(1));
        }
    }
}
