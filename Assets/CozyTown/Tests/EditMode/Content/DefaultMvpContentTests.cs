using System;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Fishing;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Npc;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Content
{
    public sealed class DefaultMvpContentTests
    {
        [Test]
        public void CreateConfiguration_ContainsCompleteValidMvpReferences()
        {
            CozyTownConfiguration configuration = DefaultMvpContent.CreateConfiguration();

            OperationResult validation = MvpContentValidator.Validate(configuration);

            Assert.That(validation.IsSuccess, Is.True, validation.ErrorCode);
            Assert.That(configuration.Crops.Length, Is.EqualTo(3));
            Assert.That(configuration.FishingEntries.Length, Is.EqualTo(3));
            Assert.That(configuration.Recipes.Length, Is.EqualTo(5));
            Assert.That(configuration.AnimalDefinitions.Length, Is.EqualTo(1));
            Assert.That(configuration.Animals.Length, Is.EqualTo(1));
            Assert.That(configuration.Npcs.Length, Is.EqualTo(4));
            Assert.That(
                configuration.Crops.Select(crop => crop.Id),
                Is.EquivalentTo(new[]
                {
                    DefaultMvpIds.Crops.Potato,
                    DefaultMvpIds.Crops.Carrot,
                    DefaultMvpIds.Crops.Tomato
                }));
            Assert.That(
                configuration.FishingEntries.Select(entry => entry.FishId),
                Is.EquivalentTo(new[]
                {
                    DefaultMvpIds.Fish.Carp,
                    DefaultMvpIds.Fish.Trout,
                    DefaultMvpIds.Fish.Bass
                }));
            Assert.That(
                configuration.Recipes.All(recipe =>
                    configuration.ShopOffers.Any(offer =>
                        offer.ItemId == recipe.OutputItemId && offer.SellPrice > 0)),
                Is.True,
                "Every default dish must have a positive shop sell price.");
        }

        [Test]
        public void CreateConfiguration_ExposesConfirmedShopRestockRules()
        {
            CozyTownConfiguration configuration = DefaultMvpContent.CreateConfiguration();

            CollectionAssert.AreEqual(
                new[]
                {
                    "seed.potato:700:3:6",
                    "seed.carrot:700:3:6",
                    "seed.tomato:700:3:6",
                    "feed.chicken:1000:6:12",
                    "ingredient.salt:750:3:8",
                    "ingredient.flour:750:3:8"
                },
                configuration.ShopRestockRules
                    .Select(rule =>
                        $"{rule.ItemId}:{rule.AppearancePermille}:{rule.MinQuantity}:{rule.MaxQuantity}")
                    .ToArray());
        }

        [TestCase("crop", true, "content.crop_seed_not_for_sale")]
        [TestCase("crop", false, "content.crop_seed_not_for_sale")]
        [TestCase("feed", true, "content.animal_feed_not_for_sale")]
        [TestCase("feed", false, "content.animal_feed_not_for_sale")]
        [TestCase("dish", true, "content.recipe_output_not_accepted")]
        [TestCase("dish", false, "content.recipe_output_not_accepted")]
        public void Validate_WhenRequiredShopSupplyIsRemovedOrZeroed_RejectsConfiguration(
            string supply,
            bool removeOffer,
            string expectedError)
        {
            CozyTownConfiguration source = DefaultMvpContent.CreateConfiguration();
            string itemId;
            switch (supply)
            {
                case "crop":
                    itemId = source.Crops[0].SeedItemId;
                    break;
                case "feed":
                    itemId = source.AnimalDefinitions[0].FeedItemId;
                    break;
                case "dish":
                    itemId = source.Recipes[0].OutputItemId;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(supply));
            }

            ShopOffer[] offers = removeOffer
                ? source.ShopOffers.Where(offer => offer.ItemId != itemId).ToArray()
                : source.ShopOffers
                    .Select(offer => offer.ItemId == itemId
                        ? new ShopOffer(offer.ItemId, 0, 0)
                        : offer)
                    .ToArray();
            CozyTownConfiguration invalid = Copy(source, shopOffers: offers);

            OperationResult result = MvpContentValidator.Validate(invalid);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
        }

        [Test]
        public void Constructor_WhenCallerMutatesTopLevelArrays_PreservesConfigurationValues()
        {
            CozyTownConfiguration source = DefaultMvpContent.CreateConfiguration();
            ItemDefinition[] items = source.Items.ToArray();
            ShopOffer[] shopOffers = source.ShopOffers.ToArray();
            CropDefinition[] crops = source.Crops.ToArray();
            string[] farmPlotIds = source.FarmPlotIds.ToArray();
            AnimalDefinition[] animalDefinitions = source.AnimalDefinitions.ToArray();
            AnimalSnapshot[] animals = source.Animals.ToArray();
            FishingEntry[] fishingEntries = source.FishingEntries.ToArray();
            RecipeDefinition[] recipes = source.Recipes.ToArray();
            NpcDefinition[] npcs = source.Npcs.ToArray();
            ShopRestockRule[] shopRestockRules = source.ShopRestockRules.ToArray();
            var configuration = new CozyTownConfiguration(
                items,
                shopOffers,
                crops,
                farmPlotIds,
                animalDefinitions,
                animals,
                fishingEntries,
                recipes,
                npcs: npcs,
                shopRestockRules: shopRestockRules);

            items[0] = null;
            shopOffers[0] = null;
            crops[0] = null;
            farmPlotIds[0] = "plot.mutated";
            animalDefinitions[0] = null;
            animals[0] = default;
            fishingEntries[0] = null;
            recipes[0] = null;
            npcs[0] = null;
            shopRestockRules[0] = null;

            Assert.That(configuration.Items[0], Is.Not.Null);
            Assert.That(configuration.ShopOffers[0], Is.Not.Null);
            Assert.That(configuration.Crops[0], Is.Not.Null);
            Assert.That(configuration.FarmPlotIds[0], Is.Not.EqualTo("plot.mutated"));
            Assert.That(configuration.AnimalDefinitions[0], Is.Not.Null);
            Assert.That(configuration.Animals[0].AnimalId, Is.Not.Null);
            Assert.That(configuration.FishingEntries[0], Is.Not.Null);
            Assert.That(configuration.Recipes[0], Is.Not.Null);
            Assert.That(configuration.Npcs[0], Is.Not.Null);
            Assert.That(configuration.ShopRestockRules[0], Is.Not.Null);
        }

        [Test]
        public void RecipeConstructor_WhenCallerMutatesIngredientArray_PreservesIngredients()
        {
            var ingredients = new[] { new RecipeIngredient("item.original", 2) };
            var recipe = new RecipeDefinition(
                "recipe.test",
                ingredients,
                "food.test",
                1);

            ingredients[0] = new RecipeIngredient("item.mutated", 99);

            Assert.That(recipe.Ingredients[0].ItemId, Is.EqualTo("item.original"));
            Assert.That(recipe.Ingredients[0].Quantity, Is.EqualTo(2));
        }

        [Test]
        public void CreateConfiguration_CalledTwice_ReturnsIndependentMutableArrays()
        {
            CozyTownConfiguration first = DefaultMvpContent.CreateConfiguration();
            CozyTownConfiguration second = DefaultMvpContent.CreateConfiguration();
            string secondItemId = second.Items[0].Id;
            RecipeIngredient secondIngredient = second.Recipes[0].Ingredients[0];
            string secondNpcId = second.Npcs[0].Id;
            string secondRestockItemId = second.ShopRestockRules[0].ItemId;

            first.Items[0] = null;
            first.Recipes[0].Ingredients[0] = new RecipeIngredient("item.mutated", 99);
            first.Npcs[0] = null;
            first.ShopRestockRules[0] = null;

            Assert.That(second.Items[0].Id, Is.EqualTo(secondItemId));
            Assert.That(second.Recipes[0].Ingredients[0].ItemId, Is.EqualTo(secondIngredient.ItemId));
            Assert.That(second.Recipes[0].Ingredients[0].Quantity, Is.EqualTo(secondIngredient.Quantity));
            Assert.That(second.Npcs[0].Id, Is.EqualTo(secondNpcId));
            Assert.That(
                second.ShopRestockRules[0].ItemId,
                Is.EqualTo(secondRestockItemId));
        }

        [TestCase("shop", "content.shop_offer_id_duplicate")]
        [TestCase("crop", "content.crop_id_duplicate")]
        [TestCase("fish", "content.fish_id_duplicate")]
        [TestCase("recipe", "content.recipe_id_duplicate")]
        [TestCase("species", "content.animal_species_id_duplicate")]
        [TestCase("animal", "content.animal_id_duplicate")]
        [TestCase("npc", "content.npc_id_duplicate")]
        public void Validate_WhenStableIdIsDuplicated_RejectsConfiguration(
            string category,
            string expectedError)
        {
            CozyTownConfiguration source = DefaultMvpContent.CreateConfiguration();
            CozyTownConfiguration invalid;
            switch (category)
            {
                case "shop":
                    invalid = Copy(
                        source,
                        shopOffers: source.ShopOffers.Concat(new[] { source.ShopOffers[0] }).ToArray());
                    break;
                case "crop":
                    invalid = Copy(
                        source,
                        crops: source.Crops.Concat(new[] { source.Crops[0] }).ToArray());
                    break;
                case "fish":
                    invalid = Copy(
                        source,
                        fishingEntries: source.FishingEntries
                            .Concat(new[] { source.FishingEntries[0] })
                            .ToArray());
                    break;
                case "recipe":
                    invalid = Copy(
                        source,
                        recipes: source.Recipes.Concat(new[] { source.Recipes[0] }).ToArray());
                    break;
                case "species":
                    invalid = Copy(
                        source,
                        animalDefinitions: source.AnimalDefinitions
                            .Concat(new[] { source.AnimalDefinitions[0] })
                            .ToArray());
                    break;
                case "animal":
                    invalid = Copy(
                        source,
                        animals: source.Animals.Concat(new[] { source.Animals[0] }).ToArray());
                    break;
                case "npc":
                    invalid = Copy(
                        source,
                        npcs: source.Npcs.Concat(new[] { source.Npcs[0] }).ToArray());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(category));
            }

            OperationResult result = MvpContentValidator.Validate(invalid);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
        }

        [Test]
        public void Validate_WhenAnimalIsFedAndProductReady_RejectsUnreachableState()
        {
            CozyTownConfiguration source = DefaultMvpContent.CreateConfiguration();
            AnimalSnapshot original = source.Animals[0];
            var animals = new[]
            {
                new AnimalSnapshot(
                    original.AnimalId,
                    original.SpeciesId,
                    fedToday: true,
                    productReady: true)
            };
            CozyTownConfiguration invalid = Copy(source, animals: animals);

            OperationResult result = MvpContentValidator.Validate(invalid);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("content.animal_invalid"));
        }

        [TestCase("shop", "content.shop_offer_item_missing")]
        [TestCase("crop", "content.crop_item_missing")]
        [TestCase("fish", "content.fish_item_missing")]
        [TestCase("recipe", "content.recipe_item_missing")]
        [TestCase("animal-item", "content.animal_item_missing")]
        [TestCase("animal-species", "content.animal_species_missing")]
        public void Validate_WhenReferenceDoesNotResolve_RejectsConfiguration(
            string category,
            string expectedError)
        {
            CozyTownConfiguration source = DefaultMvpContent.CreateConfiguration();
            CozyTownConfiguration invalid;
            switch (category)
            {
                case "shop":
                    ShopOffer[] offers = source.ShopOffers.ToArray();
                    offers[0] = new ShopOffer("item.missing", 1, 0);
                    invalid = Copy(source, shopOffers: offers);
                    break;
                case "crop":
                    CropDefinition[] crops = source.Crops.ToArray();
                    crops[0] = new CropDefinition(
                        crops[0].Id,
                        "item.missing",
                        crops[0].HarvestItemId,
                        crops[0].GrowthDays,
                        crops[0].HarvestQuantity);
                    invalid = Copy(source, crops: crops);
                    break;
                case "fish":
                    FishingEntry[] fishing = source.FishingEntries.ToArray();
                    fishing[0] = new FishingEntry(
                        fishing[0].FishId,
                        "item.missing",
                        fishing[0].MinRollInclusive,
                        fishing[0].MaxRollExclusive);
                    invalid = Copy(source, fishingEntries: fishing);
                    break;
                case "recipe":
                    RecipeDefinition[] recipes = source.Recipes.ToArray();
                    recipes[0] = new RecipeDefinition(
                        recipes[0].Id,
                        recipes[0].Ingredients,
                        "item.missing",
                        recipes[0].OutputQuantity);
                    invalid = Copy(source, recipes: recipes);
                    break;
                case "animal-item":
                    AnimalDefinition[] species = source.AnimalDefinitions.ToArray();
                    species[0] = new AnimalDefinition(
                        species[0].SpeciesId,
                        "item.missing",
                        species[0].ProductItemId,
                        species[0].ProductQuantity);
                    invalid = Copy(source, animalDefinitions: species);
                    break;
                case "animal-species":
                    AnimalSnapshot[] animals = source.Animals.ToArray();
                    animals[0] = new AnimalSnapshot(
                        animals[0].AnimalId,
                        "species.missing",
                        animals[0].FedToday,
                        animals[0].ProductReady);
                    invalid = Copy(source, animals: animals);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(category));
            }

            OperationResult result = MvpContentValidator.Validate(invalid);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
        }

        [TestCase(0, 360, 24, 0)]
        [TestCase(1, -1, 24, 0)]
        [TestCase(1, 1440, 24, 0)]
        [TestCase(1, 360, 0, 0)]
        [TestCase(1, 360, 24, -1)]
        public void Create_WhenCoreStartingValueIsInvalid_RejectsBeforeServiceConstruction(
            int startingDay,
            int startingMinute,
            int capacity,
            int balance)
        {
            CozyTownConfiguration source = DefaultMvpContent.CreateConfiguration();
            CozyTownConfiguration invalid = Copy(
                source,
                startingDay: startingDay,
                startingMinuteOfDay: startingMinute,
                inventoryCapacitySlots: capacity,
                startingBalance: balance);

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => CozyTownCompositionRoot.Create(invalid));

            Assert.That(exception.Message, Does.Contain("content.configuration_invalid"));
        }

        private static CozyTownConfiguration Copy(
            CozyTownConfiguration source,
            ItemDefinition[] items = null,
            ShopOffer[] shopOffers = null,
            CropDefinition[] crops = null,
            string[] farmPlotIds = null,
            AnimalDefinition[] animalDefinitions = null,
            AnimalSnapshot[] animals = null,
            FishingEntry[] fishingEntries = null,
            RecipeDefinition[] recipes = null,
            NpcDefinition[] npcs = null,
            ShopRestockRule[] shopRestockRules = null,
            int? inventoryCapacitySlots = null,
            int? startingBalance = null,
            int? startingDay = null,
            int? startingMinuteOfDay = null)
        {
            return new CozyTownConfiguration(
                items ?? source.Items,
                shopOffers ?? source.ShopOffers,
                crops ?? source.Crops,
                farmPlotIds ?? source.FarmPlotIds,
                animalDefinitions ?? source.AnimalDefinitions,
                animals ?? source.Animals,
                fishingEntries ?? source.FishingEntries,
                recipes ?? source.Recipes,
                inventoryCapacitySlots ?? source.InventoryCapacitySlots,
                startingBalance ?? source.StartingBalance,
                startingDay ?? source.StartingDay,
                startingMinuteOfDay ?? source.StartingMinuteOfDay,
                source.FallbackDialogue,
                npcs ?? source.Npcs,
                shopRestockRules ?? source.ShopRestockRules);
        }
    }
}
