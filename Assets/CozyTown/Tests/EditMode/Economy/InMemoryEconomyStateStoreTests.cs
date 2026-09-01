using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Economy
{
    public sealed class InMemoryEconomyStateStoreTests
    {
        [Test]
        public void Commit_WhenCandidatesAreValid_PublishesCharacterAndShopTogether()
        {
            IEconomyStateStore store = new InMemoryEconomyStateStore(
                new[]
                {
                    Character(
                        "character.player",
                        balance: 300,
                        new ItemStack("seed.potato", 1))
                },
                new[]
                {
                    Shop(
                        "shop.town.general",
                        balance: 10000,
                        lastRestockedDay: 1,
                        restockAlgorithmVersion: 1,
                        new ItemStack("seed.potato", 6))
                });

            OperationResult result = store.Commit(
                Character(
                    "character.player",
                    balance: 280,
                    new ItemStack("seed.potato", 2)),
                Shop(
                    "shop.town.general",
                    balance: 10020,
                    lastRestockedDay: 1,
                    restockAlgorithmVersion: 1,
                    new ItemStack("seed.potato", 5)));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                store.TryGetCharacter("character.player", out CharacterEconomySnapshot character),
                Is.True);
            Assert.That(
                store.TryGetShop("shop.town.general", out ShopEconomySnapshot shop),
                Is.True);
            Assert.That(character.Wallet.Balance, Is.EqualTo(280));
            Assert.That(Quantity(character.Backpack, "seed.potato"), Is.EqualTo(2));
            Assert.That(shop.Wallet.Balance, Is.EqualTo(10020));
            Assert.That(Quantity(shop.Stock, "seed.potato"), Is.EqualTo(5));
            Assert.That(shop.LastRestockedDay, Is.EqualTo(1));
            Assert.That(shop.RestockAlgorithmVersion, Is.EqualTo(1));
        }

        [Test]
        public void Commit_WhenShopCandidateIsInvalid_PublishesNeitherCandidate()
        {
            IEconomyStateStore store = new InMemoryEconomyStateStore(
                new[]
                {
                    Character(
                        "character.player",
                        balance: 300,
                        new ItemStack("seed.potato", 1))
                },
                new[]
                {
                    Shop(
                        "shop.town.general",
                        balance: 10000,
                        lastRestockedDay: 1,
                        restockAlgorithmVersion: 1,
                        new ItemStack("seed.potato", 6))
                });

            OperationResult result = store.Commit(
                Character(
                    "character.player",
                    balance: 280,
                    new ItemStack("seed.potato", 2)),
                Shop(
                    "shop.town.general",
                    balance: -1,
                    lastRestockedDay: 1,
                    restockAlgorithmVersion: 1,
                    new ItemStack("seed.potato", 5)));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("economy.shop_invalid"));
            Assert.That(
                store.TryGetCharacter("character.player", out CharacterEconomySnapshot character),
                Is.True);
            Assert.That(
                store.TryGetShop("shop.town.general", out ShopEconomySnapshot shop),
                Is.True);
            Assert.That(character.Wallet.Balance, Is.EqualTo(300));
            Assert.That(Quantity(character.Backpack, "seed.potato"), Is.EqualTo(1));
            Assert.That(shop.Wallet.Balance, Is.EqualTo(10000));
            Assert.That(Quantity(shop.Stock, "seed.potato"), Is.EqualTo(6));
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
            int lastRestockedDay,
            int restockAlgorithmVersion,
            params ItemStack[] items)
        {
            return new ShopEconomySnapshot(
                shopId,
                new InventorySnapshot(items),
                new WalletSnapshot(balance),
                lastRestockedDay,
                restockAlgorithmVersion);
        }

        private static int Quantity(InventorySnapshot snapshot, string itemId)
        {
            ItemStack stack = snapshot.Items.Single(item => item.ItemId == itemId);
            return stack.Quantity;
        }
    }
}
