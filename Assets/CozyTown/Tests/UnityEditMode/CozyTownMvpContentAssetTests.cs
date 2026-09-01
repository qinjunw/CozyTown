using System;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;
using CozyTown.Unity.Content;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class CozyTownMvpContentAssetTests
    {
        private const string DefaultAssetPath =
            "Assets/CozyTown/Content/DefaultMvpContent.asset";

        [Test]
        public void DefaultAsset_LoadsEquivalentValidatedEconomyAndProductionConfiguration()
        {
            CozyTownMvpContentAsset asset = LoadDefaultAsset();

            OperationResult<CozyTownConfiguration> result = asset.Load();

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            CozyTownConfiguration configuration = result.Value;
            CozyTownConfiguration expected = DefaultMvpContent.CreateConfiguration();
            Assert.That(MvpContentValidator.Validate(configuration).IsSuccess, Is.True);
            AssertEquivalent(expected, configuration);
        }

        [Test]
        public void DefaultAsset_BuyGrowHarvestAndSell_CompletesEconomyLoop()
        {
            OperationResult<CozyTownConfiguration> content = LoadDefaultAsset().Load();
            Assert.That(content.IsSuccess, Is.True, content.ErrorCode);
            CozyTownServices services = CozyTownCompositionRoot.Create(content.Value);

            Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);
            Assert.That(
                services.ShopTrading.Buy(
                    DefaultMvpIds.Shops.TownGeneral,
                    DefaultMvpIds.Characters.Player,
                    DefaultMvpIds.Items.PotatoSeed,
                    1).IsSuccess,
                Is.True);
            Assert.That(
                services.FarmGameplay.Plant("plot.01", DefaultMvpIds.Items.PotatoSeed)
                    .IsSuccess,
                Is.True);
            Assert.That(services.FarmGameplay.Water("plot.01").IsSuccess, Is.True);
            Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);
            Assert.That(services.FarmGameplay.Water("plot.01").IsSuccess, Is.True);
            Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);
            Assert.That(services.FarmGameplay.Harvest("plot.01").IsSuccess, Is.True);
            Assert.That(
                services.ShopTrading.Sell(
                    DefaultMvpIds.Shops.TownGeneral,
                    DefaultMvpIds.Characters.Player,
                    DefaultMvpIds.Items.Potato,
                    2).IsSuccess,
                Is.True);

            Assert.That(services.Wallet.Balance, Is.EqualTo(320));
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.Potato), Is.Zero);
            Assert.That(
                services.EconomyState.TryGetShop(
                    DefaultMvpIds.Shops.TownGeneral,
                    out var shop),
                Is.True);
            Assert.That(shop.Wallet.Balance, Is.EqualTo(9980));
            Assert.That(
                shop.Stock.Items.Single(item => item.ItemId == DefaultMvpIds.Items.Potato),
                Is.EqualTo(new ItemStack(DefaultMvpIds.Items.Potato, 2)));
        }

        [TestCase(InvalidMutation.DuplicateItemId, "content.item_id_duplicate")]
        [TestCase(InvalidMutation.MissingShopItem, "content.shop_offer_item_missing")]
        [TestCase(InvalidMutation.InvalidPrice, "content.shop_offer_invalid")]
        [TestCase(InvalidMutation.OverlappingFishingRange, "content.fishing_range_overlap")]
        [TestCase(InvalidMutation.UnreachableRecipe, "content.recipe_ingredient_unobtainable")]
        [TestCase(InvalidMutation.NegativeShopBalance, "content.configuration_invalid")]
        public void Load_WhenSerializedContentIsInvalid_RejectsAsset(
            InvalidMutation mutation,
            string expectedError)
        {
            CozyTownMvpContentAsset clone = UnityEngine.Object.Instantiate(LoadDefaultAsset());
            try
            {
                var serialized = new SerializedObject(clone);
                ApplyInvalidMutation(serialized, mutation);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                OperationResult<CozyTownConfiguration> result = clone.Load();

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        private static CozyTownMvpContentAsset LoadDefaultAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<CozyTownMvpContentAsset>(
                DefaultAssetPath);
            Assert.That(asset, Is.Not.Null, $"Missing default content asset at {DefaultAssetPath}.");
            return asset;
        }

        private static void AssertEquivalent(
            CozyTownConfiguration expected,
            CozyTownConfiguration actual)
        {
            Assert.That(actual.InventoryCapacitySlots, Is.EqualTo(expected.InventoryCapacitySlots));
            Assert.That(actual.StartingBalance, Is.EqualTo(expected.StartingBalance));
            Assert.That(actual.StartingShopBalance, Is.EqualTo(expected.StartingShopBalance));
            Assert.That(actual.StartingDay, Is.EqualTo(expected.StartingDay));
            Assert.That(actual.StartingMinuteOfDay, Is.EqualTo(expected.StartingMinuteOfDay));
            Assert.That(actual.StartingWorldSeed, Is.EqualTo(expected.StartingWorldSeed));
            Assert.That(actual.FallbackDialogue, Is.EqualTo(expected.FallbackDialogue));
            Assert.That(
                actual.Items.Select(item =>
                    $"{item.Id}|{item.DisplayName}|{item.Category}|{item.MaxStack}"),
                Is.EqualTo(expected.Items.Select(item =>
                    $"{item.Id}|{item.DisplayName}|{item.Category}|{item.MaxStack}")));
            Assert.That(
                actual.ShopOffers.Select(offer =>
                    $"{offer.ItemId}|{offer.BuyPrice}|{offer.SellPrice}"),
                Is.EqualTo(expected.ShopOffers.Select(offer =>
                    $"{offer.ItemId}|{offer.BuyPrice}|{offer.SellPrice}")));
            Assert.That(
                actual.ShopRestockRules.Select(rule =>
                    $"{rule.ItemId}|{rule.AppearancePermille}|{rule.MinQuantity}|{rule.MaxQuantity}"),
                Is.EqualTo(expected.ShopRestockRules.Select(rule =>
                    $"{rule.ItemId}|{rule.AppearancePermille}|{rule.MinQuantity}|{rule.MaxQuantity}")));
            Assert.That(
                actual.Crops.Select(crop =>
                    $"{crop.Id}|{crop.SeedItemId}|{crop.HarvestItemId}|{crop.GrowthDays}|{crop.HarvestQuantity}"),
                Is.EqualTo(expected.Crops.Select(crop =>
                    $"{crop.Id}|{crop.SeedItemId}|{crop.HarvestItemId}|{crop.GrowthDays}|{crop.HarvestQuantity}")));
            Assert.That(actual.FarmPlotIds, Is.EqualTo(expected.FarmPlotIds));
            Assert.That(
                actual.AnimalDefinitions.Select(definition =>
                    $"{definition.SpeciesId}|{definition.FeedItemId}|{definition.ProductItemId}|{definition.ProductQuantity}"),
                Is.EqualTo(expected.AnimalDefinitions.Select(definition =>
                    $"{definition.SpeciesId}|{definition.FeedItemId}|{definition.ProductItemId}|{definition.ProductQuantity}")));
            Assert.That(
                actual.Animals.Select(animal =>
                    $"{animal.AnimalId}|{animal.SpeciesId}|{animal.FedToday}|{animal.ProductReady}"),
                Is.EqualTo(expected.Animals.Select(animal =>
                    $"{animal.AnimalId}|{animal.SpeciesId}|{animal.FedToday}|{animal.ProductReady}")));
            Assert.That(
                actual.FishingEntries.Select(entry =>
                    $"{entry.FishId}|{entry.ItemId}|{entry.MinRollInclusive}|{entry.MaxRollExclusive}"),
                Is.EqualTo(expected.FishingEntries.Select(entry =>
                    $"{entry.FishId}|{entry.ItemId}|{entry.MinRollInclusive}|{entry.MaxRollExclusive}")));
            Assert.That(
                actual.Recipes.Select(FormatRecipe),
                Is.EqualTo(expected.Recipes.Select(FormatRecipe)));
            Assert.That(
                actual.Npcs.Select(npc =>
                    $"{npc.Id}|{npc.DisplayName}|{npc.Persona}|{npc.FallbackDialogue}"),
                Is.EqualTo(expected.Npcs.Select(npc =>
                    $"{npc.Id}|{npc.DisplayName}|{npc.Persona}|{npc.FallbackDialogue}")));
        }

        private static string FormatRecipe(CozyTown.Runtime.Cooking.RecipeDefinition recipe)
        {
            string ingredients = string.Join(
                ",",
                recipe.Ingredients.Select(ingredient =>
                    $"{ingredient.ItemId}:{ingredient.Quantity}"));
            return $"{recipe.Id}|{ingredients}|{recipe.OutputItemId}|{recipe.OutputQuantity}";
        }

        private static void ApplyInvalidMutation(
            SerializedObject serialized,
            InvalidMutation mutation)
        {
            switch (mutation)
            {
                case InvalidMutation.DuplicateItemId:
                    SetString(serialized, "_items.Array.data[1]._id", "seed.potato");
                    return;
                case InvalidMutation.MissingShopItem:
                    SetString(serialized, "_shopOffers.Array.data[0]._itemId", "item.missing");
                    return;
                case InvalidMutation.InvalidPrice:
                    SetInteger(serialized, "_shopOffers.Array.data[0]._buyPrice", -1);
                    return;
                case InvalidMutation.OverlappingFishingRange:
                    SetInteger(serialized, "_fishingEntries.Array.data[1]._minRollInclusive", 39);
                    return;
                case InvalidMutation.UnreachableRecipe:
                    SetString(
                        serialized,
                        "_recipes.Array.data[0]._ingredients.Array.data[0]._itemId",
                        DefaultMvpIds.Items.BakedPotato);
                    return;
                case InvalidMutation.NegativeShopBalance:
                    SetInteger(serialized, "_startingShopBalance", -1);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
            }
        }

        private static void SetString(SerializedObject serialized, string path, string value)
        {
            SerializedProperty property = RequireProperty(serialized, path);
            property.stringValue = value;
        }

        private static void SetInteger(SerializedObject serialized, string path, int value)
        {
            SerializedProperty property = RequireProperty(serialized, path);
            property.intValue = value;
        }

        private static SerializedProperty RequireProperty(
            SerializedObject serialized,
            string path)
        {
            return serialized.FindProperty(path)
                ?? throw new InvalidOperationException($"Missing serialized property '{path}'.");
        }

        public enum InvalidMutation
        {
            DuplicateItemId,
            MissingShopItem,
            InvalidPrice,
            OverlappingFishingRange,
            UnreachableRecipe,
            NegativeShopBalance
        }
    }
}
