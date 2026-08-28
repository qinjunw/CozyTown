using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class ShopTradingCoordinatorTests
    {
        [Test]
        public void GetCurrentState_JoinsItemNamesAndOwnCountsInStableOrder()
        {
            var items = new[]
            {
                new ItemDefinition("z-item", "Z Item", ItemCategory.Material, 99),
                new ItemDefinition("a-item", "A Item", ItemCategory.Seed, 99)
            };
            var inventory = new InMemoryInventory(items, capacitySlots: 4);
            Assert.That(inventory.Add("z-item", 2).IsSuccess, Is.True);
            var wallet = new InMemoryWallet(startingBalance: 50);
            var shop = new InMemoryShopService(
                new[]
                {
                    new ShopOffer("z-item", buyPrice: 0, sellPrice: 7),
                    new ShopOffer("a-item", buyPrice: 3, sellPrice: 0)
                },
                wallet,
                inventory);
            var coordinator = new ShopTradingCoordinator(items, shop, wallet, inventory);
            items[0] = new ItemDefinition("changed", "Changed", ItemCategory.Food, 1);

            ShopViewState state = coordinator.GetCurrentState();

            Assert.That(state.Balance, Is.EqualTo(50));
            Assert.That(state.Items, Has.Count.EqualTo(2));
            Assert.That(state.Items[0].ItemId, Is.EqualTo("a-item"));
            Assert.That(state.Items[0].DisplayName, Is.EqualTo("A Item"));
            Assert.That(state.Items[0].BuyPrice, Is.EqualTo(3));
            Assert.That(state.Items[0].OwnedQuantity, Is.Zero);
            Assert.That(state.Items[1].ItemId, Is.EqualTo("z-item"));
            Assert.That(state.Items[1].DisplayName, Is.EqualTo("Z Item"));
            Assert.That(state.Items[1].SellPrice, Is.EqualTo(7));
            Assert.That(state.Items[1].OwnedQuantity, Is.EqualTo(2));
        }

        [Test]
        public void ShopViewState_WhenCallerChangesSourceArray_KeepsDefensiveCopy()
        {
            var original = new ShopLineItem("item", "Item", 4, 2, 1);
            var source = new[] { original };
            var state = new ShopViewState(balance: 10, source);

            source[0] = new ShopLineItem("changed", "Changed", 0, 0, 0);

            Assert.That(state.Items, Has.Count.EqualTo(1));
            Assert.That(state.Items[0], Is.SameAs(original));
            Assert.Throws<NotSupportedException>(() =>
                ((System.Collections.Generic.IList<ShopLineItem>)state.Items)[0] = source[0]);
        }

        [Test]
        public void BuyAndSell_DelegateToShopAndExposeUpdatedState()
        {
            var items = new[]
            {
                new ItemDefinition("item", "Item", ItemCategory.Material, 99)
            };
            var inventory = new InMemoryInventory(items, capacitySlots: 4);
            var wallet = new InMemoryWallet(startingBalance: 10);
            var shop = new InMemoryShopService(
                new[] { new ShopOffer("item", buyPrice: 4, sellPrice: 2) },
                wallet,
                inventory);
            var coordinator = new ShopTradingCoordinator(items, shop, wallet, inventory);

            OperationResult<ShopReceipt> buy = coordinator.Buy("item", 1);
            ShopViewState afterBuy = coordinator.GetCurrentState();
            OperationResult<ShopReceipt> sell = coordinator.Sell("item", 1);
            ShopViewState afterSell = coordinator.GetCurrentState();

            Assert.That(buy.IsSuccess, Is.True);
            Assert.That(afterBuy.Balance, Is.EqualTo(6));
            Assert.That(afterBuy.Items[0].OwnedQuantity, Is.EqualTo(1));
            Assert.That(sell.IsSuccess, Is.True);
            Assert.That(afterSell.Balance, Is.EqualTo(8));
            Assert.That(afterSell.Items[0].OwnedQuantity, Is.Zero);
        }
    }
}
