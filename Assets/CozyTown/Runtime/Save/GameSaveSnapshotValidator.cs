using System;
using System.Collections.Generic;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Save
{
    internal static class GameSaveSnapshotValidator
    {
        public static OperationResult Validate(GameSaveSnapshot snapshot)
        {
            return Validate(snapshot, GameSaveSnapshot.CurrentSchemaVersion);
        }

        public static OperationResult ValidateLegacyV2(GameSaveSnapshot snapshot)
        {
            return Validate(snapshot, expectedSchemaVersion: 2);
        }

        private static OperationResult Validate(
            GameSaveSnapshot snapshot,
            int expectedSchemaVersion)
        {
            if (snapshot == null)
            {
                return OperationResult.Failure("save.payload_invalid");
            }

            if (snapshot.SchemaVersion != expectedSchemaVersion)
            {
                return OperationResult.Failure("save.schema_unsupported");
            }

            CharacterEconomySnapshot[] characters = snapshot.Characters;
            ShopEconomySnapshot[] shops = snapshot.Shops;
            if (snapshot.Farm == null
                || snapshot.Livestock == null
                || !DailySettlementSchedule.IsValidProgress(
                    snapshot.Clock,
                    snapshot.Farm.LastProcessedDay)
                || (expectedSchemaVersion == 2
                    && snapshot.Farm.LastProcessedDay != snapshot.Clock.Day)
                || snapshot.Livestock.LastProcessedDay != snapshot.Farm.LastProcessedDay
                || !CharactersAreValid(characters)
                || !ShopsAreValid(shops, snapshot.Farm.LastProcessedDay)
                || !FarmIsValid(snapshot.Farm)
                || !LivestockIsValid(snapshot.Livestock))
            {
                return OperationResult.Failure("save.payload_invalid");
            }

            return OperationResult.Success();
        }

        private static bool CharactersAreValid(CharacterEconomySnapshot[] characters)
        {
            if (characters == null)
            {
                return false;
            }

            var characterIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterEconomySnapshot character in characters)
            {
                if (character == null
                    || string.IsNullOrWhiteSpace(character.CharacterId)
                    || character.Wallet.Balance < 0
                    || !characterIds.Add(character.CharacterId)
                    || !InventoryIsValid(character.Backpack))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ShopsAreValid(ShopEconomySnapshot[] shops, int completedDay)
        {
            if (shops == null)
            {
                return false;
            }

            var shopIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShopEconomySnapshot shop in shops)
            {
                if (shop == null
                    || string.IsNullOrWhiteSpace(shop.ShopId)
                    || shop.Wallet.Balance < 0
                    || shop.LastRestockedDay != completedDay
                    || shop.RestockAlgorithmVersion
                        != DeterministicShopStockReplacementPolicy.VersionOne
                    || !shopIds.Add(shop.ShopId)
                    || !InventoryIsValid(shop.Stock))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool InventoryIsValid(InventorySnapshot inventory)
        {
            if (inventory == null)
            {
                return false;
            }

            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemStack item in inventory.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ItemId)
                    || item.Quantity <= 0
                    || !itemIds.Add(item.ItemId))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FarmIsValid(FarmSnapshot farm)
        {
            var plotIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (FarmPlotSnapshot plot in farm.Plots)
            {
                if (string.IsNullOrWhiteSpace(plot.PlotId)
                    || !plotIds.Add(plot.PlotId)
                    || !Enum.IsDefined(typeof(FarmPlotStatus), plot.Status))
                {
                    return false;
                }

                if (plot.Status == FarmPlotStatus.Empty)
                {
                    if (!string.IsNullOrEmpty(plot.CropId)
                        || plot.GrowthProgressDays != 0
                        || plot.WateredToday)
                    {
                        return false;
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(plot.CropId)
                    || plot.GrowthProgressDays < 0
                    || (plot.Status == FarmPlotStatus.Ready && plot.WateredToday))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LivestockIsValid(LivestockSnapshot livestock)
        {
            var animalIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AnimalSnapshot animal in livestock.Animals)
            {
                if (string.IsNullOrWhiteSpace(animal.AnimalId)
                    || string.IsNullOrWhiteSpace(animal.SpeciesId)
                    || !animalIds.Add(animal.AnimalId)
                    || (animal.FedToday && animal.ProductReady))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
