using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Farming;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class ProductionEconomyLoopTests
    {
        [Test]
        public void DefaultGame_BuyProduceCookAndSell_CompletesDeterministicEconomyLoop()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();

            Assert.That(
                services.ShopTrading.Buy(DefaultMvpIds.Items.PotatoSeed, 1).IsSuccess,
                Is.True);
            Assert.That(
                services.ShopTrading.Buy(DefaultMvpIds.Items.ChickenFeed, 1).IsSuccess,
                Is.True);
            Assert.That(
                services.ShopTrading.Buy(DefaultMvpIds.Items.Salt, 2).IsSuccess,
                Is.True);
            Assert.That(services.Wallet.Balance, Is.EqualTo(260));

            Assert.That(
                services.FarmGameplay.Plant("plot.01", DefaultMvpIds.Items.PotatoSeed)
                    .IsSuccess,
                Is.True);
            Assert.That(services.FarmGameplay.Water("plot.01").IsSuccess, Is.True);
            Assert.That(
                services.LivestockGameplay.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess,
                Is.True);
            Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);

            Assert.That(services.Time.Current.Day, Is.EqualTo(2));
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
            Assert.That(services.Time.Current.Day, Is.EqualTo(3));
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

            Assert.That(
                services.ShopTrading.Sell(DefaultMvpIds.Items.BakedPotato, 1).IsSuccess,
                Is.True);
            Assert.That(
                services.ShopTrading.Sell(DefaultMvpIds.Items.GrilledFish, 1).IsSuccess,
                Is.True);
            Assert.That(
                services.ShopTrading.Sell(DefaultMvpIds.Items.Egg, 1).IsSuccess,
                Is.True);
            Assert.That(services.Wallet.Balance, Is.EqualTo(385));
            Assert.That(
                services.ShopTrading.Buy(DefaultMvpIds.Items.PotatoSeed, 1).IsSuccess,
                Is.True);

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
    }
}
