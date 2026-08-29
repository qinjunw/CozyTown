using System;
using System.Collections.Generic;
using CozyTown.Runtime.Core;
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
            if (snapshot == null)
            {
                return OperationResult.Failure("save.payload_invalid");
            }

            if (snapshot.SchemaVersion != GameSaveSnapshot.CurrentSchemaVersion)
            {
                return OperationResult.Failure("save.schema_unsupported");
            }

            if (snapshot.Inventory == null
                || snapshot.Farm == null
                || snapshot.Livestock == null
                || snapshot.Clock.Day < 1
                || snapshot.Clock.MinuteOfDay < 0
                || snapshot.Clock.MinuteOfDay >= InMemoryTimeService.MinutesPerDay
                || snapshot.Wallet.Balance < 0
                || snapshot.Farm.LastProcessedDay != snapshot.Clock.Day
                || snapshot.Livestock.LastProcessedDay != snapshot.Clock.Day
                || !InventoryIsValid(snapshot.Inventory)
                || !FarmIsValid(snapshot.Farm)
                || !LivestockIsValid(snapshot.Livestock))
            {
                return OperationResult.Failure("save.payload_invalid");
            }

            return OperationResult.Success();
        }

        private static bool InventoryIsValid(InventorySnapshot inventory)
        {
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
