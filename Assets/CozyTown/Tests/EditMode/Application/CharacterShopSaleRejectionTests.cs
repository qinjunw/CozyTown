using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class CharacterShopSaleRejectionTests
    {
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
        public void Sell_WhenIdentifierIsUnknown_LeavesKnownStateUnchanged(
            string shopId,
            string characterId,
            string itemId,
            string expectedError)
        {
            IEconomyStateStore stateStore = StateStore();
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Sell(
                shopId,
                characterId,
                itemId,
                quantity: 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
            AssertInitialState(stateStore);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Sell_WhenQuantityIsNotPositive_LeavesBothSubjectsUnchanged(int quantity)
        {
            IEconomyStateStore stateStore = StateStore();
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Sell(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("shop.quantity_invalid"));
            AssertInitialState(stateStore);
        }

        [Test]
        public void Sell_WhenCharacterItemsAreInsufficient_LeavesBothSubjectsUnchanged()
        {
            IEconomyStateStore stateStore = StateStore();
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Sell(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 4);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("inventory.insufficient_quantity"));
            AssertInitialState(stateStore);
        }

        [Test]
        public void Sell_WhenShopFundsAreInsufficient_LeavesBothSubjectsUnchanged()
        {
            IEconomyStateStore stateStore = StateStore(shopBalance: 5);
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Sell(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("wallet.insufficient_funds"));
            AssertInitialState(stateStore, shopBalance: 5);
        }

        [Test]
        public void Sell_WhenBuybackPriceIsInvalid_LeavesBothSubjectsUnchanged()
        {
            IEconomyStateStore stateStore = StateStore();
            ICharacterShopTradingCoordinator coordinator = Coordinator(
                stateStore,
                sellPrice: 0);

            OperationResult<ShopReceipt> result = coordinator.Sell(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("shop.item_not_accepted"));
            AssertInitialState(stateStore);
        }

        [Test]
        public void Sell_WhenCommitIsRejected_LeavesBothSubjectsUnchanged()
        {
            IEconomyStateStore stateStore = new RejectingEconomyStateStore(StateStore());
            ICharacterShopTradingCoordinator coordinator = Coordinator(stateStore);

            OperationResult<ShopReceipt> result = coordinator.Sell(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("economy.commit_rejected"));
            AssertInitialState(stateStore);
        }

        private static IEconomyStateStore StateStore(int shopBalance = 1000)
        {
            return new InMemoryEconomyStateStore(
                new[]
                {
                    new CharacterEconomySnapshot(
                        "character.player",
                        new InventorySnapshot(
                            new[] { new ItemStack("seed.potato", 3) }),
                        new WalletSnapshot(100))
                },
                new[]
                {
                    new ShopEconomySnapshot(
                        "shop.town.general",
                        new InventorySnapshot(new ItemStack[0]),
                        new WalletSnapshot(shopBalance),
                        lastRestockedDay: 1,
                        restockAlgorithmVersion: 1)
                });
        }

        private static ICharacterShopTradingCoordinator Coordinator(
            IEconomyStateStore stateStore,
            int sellPrice = 10)
        {
            return new CharacterShopTradingCoordinator(
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
                    new ShopOffer("seed.potato", buyPrice: 20, sellPrice)
                },
                backpackCapacitySlots: 12,
                stateStore);
        }

        private static void AssertInitialState(
            IEconomyStateStore stateStore,
            int shopBalance = 1000)
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
            Assert.That(character.Wallet.Balance, Is.EqualTo(100));
            Assert.That(Quantity(character.Backpack, "seed.potato"), Is.EqualTo(3));
            Assert.That(shop.Wallet.Balance, Is.EqualTo(shopBalance));
            Assert.That(Quantity(shop.Stock, "seed.potato"), Is.EqualTo(0));
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
