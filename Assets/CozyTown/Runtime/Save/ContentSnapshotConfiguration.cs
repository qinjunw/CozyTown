using System;
using System.Globalization;
using System.Text;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Save
{
    internal static class ContentSnapshotConfiguration
    {
        public static string Capture(CozyTownConfiguration configuration)
        {
            var text = new StringBuilder();
            void Row(string name, params object[] fields)
            {
                text.Append(name).Append(':');
                foreach (object field in fields)
                {
                    string value = Convert.ToString(field, CultureInfo.InvariantCulture) ?? string.Empty;
                    text.Append(value.Length).Append(':').Append(value);
                }
                text.Append('\n');
            }
            Row("content-rules", "cozytown.snapshot.v4", "morning-settlement.v1", "resource-trading.v1");
            Row("initial", configuration.InventoryCapacitySlots, configuration.StartingBalance,
                configuration.StartingDay, configuration.StartingMinuteOfDay, configuration.StartingWorldSeed,
                configuration.StartingShopBalance, configuration.FallbackDialogue);
            foreach (var item in configuration.Items) Row("item", item.Id, item.DisplayName, item.Category, item.MaxStack);
            foreach (var offer in configuration.ShopOffers) Row("offer", offer.ItemId, offer.BuyPrice, offer.SellPrice);
            foreach (var rule in configuration.ShopRestockRules)
                Row("restock", rule.ItemId, rule.AppearancePermille, rule.MinQuantity, rule.MaxQuantity);
            foreach (var crop in configuration.Crops)
                Row("crop", crop.Id, crop.SeedItemId, crop.HarvestItemId, crop.GrowthDays, crop.HarvestQuantity);
            foreach (var plot in configuration.FarmPlotIds) Row("plot", plot);
            foreach (var definition in configuration.AnimalDefinitions)
                Row("species", definition.SpeciesId, definition.FeedItemId, definition.ProductItemId, definition.ProductQuantity);
            foreach (var animal in configuration.Animals)
                Row("animal", animal.AnimalId, animal.SpeciesId, animal.FedToday, animal.ProductReady);
            foreach (var fish in configuration.FishingEntries)
                Row("fish", fish.FishId, fish.ItemId, fish.MinRollInclusive, fish.MaxRollExclusive);
            foreach (var recipe in configuration.Recipes)
            {
                Row("recipe", recipe.Id, recipe.OutputItemId, recipe.OutputQuantity);
                foreach (var ingredient in recipe.Ingredients) Row("ingredient", ingredient.ItemId, ingredient.Quantity);
            }
            foreach (var npc in configuration.Npcs) Row("npc", npc.Id, npc.DisplayName, npc.Persona, npc.FallbackDialogue);
            foreach (var npc in configuration.InitialNpcEconomy)
            {
                Row("npc-economy", npc.CharacterId, npc.Wallet.Balance);
                foreach (var stack in npc.Backpack.Items) Row("npc-item", stack.ItemId, stack.Quantity);
            }
            return text.ToString();
        }
    }
}
