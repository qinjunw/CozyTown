using System;
using System.Collections.Generic;
using System.Linq;
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
            return Validate(snapshot, snapshot?.SchemaVersion == GameSaveSnapshot.LegacySchemaVersion
                ? GameSaveSnapshot.LegacySchemaVersion : GameSaveSnapshot.CurrentSchemaVersion);
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

            if (expectedSchemaVersion == GameSaveSnapshot.CurrentSchemaVersion && !CompleteWorldIsValid(snapshot))
                return OperationResult.Failure("save.payload_invalid");
            if (expectedSchemaVersion < GameSaveSnapshot.CurrentSchemaVersion
                && (snapshot.CompleteWorld != null || snapshot.FractionalMinute != 0))
                return OperationResult.Failure("save.payload_invalid");

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

        private static bool CompleteWorldIsValid(GameSaveSnapshot snapshot)
        {
            var complete = snapshot.CompleteWorld;
            if (double.IsNaN(snapshot.FractionalMinute) || snapshot.FractionalMinute < 0 || snapshot.FractionalMinute >= 1
                || snapshot.SourceSchemaVersion < 1 || snapshot.SourceSchemaVersion > GameSaveSnapshot.CurrentSchemaVersion
                || complete == null || string.IsNullOrWhiteSpace(complete.ContentConfiguration)
                || string.IsNullOrWhiteSpace(complete.BodyConfiguration) || complete.World == null
                || complete.World.Residents == null || complete.World.Schedules == null
                || complete.Residents == null || complete.Residents.Count != 4 || complete.Player == null
                || complete.World.Residents.Count != 4 || complete.World.Schedules.Count != 4
                || complete.MeetingsEnabled != (complete.Meetings != null)
                || complete.DecisionsEnabled != (complete.Decisions != null)
                || (complete.MeetingsEnabled && !complete.DecisionsEnabled)
                || complete.World.TotalMinutes != new WorldTimeProgress(snapshot.Clock, snapshot.FractionalMinute, true).TotalMinutes)
                return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (complete.Residents.Any(body => body == null || string.IsNullOrWhiteSpace(body.NpcId)
                    || !ids.Add(body.NpcId) || body.Route == null)) return false;
            if (complete.World.Residents.Any(resident => resident == null)
                || complete.World.Schedules.Any(schedule => schedule == null)) return false;
            if (!complete.MeetingsEnabled && complete.World.Residents.Any(resident => resident.Activity?.IsMeetingActivity == true))
                return false;
            return ids.SetEquals(complete.World.Residents.Select(resident => resident.NpcId))
                && ids.SetEquals(complete.World.Schedules.Select(schedule => schedule.NpcId))
                && snapshot.Characters.Length == 5
                && new HashSet<string>(snapshot.Characters.Where(character => character != null)
                    .Select(character => character.CharacterId), StringComparer.Ordinal)
                    .SetEquals(ids.Concat(new[] { Content.DefaultMvpIds.Characters.Player }));
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
