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

        [Test]
        public void CommitShop_WhenCandidateIsValid_PublishesOnlyThatShop()
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

            OperationResult result = store.CommitShop(
                Shop(
                    "shop.town.general",
                    balance: 10000,
                    lastRestockedDay: 2,
                    restockAlgorithmVersion: 1,
                    new ItemStack("seed.carrot", 4)));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                store.TryGetCharacter("character.player", out CharacterEconomySnapshot character),
                Is.True);
            Assert.That(character.Wallet.Balance, Is.EqualTo(300));
            Assert.That(Quantity(character.Backpack, "seed.potato"), Is.EqualTo(1));
            Assert.That(
                store.TryGetShop("shop.town.general", out ShopEconomySnapshot shop),
                Is.True);
            Assert.That(shop.LastRestockedDay, Is.EqualTo(2));
            Assert.That(Quantity(shop.Stock, "seed.carrot"), Is.EqualTo(4));
        }

        [Test]
        public void CommitShop_WhenCandidateIsInvalid_LeavesStoredShopUnchanged()
        {
            IEconomyStateStore store = new InMemoryEconomyStateStore(
                new CharacterEconomySnapshot[0],
                new[]
                {
                    Shop(
                        "shop.town.general",
                        balance: 10000,
                        lastRestockedDay: 1,
                        restockAlgorithmVersion: 1,
                        new ItemStack("seed.potato", 6))
                });

            OperationResult result = store.CommitShop(
                Shop(
                    "shop.town.general",
                    balance: -1,
                    lastRestockedDay: 2,
                    restockAlgorithmVersion: 1,
                    new ItemStack("seed.carrot", 4)));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("economy.shop_invalid"));
            Assert.That(
                store.TryGetShop("shop.town.general", out ShopEconomySnapshot shop),
                Is.True);
            Assert.That(shop.Wallet.Balance, Is.EqualTo(10000));
            Assert.That(shop.LastRestockedDay, Is.EqualTo(1));
            Assert.That(Quantity(shop.Stock, "seed.potato"), Is.EqualTo(6));
        }

        [Test]
        public void Restore_WhenWholeSnapshotIsValid_PublishesAllOwnedState()
        {
            IEconomyStateStore store = new InMemoryEconomyStateStore(
                new[] { Character("character.player", 300, new ItemStack("seed.potato", 1)) },
                new[] { Shop("shop.town.general", 10000, 1, 1, new ItemStack("seed.potato", 6)) },
                Catalog(),
                characterBackpackCapacitySlots: 2);
            var replacement = new EconomyStateSnapshot(
                new[] { Character("character.player", 275, new ItemStack("seed.carrot", 2)) },
                new[] { Shop("shop.town.general", 10025, 2, 1, new ItemStack("seed.potato", 4)) });

            OperationResult result = store.Restore(replacement);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(store.TryGetCharacter("character.player", out CharacterEconomySnapshot character), Is.True);
            Assert.That(character.Wallet.Balance, Is.EqualTo(275));
            Assert.That(Quantity(character.Backpack, "seed.carrot"), Is.EqualTo(2));
            Assert.That(store.TryGetShop("shop.town.general", out ShopEconomySnapshot shop), Is.True);
            Assert.That(shop.Wallet.Balance, Is.EqualTo(10025));
            Assert.That(shop.LastRestockedDay, Is.EqualTo(2));
            Assert.That(Quantity(shop.Stock, "seed.potato"), Is.EqualTo(4));
        }

        [Test]
        public void Restore_WhenOneCharacterItemIsUnknown_PublishesNothing()
        {
            IEconomyStateStore store = new InMemoryEconomyStateStore(
                new[] { Character("character.player", 300, new ItemStack("seed.potato", 1)) },
                new[] { Shop("shop.town.general", 10000, 1, 1, new ItemStack("seed.potato", 6)) },
                Catalog(),
                characterBackpackCapacitySlots: 2);
            EconomyStateSnapshot before = store.CaptureSnapshot();
            var invalid = new EconomyStateSnapshot(
                new[] { Character("character.player", 1, new ItemStack("unknown", 1)) },
                new[] { Shop("shop.town.general", 10099, 2, 1, new ItemStack("seed.carrot", 4)) });

            OperationResult result = store.Restore(invalid);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("economy.character_invalid"));
            Assert.That(store.CaptureSnapshot().Characters[0].Wallet.Balance,
                Is.EqualTo(before.Characters[0].Wallet.Balance));
            Assert.That(store.CaptureSnapshot().Shops[0].Wallet.Balance,
                Is.EqualTo(before.Shops[0].Wallet.Balance));
            Assert.That(store.CaptureSnapshot().Shops[0].LastRestockedDay,
                Is.EqualTo(before.Shops[0].LastRestockedDay));
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

        private static ItemDefinition[] Catalog()
        {
            return new[]
            {
                new ItemDefinition("seed.potato", "Potato Seed", ItemCategory.Seed, 99),
                new ItemDefinition("seed.carrot", "Carrot Seed", ItemCategory.Seed, 99)
            };
        }
    }
}
