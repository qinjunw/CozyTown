using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Farming;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class GameplayCoordinatorTests
    {
        [Test]
        public void FarmGameplay_ProjectsDefinitionsAndUpdatesSharedFarm()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(
                services.ShopTrading.Buy(DefaultMvpIds.Items.PotatoSeed, 1).IsSuccess,
                Is.True);

            FarmViewState before = services.FarmGameplay.GetCurrentState();
            Assert.That(before.Plots.Select(plot => plot.PlotId), Is.Ordered);
            Assert.That(before.SeedOptions.Select(option => option.CropId), Is.Ordered);
            FarmSeedOption option = before.SeedOptions.Single(candidate =>
                candidate.SeedItemId == DefaultMvpIds.Items.PotatoSeed);
            Assert.That(option.DisplayName, Is.EqualTo("Potato Seed"));
            Assert.That(option.OwnedQuantity, Is.EqualTo(1));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<FarmPlotView>)before.Plots)[0] = null);

            Assert.That(
                services.FarmGameplay.Plant("plot.01", DefaultMvpIds.Items.PotatoSeed)
                    .IsSuccess,
                Is.True);
            FarmPlotView planted = services.FarmGameplay.GetCurrentState().Plots.Single(
                plot => plot.PlotId == "plot.01");

            Assert.That(planted.CropDisplayName, Is.EqualTo("Potato"));
            Assert.That(planted.Status, Is.EqualTo(FarmPlotStatus.Growing));
            Assert.That(
                services.Farm.Plots.Single(plot => plot.PlotId == "plot.01").CropId,
                Is.EqualTo(DefaultMvpIds.Crops.Potato));
        }

        [Test]
        public void LivestockGameplay_ProjectsDefinitionsAndUpdatesSharedAnimal()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(
                services.ShopTrading.Buy(DefaultMvpIds.Items.ChickenFeed, 1).IsSuccess,
                Is.True);

            LivestockViewState state = services.LivestockGameplay.GetCurrentState();
            AnimalView before = state.Animals.Single();
            Assert.That(before.FeedDisplayName, Is.EqualTo("Chicken Feed"));
            Assert.That(before.ProductDisplayName, Is.EqualTo("Egg"));
            Assert.That(before.OwnedFeedQuantity, Is.EqualTo(1));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<AnimalView>)state.Animals)[0] = null);

            Assert.That(
                services.LivestockGameplay.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess,
                Is.True);

            Assert.That(services.Livestock.Animals.Single().FedToday, Is.True);
            Assert.That(
                services.LivestockGameplay.GetCurrentState().Animals.Single().OwnedFeedQuantity,
                Is.Zero);
        }

        [Test]
        public void FishingGameplay_UsesStableViewAndUpdatesSharedInventory()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();

            FishingViewState before = services.FishingGameplay.GetCurrentState();
            Assert.That(before.Entries.Select(entry => entry.FishId), Is.Ordered);
            Assert.That(
                before.Entries.Single(entry => entry.ItemId == DefaultMvpIds.Items.Carp)
                    .DisplayName,
                Is.EqualTo("Carp"));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<FishingEntryView>)before.Entries)[0] = null);

            Assert.That(services.FishingGameplay.Catch(0).IsSuccess, Is.True);

            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.Carp), Is.EqualTo(1));
            Assert.That(
                services.FishingGameplay.GetCurrentState().Entries
                    .Single(entry => entry.ItemId == DefaultMvpIds.Items.Carp)
                    .OwnedQuantity,
                Is.EqualTo(1));
        }

        [Test]
        public void CookingGameplay_ProjectsIngredientsAndUpdatesSharedInventory()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.Inventory.Add(DefaultMvpIds.Items.Potato, 1).IsSuccess, Is.True);
            Assert.That(
                services.ShopTrading.Buy(DefaultMvpIds.Items.Salt, 1).IsSuccess,
                Is.True);

            CookingViewState state = services.CookingGameplay.GetCurrentState();
            RecipeView before = state.Recipes.Single(
                recipe => recipe.RecipeId == DefaultMvpIds.Recipes.BakedPotato);
            Assert.That(before.OutputDisplayName, Is.EqualTo("Baked Potato"));
            Assert.That(before.HasIngredients, Is.True);
            Assert.That(before.Ingredients.Select(ingredient => ingredient.ItemId), Is.Ordered);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<RecipeIngredientView>)before.Ingredients)[0] = null);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<RecipeView>)state.Recipes)[0] = null);

            Assert.That(
                services.CookingGameplay.Cook(DefaultMvpIds.Recipes.BakedPotato).IsSuccess,
                Is.True);

            Assert.That(
                services.Inventory.Count(DefaultMvpIds.Items.BakedPotato),
                Is.EqualTo(1));
            Assert.That(
                services.CookingGameplay.GetCurrentState().Recipes.Single(
                    recipe => recipe.RecipeId == DefaultMvpIds.Recipes.BakedPotato)
                    .HasIngredients,
                Is.False);
        }
    }
}
