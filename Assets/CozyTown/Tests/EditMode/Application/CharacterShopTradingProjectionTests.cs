using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class CharacterShopTradingProjectionTests
    {
        [Test]
        public void GetCurrentState_WhenAssetsAndOffersDiffer_ProjectsOnlyExecutableRows()
        {
            IEconomyStateStore stateStore = new InMemoryEconomyStateStore(
                new[]
                {
                    new CharacterEconomySnapshot(
                        "character.player",
                        new InventorySnapshot(
                            new[]
                            {
                                new ItemStack("seed.potato", 1),
                                new ItemStack("fish.carp", 2),
                                new ItemStack("item.unconfigured", 4)
                            }),
                        new WalletSnapshot(100))
                },
                new[]
                {
                    new ShopEconomySnapshot(
                        "shop.town.general",
                        new InventorySnapshot(
                            new[]
                            {
                                new ItemStack("seed.potato", 3),
                                new ItemStack("fish.carp", 5),
                                new ItemStack("item.unconfigured", 7)
                            }),
                        new WalletSnapshot(1000),
                        lastRestockedDay: 1,
                        restockAlgorithmVersion: 1)
                });
            ICharacterShopTradingCoordinator coordinator =
                new CharacterShopTradingCoordinator(
                    new[]
                    {
                        Item("seed.potato", "Potato Seed", ItemCategory.Seed),
                        Item("ingredient.flour", "Flour", ItemCategory.Material),
                        Item("fish.carp", "Carp", ItemCategory.Fish),
                        Item("produce.egg", "Egg", ItemCategory.AnimalProduct)
                    },
                    new[]
                    {
                        new ShopOffer("seed.potato", buyPrice: 20, sellPrice: 5),
                        new ShopOffer("ingredient.flour", buyPrice: 10, sellPrice: 0),
                        new ShopOffer("fish.carp", buyPrice: 0, sellPrice: 25),
                        new ShopOffer("produce.egg", buyPrice: 0, sellPrice: 20)
                    },
                    backpackCapacitySlots: 12,
                    stateStore);

            OperationResult<ShopTradingViewState> result = coordinator.GetCurrentState(
                "shop.town.general",
                "character.player");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.CharacterBalance, Is.EqualTo(100));
            Assert.That(result.Value.ShopBalance, Is.EqualTo(1000));
            Assert.That(result.Value.PurchaseItems, Has.Count.EqualTo(1));
            Assert.That(result.Value.PurchaseItems[0].ItemId, Is.EqualTo("seed.potato"));
            Assert.That(result.Value.PurchaseItems[0].DisplayName, Is.EqualTo("Potato Seed"));
            Assert.That(result.Value.PurchaseItems[0].UnitPrice, Is.EqualTo(20));
            Assert.That(result.Value.PurchaseItems[0].Quantity, Is.EqualTo(3));
            Assert.That(result.Value.SaleItems, Has.Count.EqualTo(2));
            Assert.That(result.Value.SaleItems[0].ItemId, Is.EqualTo("fish.carp"));
            Assert.That(result.Value.SaleItems[0].UnitPrice, Is.EqualTo(25));
            Assert.That(result.Value.SaleItems[0].Quantity, Is.EqualTo(2));
            Assert.That(result.Value.SaleItems[1].ItemId, Is.EqualTo("seed.potato"));
            Assert.That(result.Value.SaleItems[1].UnitPrice, Is.EqualTo(5));
            Assert.That(result.Value.SaleItems[1].Quantity, Is.EqualTo(1));
        }

        [Test]
        public void GetCurrentState_AfterSuccessfulTrades_ReflectsCommittedAssets()
        {
            IEconomyStateStore stateStore = new InMemoryEconomyStateStore(
                new[]
                {
                    new CharacterEconomySnapshot(
                        "character.player",
                        new InventorySnapshot(new ItemStack[0]),
                        new WalletSnapshot(100))
                },
                new[]
                {
                    new ShopEconomySnapshot(
                        "shop.town.general",
                        new InventorySnapshot(
                            new[] { new ItemStack("seed.potato", 3) }),
                        new WalletSnapshot(1000),
                        lastRestockedDay: 1,
                        restockAlgorithmVersion: 1)
                });
            ICharacterShopTradingCoordinator coordinator =
                new CharacterShopTradingCoordinator(
                    new[] { Item("seed.potato", "Potato Seed", ItemCategory.Seed) },
                    new[]
                    {
                        new ShopOffer("seed.potato", buyPrice: 20, sellPrice: 10)
                    },
                    backpackCapacitySlots: 12,
                    stateStore);

            OperationResult<ShopReceipt> purchase = coordinator.Buy(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 1);
            OperationResult<ShopTradingViewState> afterPurchase =
                coordinator.GetCurrentState("shop.town.general", "character.player");

            Assert.That(purchase.IsSuccess, Is.True);
            Assert.That(afterPurchase.IsSuccess, Is.True);
            Assert.That(afterPurchase.Value.CharacterBalance, Is.EqualTo(80));
            Assert.That(afterPurchase.Value.ShopBalance, Is.EqualTo(1020));
            Assert.That(afterPurchase.Value.PurchaseItems[0].Quantity, Is.EqualTo(2));
            Assert.That(afterPurchase.Value.SaleItems[0].Quantity, Is.EqualTo(1));

            OperationResult<ShopReceipt> sale = coordinator.Sell(
                "shop.town.general",
                "character.player",
                "seed.potato",
                quantity: 1);
            OperationResult<ShopTradingViewState> afterSale =
                coordinator.GetCurrentState("shop.town.general", "character.player");

            Assert.That(sale.IsSuccess, Is.True);
            Assert.That(afterSale.IsSuccess, Is.True);
            Assert.That(afterSale.Value.CharacterBalance, Is.EqualTo(90));
            Assert.That(afterSale.Value.ShopBalance, Is.EqualTo(1010));
            Assert.That(afterSale.Value.PurchaseItems[0].Quantity, Is.EqualTo(3));
            Assert.That(afterSale.Value.SaleItems, Is.Empty);
        }

        [TestCase(
            "shop.unknown",
            "character.player",
            "economy.shop_unknown")]
        [TestCase(
            "shop.town.general",
            "character.unknown",
            "economy.character_unknown")]
        public void GetCurrentState_WhenIdentityIsUnknown_ReturnsFailure(
            string shopId,
            string characterId,
            string expectedError)
        {
            IEconomyStateStore stateStore = new InMemoryEconomyStateStore(
                new[]
                {
                    new CharacterEconomySnapshot(
                        "character.player",
                        new InventorySnapshot(new ItemStack[0]),
                        new WalletSnapshot(100))
                },
                new[]
                {
                    new ShopEconomySnapshot(
                        "shop.town.general",
                        new InventorySnapshot(new ItemStack[0]),
                        new WalletSnapshot(1000),
                        lastRestockedDay: 1,
                        restockAlgorithmVersion: 1)
                });
            ICharacterShopTradingCoordinator coordinator =
                new CharacterShopTradingCoordinator(
                    new[] { Item("seed.potato", "Potato Seed", ItemCategory.Seed) },
                    new[]
                    {
                        new ShopOffer("seed.potato", buyPrice: 20, sellPrice: 10)
                    },
                    backpackCapacitySlots: 12,
                    stateStore);

            OperationResult<ShopTradingViewState> result = coordinator.GetCurrentState(
                shopId,
                characterId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
        }

        private static ItemDefinition Item(
            string itemId,
            string displayName,
            ItemCategory category)
        {
            return new ItemDefinition(itemId, displayName, category, maxStack: 99);
        }
    }
}
