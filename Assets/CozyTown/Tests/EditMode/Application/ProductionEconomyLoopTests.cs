using System;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class ProductionEconomyLoopTests
    {
        [Test]
        public void DefaultGame_BuyProduceCookSellAndRebuy_ConservesOrdinaryTrades()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);
            Assert.That(services.Time.Current.Day, Is.EqualTo(2));

            AssertConservedTrade(
                services,
                DefaultMvpIds.Items.PotatoSeed,
                expectedCharacterBalance: 280,
                expectedShopBalance: 10020,
                expectedCharacterQuantity: 1,
                expectedShopQuantity: 2,
                () => services.ShopTrading.Buy(
                    DefaultMvpIds.Shops.TownGeneral,
                    DefaultMvpIds.Characters.Player,
                    DefaultMvpIds.Items.PotatoSeed,
                    1));
            AssertConservedTrade(
                services,
                DefaultMvpIds.Items.ChickenFeed,
                expectedCharacterBalance: 270,
                expectedShopBalance: 10030,
                expectedCharacterQuantity: 1,
                expectedShopQuantity: 8,
                () => services.ShopTrading.Buy(
                    DefaultMvpIds.Shops.TownGeneral,
                    DefaultMvpIds.Characters.Player,
                    DefaultMvpIds.Items.ChickenFeed,
                    1));
            AssertConservedTrade(
                services,
                DefaultMvpIds.Items.Salt,
                expectedCharacterBalance: 260,
                expectedShopBalance: 10040,
                expectedCharacterQuantity: 2,
                expectedShopQuantity: 5,
                () => services.ShopTrading.Buy(
                    DefaultMvpIds.Shops.TownGeneral,
                    DefaultMvpIds.Characters.Player,
                    DefaultMvpIds.Items.Salt,
                    2));

            Assert.That(
                services.FarmGameplay.Plant("plot.01", DefaultMvpIds.Items.PotatoSeed)
                    .IsSuccess,
                Is.True);
            Assert.That(services.FarmGameplay.Water("plot.01").IsSuccess, Is.True);
            Assert.That(
                services.LivestockGameplay.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess,
                Is.True);
            Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);

            Assert.That(services.Time.Current.Day, Is.EqualTo(3));
            Assert.That(
                services.Farm.Plots.Single(plot => plot.PlotId == "plot.01")
                    .GrowthProgressDays,
                Is.EqualTo(1));
            Assert.That(services.Livestock.Animals.Single().ProductReady, Is.True);
            Assert.That(
                services.LivestockGameplay.CollectProduct(DefaultMvpIds.Livestock.Hen)
                    .IsSuccess,
                Is.True);

            Assert.That(services.FarmGameplay.Water("plot.01").IsSuccess, Is.True);
            Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);
            Assert.That(services.Time.Current.Day, Is.EqualTo(4));
            Assert.That(
                services.Farm.Plots.Single(plot => plot.PlotId == "plot.01").Status,
                Is.EqualTo(FarmPlotStatus.Ready));
            Assert.That(services.FarmGameplay.Harvest("plot.01").IsSuccess, Is.True);

            Assert.That(services.FishingGameplay.Catch(0).IsSuccess, Is.True);
            Assert.That(
                services.CookingGameplay.Cook(DefaultMvpIds.Recipes.BakedPotato)
                    .IsSuccess,
                Is.True);
            Assert.That(
                services.CookingGameplay.Cook(DefaultMvpIds.Recipes.GrilledFish)
                    .IsSuccess,
                Is.True);

            AssertConservedTrade(
                services,
                DefaultMvpIds.Items.BakedPotato,
                expectedCharacterBalance: 310,
                expectedShopBalance: 9990,
                expectedCharacterQuantity: 0,
                expectedShopQuantity: 1,
                () => services.ShopTrading.Sell(
                    DefaultMvpIds.Shops.TownGeneral,
                    DefaultMvpIds.Characters.Player,
                    DefaultMvpIds.Items.BakedPotato,
                    1));
            AssertConservedTrade(
                services,
                DefaultMvpIds.Items.GrilledFish,
                expectedCharacterBalance: 365,
                expectedShopBalance: 9935,
                expectedCharacterQuantity: 0,
                expectedShopQuantity: 1,
                () => services.ShopTrading.Sell(
                    DefaultMvpIds.Shops.TownGeneral,
                    DefaultMvpIds.Characters.Player,
                    DefaultMvpIds.Items.GrilledFish,
                    1));
            AssertConservedTrade(
                services,
                DefaultMvpIds.Items.Egg,
                expectedCharacterBalance: 385,
                expectedShopBalance: 9915,
                expectedCharacterQuantity: 0,
                expectedShopQuantity: 1,
                () => services.ShopTrading.Sell(
                    DefaultMvpIds.Shops.TownGeneral,
                    DefaultMvpIds.Characters.Player,
                    DefaultMvpIds.Items.Egg,
                    1));
            AssertConservedTrade(
                services,
                DefaultMvpIds.Items.PotatoSeed,
                expectedCharacterBalance: 365,
                expectedShopBalance: 9935,
                expectedCharacterQuantity: 1,
                expectedShopQuantity: 3,
                () => services.ShopTrading.Buy(
                    DefaultMvpIds.Shops.TownGeneral,
                    DefaultMvpIds.Characters.Player,
                    DefaultMvpIds.Items.PotatoSeed,
                    1));

            Assert.That(services.Wallet.Balance, Is.EqualTo(365));
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.PotatoSeed), Is.EqualTo(1));
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.ChickenFeed), Is.Zero);
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.Salt), Is.Zero);
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.Carp), Is.Zero);
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.Egg), Is.Zero);
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.Potato), Is.EqualTo(1));
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.BakedPotato), Is.Zero);
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.GrilledFish), Is.Zero);
            Assert.That(
                services.Farm.Plots.Single(plot => plot.PlotId == "plot.01").Status,
                Is.EqualTo(FarmPlotStatus.Empty));
            Assert.That(services.Livestock.Animals.Single().ProductReady, Is.False);
        }

        private static void AssertConservedTrade(
            CozyTownServices services,
            string itemId,
            int expectedCharacterBalance,
            int expectedShopBalance,
            int expectedCharacterQuantity,
            int expectedShopQuantity,
            Func<OperationResult<ShopReceipt>> trade)
        {
            GetTradeParties(
                services,
                out CharacterEconomySnapshot characterBefore,
                out ShopEconomySnapshot shopBefore);
            int coinsBefore = characterBefore.Wallet.Balance + shopBefore.Wallet.Balance;
            string[] inventoryTotalsBefore = CombinedInventoryTotals(
                characterBefore,
                shopBefore);

            Assert.That(trade().IsSuccess, Is.True);

            GetTradeParties(
                services,
                out CharacterEconomySnapshot characterAfter,
                out ShopEconomySnapshot shopAfter);
            Assert.That(characterAfter.Wallet.Balance, Is.EqualTo(expectedCharacterBalance));
            Assert.That(shopAfter.Wallet.Balance, Is.EqualTo(expectedShopBalance));
            Assert.That(
                characterAfter.Wallet.Balance + shopAfter.Wallet.Balance,
                Is.EqualTo(10300));
            Assert.That(
                characterAfter.Wallet.Balance + shopAfter.Wallet.Balance,
                Is.EqualTo(coinsBefore));
            Assert.That(
                Quantity(characterAfter.Backpack, itemId),
                Is.EqualTo(expectedCharacterQuantity));
            Assert.That(
                Quantity(shopAfter.Stock, itemId),
                Is.EqualTo(expectedShopQuantity));
            Assert.That(
                Quantity(characterAfter.Backpack, itemId)
                    + Quantity(shopAfter.Stock, itemId),
                Is.EqualTo(expectedCharacterQuantity + expectedShopQuantity));
            Assert.That(
                CombinedInventoryTotals(characterAfter, shopAfter),
                Is.EqualTo(inventoryTotalsBefore));
        }

        private static void GetTradeParties(
            CozyTownServices services,
            out CharacterEconomySnapshot character,
            out ShopEconomySnapshot shop)
        {
            Assert.That(
                services.EconomyState.TryGetCharacter(
                    DefaultMvpIds.Characters.Player,
                    out character),
                Is.True);
            Assert.That(
                services.EconomyState.TryGetShop(
                    DefaultMvpIds.Shops.TownGeneral,
                    out shop),
                Is.True);
        }

        private static int Quantity(InventorySnapshot inventory, string itemId)
        {
            return inventory.Items
                .Where(item => item.ItemId == itemId)
                .Select(item => item.Quantity)
                .SingleOrDefault();
        }

        private static string[] CombinedInventoryTotals(
            CharacterEconomySnapshot character,
            ShopEconomySnapshot shop)
        {
            return character.Backpack.Items
                .Concat(shop.Stock.Items)
                .GroupBy(item => item.ItemId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}:{group.Sum(item => item.Quantity)}")
                .ToArray();
        }
    }
}
