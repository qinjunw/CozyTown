using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class CharacterShopTradingCoordinatorTests
    {
        [Test]
        public void Buy_WhenPurchaseIsValid_TransfersItemsAndMoneyWithoutCreatingAssets()
        {
            IEconomyStateStore stateStore = new InMemoryEconomyStateStore(
                new[]
                {
                    Character("character.player", balance: 100)
                },
                new[]
                {
                    Shop(
                        "shop.town.general",
                        balance: 1000,
                        new ItemStack("seed.potato", 3))
                });
            ICharacterShopTradingCoordinator coordinator =
                new CharacterShopTradingCoordinator(
                    new[]
                    {
                        new ItemDefinition(
                            "seed.potato",
                            "Potato Seed",
                            ItemCategory.Seed,
                            maxStack: 99)
                    },
                    new[]
                    {
                        new ShopOffer("seed.potato", buyPrice: 20, sellPrice: 10)
                    },
                    backpackCapacitySlots: 12,
                    stateStore);

            OperationResult<ShopReceipt> result = coordinator.Buy(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 2);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.ItemId, Is.EqualTo("seed.potato"));
            Assert.That(result.Value.Quantity, Is.EqualTo(2));
            Assert.That(result.Value.TotalPrice, Is.EqualTo(40));
            Assert.That(result.Value.IsPurchase, Is.True);
            Assert.That(
                stateStore.TryGetCharacter(
                    "character.player",
                    out CharacterEconomySnapshot character),
                Is.True);
            Assert.That(
                stateStore.TryGetShop(
                    "shop.town.general",
                    out ShopEconomySnapshot shop),
                Is.True);
            Assert.That(character.Wallet.Balance, Is.EqualTo(60));
            Assert.That(Quantity(character.Backpack, "seed.potato"), Is.EqualTo(2));
            Assert.That(shop.Wallet.Balance, Is.EqualTo(1040));
            Assert.That(Quantity(shop.Stock, "seed.potato"), Is.EqualTo(1));
            Assert.That(character.Wallet.Balance + shop.Wallet.Balance, Is.EqualTo(1100));
            Assert.That(
                Quantity(character.Backpack, "seed.potato")
                    + Quantity(shop.Stock, "seed.potato"),
                Is.EqualTo(3));
        }

        private static CharacterEconomySnapshot Character(
            string characterId,
            int balance,
            params ItemStack[] items)
        {
            return new CharacterEconomySnapshot(
                characterId,
                new InventorySnapshot(items),
                new WalletSnapshot(balance));
        }

        private static ShopEconomySnapshot Shop(
            string shopId,
            int balance,
            params ItemStack[] items)
        {
            return new ShopEconomySnapshot(
                shopId,
                new InventorySnapshot(items),
                new WalletSnapshot(balance),
                lastRestockedDay: 1,
                restockAlgorithmVersion: 1);
        }

        private static int Quantity(InventorySnapshot snapshot, string itemId)
        {
            ItemStack stack = snapshot.Items.SingleOrDefault(item => item.ItemId == itemId);
            return stack.Quantity;
        }
    }
}
