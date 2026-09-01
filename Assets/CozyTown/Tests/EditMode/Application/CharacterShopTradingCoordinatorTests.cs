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

        [Test]
        public void Sell_WhenSaleIsValid_TransfersItemsAndMoneyWithoutCreatingAssets()
        {
            IEconomyStateStore stateStore = new InMemoryEconomyStateStore(
                new[]
                {
                    Character(
                        "character.player",
                        balance: 100,
                        new ItemStack("seed.potato", 3))
                },
                new[]
                {
                    Shop("shop.town.general", balance: 1000)
                });
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Sell(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 2);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.ItemId, Is.EqualTo("seed.potato"));
            Assert.That(result.Value.Quantity, Is.EqualTo(2));
            Assert.That(result.Value.TotalPrice, Is.EqualTo(20));
            Assert.That(result.Value.IsPurchase, Is.False);
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
            Assert.That(character.Wallet.Balance, Is.EqualTo(120));
            Assert.That(Quantity(character.Backpack, "seed.potato"), Is.EqualTo(1));
            Assert.That(shop.Wallet.Balance, Is.EqualTo(980));
            Assert.That(Quantity(shop.Stock, "seed.potato"), Is.EqualTo(2));
            Assert.That(character.Wallet.Balance + shop.Wallet.Balance, Is.EqualTo(1100));
            Assert.That(
                Quantity(character.Backpack, "seed.potato")
                    + Quantity(shop.Stock, "seed.potato"),
                Is.EqualTo(3));
        }

        [TestCase(
            "shop.unknown",
            "character.player",
            "seed.potato",
            "economy.shop_unknown")]
        [TestCase(
            "shop.town.general",
            "character.unknown",
            "seed.potato",
            "economy.character_unknown")]
        [TestCase(
            "shop.town.general",
            "character.player",
            "seed.unknown",
            "shop.offer_missing")]
        public void Buy_WhenIdentifierIsUnknown_LeavesKnownStateUnchanged(
            string shopId,
            string characterId,
            string itemId,
            string expectedError)
        {
            IEconomyStateStore stateStore = DefaultStateStore();
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Buy(
                shopId,
                characterId,
                itemId,
                quantity: 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
            AssertDefaultState(stateStore);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Buy_WhenQuantityIsNotPositive_LeavesBothSubjectsUnchanged(int quantity)
        {
            IEconomyStateStore stateStore = DefaultStateStore();
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Buy(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("shop.quantity_invalid"));
            AssertDefaultState(stateStore);
        }

        [Test]
        public void Buy_WhenShopStockIsInsufficient_LeavesBothSubjectsUnchanged()
        {
            IEconomyStateStore stateStore = DefaultStateStore();
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Buy(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 4);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("inventory.insufficient_quantity"));
            AssertDefaultState(stateStore);
        }

        [Test]
        public void Buy_WhenCharacterFundsAreInsufficient_LeavesBothSubjectsUnchanged()
        {
            IEconomyStateStore stateStore = DefaultStateStore(characterBalance: 10);
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Buy(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("wallet.insufficient_funds"));
            AssertDefaultState(stateStore, characterBalance: 10);
        }

        [Test]
        public void Buy_WhenOfferPriceIsInvalid_LeavesBothSubjectsUnchanged()
        {
            IEconomyStateStore stateStore = DefaultStateStore();
            ICharacterShopTradingCoordinator coordinator = Coordinator(
                stateStore,
                buyPrice: 0);

            OperationResult<ShopReceipt> result = coordinator.Buy(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("shop.item_not_for_sale"));
            AssertDefaultState(stateStore);
        }

        [Test]
        public void Buy_WhenBackpackHasNoCapacity_LeavesBothSubjectsUnchanged()
        {
            IEconomyStateStore stateStore = DefaultStateStore(
                characterItems: new[] { new ItemStack("fish.carp", 1) });
            ICharacterShopTradingCoordinator coordinator = Coordinator(
                stateStore,
                backpackCapacitySlots: 1);

            OperationResult<ShopReceipt> result = coordinator.Buy(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("inventory.capacity_exceeded"));
            AssertDefaultState(
                stateStore,
                characterItems: new[] { new ItemStack("fish.carp", 1) });
        }

        [Test]
        public void Buy_WhenCommitIsRejected_LeavesBothSubjectsUnchanged()
        {
            IEconomyStateStore stateStore = new RejectingEconomyStateStore(
                DefaultStateStore());
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Buy(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("economy.commit_rejected"));
            AssertDefaultState(stateStore);
        }

        private static IEconomyStateStore DefaultStateStore(
            int characterBalance = 100,
            ItemStack[] characterItems = null)
        {
            return new InMemoryEconomyStateStore(
                new[]
                {
                    Character(
                        "character.player",
                        characterBalance,
                        characterItems ?? new ItemStack[0])
                },
                new[]
                {
                    Shop(
                        "shop.town.general",
                        balance: 1000,
                        new ItemStack("seed.potato", 3))
                });
        }

        private static ICharacterShopTradingCoordinator Coordinator(
            IEconomyStateStore stateStore,
            int buyPrice = 20,
            int backpackCapacitySlots = 12)
        {
            return new CharacterShopTradingCoordinator(
                new[]
                {
                    new ItemDefinition(
                        "seed.potato",
                        "Potato Seed",
                        ItemCategory.Seed,
                        maxStack: 99),
                    new ItemDefinition(
                        "fish.carp",
                        "Carp",
                        ItemCategory.Fish,
                        maxStack: 99)
                },
                new[]
                {
                    new ShopOffer("seed.potato", buyPrice, sellPrice: 10)
                },
                backpackCapacitySlots,
                stateStore);
        }

        private static void AssertDefaultState(
            IEconomyStateStore stateStore,
            int characterBalance = 100,
            ItemStack[] characterItems = null)
        {
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
            Assert.That(character.Wallet.Balance, Is.EqualTo(characterBalance));
            Assert.That(
                character.Backpack.Items,
                Is.EquivalentTo(characterItems ?? new ItemStack[0]));
            Assert.That(shop.Wallet.Balance, Is.EqualTo(1000));
            Assert.That(Quantity(shop.Stock, "seed.potato"), Is.EqualTo(3));
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

        private sealed class RejectingEconomyStateStore : IEconomyStateStore
        {
            private readonly IEconomyStateStore _inner;

            public RejectingEconomyStateStore(IEconomyStateStore inner)
            {
                _inner = inner;
            }

            public bool TryGetCharacter(
                string characterId,
                out CharacterEconomySnapshot snapshot)
            {
                return _inner.TryGetCharacter(characterId, out snapshot);
            }

            public bool TryGetShop(string shopId, out ShopEconomySnapshot snapshot)
            {
                return _inner.TryGetShop(shopId, out snapshot);
            }

            public OperationResult Commit(
                CharacterEconomySnapshot characterCandidate,
                ShopEconomySnapshot shopCandidate)
            {
                return OperationResult.Failure("economy.commit_rejected");
            }

            public OperationResult CommitShop(ShopEconomySnapshot shopCandidate)
            {
                return _inner.CommitShop(shopCandidate);
            }
        }
    }
}
