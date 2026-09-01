using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Economy
{
    public sealed class DeterministicShopStockReplacementPolicyTests
    {
        [Test]
        public void CreateCandidate_WithFixedVersionOneInputs_ReturnsLockedStockAndPreservesWallet()
        {
            IShopStockReplacementPolicy policy =
                new DeterministicShopStockReplacementPolicy(
                    DefaultRules(),
                    minimumDistinctItems: 4);
            var current = new ShopEconomySnapshot(
                "shop.town.general",
                new InventorySnapshot(
                    new[] { new ItemStack("fish.carp", 2) }),
                new WalletSnapshot(10000),
                lastRestockedDay: 1,
                restockAlgorithmVersion: 1);

            OperationResult<ShopEconomySnapshot> result = policy.CreateCandidate(
                worldSeed: 12345,
                current,
                targetDay: 2);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value.Wallet.Balance, Is.EqualTo(10000));
            Assert.That(result.Value.LastRestockedDay, Is.EqualTo(2));
            Assert.That(result.Value.RestockAlgorithmVersion, Is.EqualTo(1));
            CollectionAssert.AreEqual(
                new[]
                {
                    "seed.potato:3",
                    "seed.carrot:3",
                    "seed.tomato:3",
                    "feed.chicken:9",
                    "ingredient.salt:7"
                },
                result.Value.Stock.Items
                    .Select(stack => $"{stack.ItemId}:{stack.Quantity}")
                    .ToArray());
            Assert.That(
                result.Value.Stock.Items.Any(stack => stack.ItemId == "fish.carp"),
                Is.False);
        }

        [Test]
        public void CreateCandidate_WhenDayWasAlreadyRestocked_PreservesActualTradedStock()
        {
            IShopStockReplacementPolicy policy = CreatePolicy();
            var current = Shop(
                new[]
                {
                    new ItemStack("seed.potato", 1),
                    new ItemStack("fish.carp", 3)
                },
                balance: 9876,
                lastRestockedDay: 2,
                algorithmVersion: 1);

            OperationResult<ShopEconomySnapshot> result = policy.CreateCandidate(
                worldSeed: 12345,
                current,
                targetDay: 2);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value, Is.Not.SameAs(current));
            Assert.That(result.Value.Wallet.Balance, Is.EqualTo(9876));
            CollectionAssert.AreEqual(
                new[] { "seed.potato:1", "fish.carp:3" },
                StackValues(result.Value));
        }

        [Test]
        public void CreateCandidate_WhenInitialSelectionIsSparse_SupplementsToMinimumInRuleOrder()
        {
            IShopStockReplacementPolicy policy = CreatePolicy();

            OperationResult<ShopEconomySnapshot> result = policy.CreateCandidate(
                worldSeed: 4,
                Shop(new ItemStack[0], 10000, 1, 1),
                targetDay: 2);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            CollectionAssert.AreEqual(
                new[]
                {
                    "seed.potato:4",
                    "seed.carrot:6",
                    "feed.chicken:9",
                    "ingredient.salt:7"
                },
                StackValues(result.Value));
        }

        [Test]
        public void CreateCandidate_CalledTwiceWithSameInputs_ReturnsSameStock()
        {
            IShopStockReplacementPolicy policy = CreatePolicy();
            ShopEconomySnapshot current = Shop(new ItemStack[0], 10000, 1, 1);

            OperationResult<ShopEconomySnapshot> first = policy.CreateCandidate(
                worldSeed: -214,
                current,
                targetDay: 17);
            OperationResult<ShopEconomySnapshot> second = policy.CreateCandidate(
                worldSeed: -214,
                current,
                targetDay: 17);

            Assert.That(first.IsSuccess, Is.True, first.ErrorCode);
            Assert.That(second.IsSuccess, Is.True, second.ErrorCode);
            CollectionAssert.AreEqual(StackValues(first.Value), StackValues(second.Value));
        }

        [TestCase(0, 1, 1, "shop_restock.day_invalid")]
        [TestCase(1, 2, 1, "shop_restock.day_regressed")]
        [TestCase(2, 1, 2, "shop_restock.algorithm_unsupported")]
        public void CreateCandidate_WhenRequestIsUnsupported_RejectsWithoutCandidate(
            int targetDay,
            int lastRestockedDay,
            int algorithmVersion,
            string expectedError)
        {
            IShopStockReplacementPolicy policy = CreatePolicy();

            OperationResult<ShopEconomySnapshot> result = policy.CreateCandidate(
                worldSeed: 12345,
                Shop(new ItemStack[0], 10000, lastRestockedDay, algorithmVersion),
                targetDay);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
            Assert.That(result.Value, Is.Null);
        }

        private static IShopStockReplacementPolicy CreatePolicy()
        {
            return new DeterministicShopStockReplacementPolicy(
                DefaultRules(),
                minimumDistinctItems: 4);
        }

        private static ShopEconomySnapshot Shop(
            ItemStack[] stock,
            int balance,
            int lastRestockedDay,
            int algorithmVersion)
        {
            return new ShopEconomySnapshot(
                "shop.town.general",
                new InventorySnapshot(stock),
                new WalletSnapshot(balance),
                lastRestockedDay,
                algorithmVersion);
        }

        private static string[] StackValues(ShopEconomySnapshot shop)
        {
            return shop.Stock.Items
                .Select(stack => $"{stack.ItemId}:{stack.Quantity}")
                .ToArray();
        }

        private static ShopRestockRule[] DefaultRules()
        {
            return new[]
            {
                new ShopRestockRule("seed.potato", 700, 3, 6),
                new ShopRestockRule("seed.carrot", 700, 3, 6),
                new ShopRestockRule("seed.tomato", 700, 3, 6),
                new ShopRestockRule("feed.chicken", 1000, 6, 12),
                new ShopRestockRule("ingredient.salt", 750, 3, 8),
                new ShopRestockRule("ingredient.flour", 750, 3, 8)
            };
        }
    }
}
