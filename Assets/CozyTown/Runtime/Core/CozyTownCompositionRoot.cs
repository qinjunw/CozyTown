using System;
using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Fishing;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Core
{
    public static class CozyTownCompositionRoot
    {
        public static CozyTownServices Create(CozyTownConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var time = new InMemoryTimeService(
                configuration.StartingDay,
                configuration.StartingMinuteOfDay);
            var inventory = new InMemoryInventory(
                configuration.Items,
                configuration.InventoryCapacitySlots);
            var wallet = new InMemoryWallet(configuration.StartingBalance);
            var shop = new InMemoryShopService(configuration.ShopOffers, wallet, inventory);
            var farm = new InMemoryFarmService(
                configuration.FarmPlotIds,
                configuration.Crops,
                inventory,
                configuration.StartingDay);
            var livestock = new InMemoryLivestockService(
                configuration.Animals,
                configuration.AnimalDefinitions,
                inventory,
                configuration.StartingDay);
            var fishing = new InMemoryFishingService(configuration.FishingEntries, inventory);
            var cooking = new InMemoryCookingService(configuration.Recipes, inventory);
            var npcDialogue = new FixedFallbackDialogueGenerator(configuration.FallbackDialogue);
            var saveStorage = new InMemorySaveStorage();

            return new CozyTownServices(
                time,
                inventory,
                wallet,
                shop,
                farm,
                livestock,
                fishing,
                cooking,
                npcDialogue,
                saveStorage);
        }

        public static CozyTownServices CreateEmpty()
        {
            return Create(CozyTownConfiguration.Empty());
        }
    }
}
