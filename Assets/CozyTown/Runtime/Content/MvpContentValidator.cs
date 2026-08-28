using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Content
{
    public static class MvpContentValidator
    {
        public static OperationResult Validate(CozyTownConfiguration configuration)
        {
            if (configuration == null)
            {
                return OperationResult.Failure("content.configuration_missing");
            }

            if (configuration.InventoryCapacitySlots <= 0
                || configuration.StartingBalance < 0
                || configuration.StartingDay <= 0
                || configuration.StartingMinuteOfDay < 0
                || configuration.StartingMinuteOfDay >= 24 * 60
                || string.IsNullOrWhiteSpace(configuration.FallbackDialogue))
            {
                return OperationResult.Failure("content.configuration_invalid");
            }

            if (configuration.Items.Any(item =>
                    item == null
                    || string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.DisplayName)
                    || !Enum.IsDefined(typeof(ItemCategory), item.Category)
                    || item.MaxStack <= 0))
            {
                return OperationResult.Failure("content.item_invalid");
            }

            if (HasDuplicate(configuration.Items.Select(item => item.Id)))
            {
                return OperationResult.Failure("content.item_id_duplicate");
            }

            var itemIds = new HashSet<string>(
                configuration.Items.Select(item => item.Id),
                StringComparer.Ordinal);

            if (configuration.ShopOffers.Any(offer =>
                    offer == null
                    || string.IsNullOrWhiteSpace(offer.ItemId)
                    || offer.BuyPrice < 0
                    || offer.SellPrice < 0))
            {
                return OperationResult.Failure("content.shop_offer_invalid");
            }

            if (HasDuplicate(configuration.ShopOffers.Select(offer => offer.ItemId)))
            {
                return OperationResult.Failure("content.shop_offer_id_duplicate");
            }

            if (configuration.ShopOffers.Any(offer => !itemIds.Contains(offer.ItemId)))
            {
                return OperationResult.Failure("content.shop_offer_item_missing");
            }

            var purchasableItemIds = new HashSet<string>(
                configuration.ShopOffers
                    .Where(offer => offer.BuyPrice > 0)
                    .Select(offer => offer.ItemId),
                StringComparer.Ordinal);
            var sellableItemIds = new HashSet<string>(
                configuration.ShopOffers
                    .Where(offer => offer.SellPrice > 0)
                    .Select(offer => offer.ItemId),
                StringComparer.Ordinal);

            if (configuration.Crops.Any(crop =>
                    crop == null
                    || string.IsNullOrWhiteSpace(crop.Id)
                    || string.IsNullOrWhiteSpace(crop.SeedItemId)
                    || string.IsNullOrWhiteSpace(crop.HarvestItemId)
                    || crop.GrowthDays <= 0
                    || crop.HarvestQuantity <= 0))
            {
                return OperationResult.Failure("content.crop_invalid");
            }

            if (HasDuplicate(configuration.Crops.Select(crop => crop.Id)))
            {
                return OperationResult.Failure("content.crop_id_duplicate");
            }

            if (HasDuplicate(configuration.Crops.Select(crop => crop.SeedItemId)))
            {
                return OperationResult.Failure("content.crop_seed_duplicate");
            }

            if (configuration.Crops.Any(crop =>
                    !itemIds.Contains(crop.SeedItemId)
                    || !itemIds.Contains(crop.HarvestItemId)))
            {
                return OperationResult.Failure("content.crop_item_missing");
            }

            if (configuration.Crops.Any(crop => !purchasableItemIds.Contains(crop.SeedItemId)))
            {
                return OperationResult.Failure("content.crop_seed_not_for_sale");
            }

            if (configuration.FarmPlotIds.Any(string.IsNullOrWhiteSpace))
            {
                return OperationResult.Failure("content.farm_plot_invalid");
            }

            if (HasDuplicate(configuration.FarmPlotIds))
            {
                return OperationResult.Failure("content.farm_plot_id_duplicate");
            }

            if (configuration.AnimalDefinitions.Any(definition =>
                    definition == null
                    || string.IsNullOrWhiteSpace(definition.SpeciesId)
                    || string.IsNullOrWhiteSpace(definition.FeedItemId)
                    || string.IsNullOrWhiteSpace(definition.ProductItemId)
                    || definition.ProductQuantity <= 0))
            {
                return OperationResult.Failure("content.animal_definition_invalid");
            }

            if (HasDuplicate(configuration.AnimalDefinitions.Select(definition => definition.SpeciesId)))
            {
                return OperationResult.Failure("content.animal_species_id_duplicate");
            }

            if (configuration.AnimalDefinitions.Any(definition =>
                    !itemIds.Contains(definition.FeedItemId)
                    || !itemIds.Contains(definition.ProductItemId)))
            {
                return OperationResult.Failure("content.animal_item_missing");
            }

            if (configuration.AnimalDefinitions.Any(definition =>
                    !purchasableItemIds.Contains(definition.FeedItemId)))
            {
                return OperationResult.Failure("content.animal_feed_not_for_sale");
            }

            var speciesIds = new HashSet<string>(
                configuration.AnimalDefinitions.Select(definition => definition.SpeciesId),
                StringComparer.Ordinal);
            if (configuration.Animals.Any(animal =>
                    string.IsNullOrWhiteSpace(animal.AnimalId)
                    || string.IsNullOrWhiteSpace(animal.SpeciesId)))
            {
                return OperationResult.Failure("content.animal_invalid");
            }

            if (HasDuplicate(configuration.Animals.Select(animal => animal.AnimalId)))
            {
                return OperationResult.Failure("content.animal_id_duplicate");
            }

            if (configuration.Animals.Any(animal => !speciesIds.Contains(animal.SpeciesId)))
            {
                return OperationResult.Failure("content.animal_species_missing");
            }

            if (configuration.FishingEntries.Any(entry =>
                    entry == null
                    || string.IsNullOrWhiteSpace(entry.FishId)
                    || string.IsNullOrWhiteSpace(entry.ItemId)
                    || entry.MinRollInclusive >= entry.MaxRollExclusive))
            {
                return OperationResult.Failure("content.fishing_entry_invalid");
            }

            if (HasDuplicate(configuration.FishingEntries.Select(entry => entry.FishId)))
            {
                return OperationResult.Failure("content.fish_id_duplicate");
            }

            if (configuration.FishingEntries.Any(entry => !itemIds.Contains(entry.ItemId)))
            {
                return OperationResult.Failure("content.fish_item_missing");
            }

            var orderedFishing = configuration.FishingEntries
                .OrderBy(entry => entry.MinRollInclusive)
                .ToArray();
            for (int index = 1; index < orderedFishing.Length; index++)
            {
                if (orderedFishing[index - 1].MaxRollExclusive
                    > orderedFishing[index].MinRollInclusive)
                {
                    return OperationResult.Failure("content.fishing_range_overlap");
                }
            }

            if (configuration.Recipes.Any(recipe =>
                    recipe == null
                    || string.IsNullOrWhiteSpace(recipe.Id)
                    || string.IsNullOrWhiteSpace(recipe.OutputItemId)
                    || recipe.OutputQuantity <= 0
                    || recipe.Ingredients.Length == 0
                    || recipe.Ingredients.Any(ingredient =>
                        string.IsNullOrWhiteSpace(ingredient.ItemId)
                        || ingredient.Quantity <= 0)
                    || HasDuplicate(recipe.Ingredients.Select(ingredient => ingredient.ItemId))))
            {
                return OperationResult.Failure("content.recipe_invalid");
            }

            if (HasDuplicate(configuration.Recipes.Select(recipe => recipe.Id)))
            {
                return OperationResult.Failure("content.recipe_id_duplicate");
            }

            if (configuration.Recipes.Any(recipe =>
                    !itemIds.Contains(recipe.OutputItemId)
                    || recipe.Ingredients.Any(ingredient => !itemIds.Contains(ingredient.ItemId))))
            {
                return OperationResult.Failure("content.recipe_item_missing");
            }

            if (configuration.Recipes.Any(recipe =>
                    !sellableItemIds.Contains(recipe.OutputItemId)))
            {
                return OperationResult.Failure("content.recipe_output_not_accepted");
            }

            var obtainableIngredientIds = new HashSet<string>(
                configuration.ShopOffers
                    .Where(offer => offer.BuyPrice > 0)
                    .Select(offer => offer.ItemId),
                StringComparer.Ordinal);
            obtainableIngredientIds.UnionWith(
                configuration.Crops.Select(crop => crop.HarvestItemId));
            obtainableIngredientIds.UnionWith(
                configuration.AnimalDefinitions.Select(definition => definition.ProductItemId));
            obtainableIngredientIds.UnionWith(
                configuration.FishingEntries.Select(entry => entry.ItemId));
            if (configuration.Recipes.Any(recipe =>
                    recipe.Ingredients.Any(ingredient =>
                        !obtainableIngredientIds.Contains(ingredient.ItemId))))
            {
                return OperationResult.Failure("content.recipe_ingredient_unobtainable");
            }

            if (configuration.Npcs.Any(npc =>
                    npc == null
                    || string.IsNullOrWhiteSpace(npc.Id)
                    || string.IsNullOrWhiteSpace(npc.DisplayName)
                    || string.IsNullOrWhiteSpace(npc.Persona)
                    || string.IsNullOrWhiteSpace(npc.FallbackDialogue)))
            {
                return OperationResult.Failure("content.npc_invalid");
            }

            if (HasDuplicate(configuration.Npcs.Select(npc => npc.Id)))
            {
                return OperationResult.Failure("content.npc_id_duplicate");
            }

            if (configuration.ShopOffers.Any(offer =>
                    offer.BuyPrice == 0 && offer.SellPrice == 0))
            {
                return OperationResult.Failure("content.shop_offer_invalid");
            }

            return OperationResult.Success();
        }

        private static bool HasDuplicate(IEnumerable<string> ids)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                if (!unique.Add(id))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
