using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
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

            OperationResult validation = MvpContentValidator.Validate(configuration);
            if (!validation.IsSuccess)
            {
                throw new ArgumentException(
                    $"CozyTown configuration is invalid: {validation.ErrorCode}",
                    nameof(configuration));
            }

            var time = new InMemoryTimeService(
                configuration.StartingDay,
                configuration.StartingMinuteOfDay);
            var inventory = new InMemoryInventory(
                configuration.Items,
                configuration.InventoryCapacitySlots);
            var wallet = new InMemoryWallet(configuration.StartingBalance);
            var shop = new InMemoryShopService(configuration.ShopOffers, wallet, inventory);
            var shopTrading = new ShopTradingCoordinator(
                configuration.Items,
                shop,
                wallet,
                inventory);
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
            var farmGameplay = new FarmGameplayCoordinator(
                configuration.Items,
                configuration.Crops,
                farm,
                inventory);
            var livestockGameplay = new LivestockGameplayCoordinator(
                configuration.Items,
                configuration.AnimalDefinitions,
                livestock,
                inventory);
            var fishingGameplay = new FishingGameplayCoordinator(
                configuration.Items,
                fishing,
                inventory);
            var cookingGameplay = new CookingGameplayCoordinator(
                configuration.Items,
                cooking,
                inventory);
            INpcDialogueGenerator npcDialogue = configuration.Npcs.Length == 0
                ? new FixedFallbackDialogueGenerator(configuration.FallbackDialogue)
                : new ConfiguredFallbackDialogueGenerator(
                    configuration.Npcs,
                    configuration.FallbackDialogue);
            var saveStorage = new InMemorySaveStorage();
            var dayTransition = new DayTransitionCoordinator(time, farm, livestock);

            return new CozyTownServices(
                dayTransition,
                time,
                inventory,
                wallet,
                shop,
                shopTrading,
                farm,
                farmGameplay,
                livestock,
                livestockGameplay,
                fishing,
                fishingGameplay,
                cooking,
                cookingGameplay,
                npcDialogue,
                saveStorage);
        }

        public static CozyTownServices CreateDefault()
        {
            CozyTownConfiguration configuration = DefaultMvpContent.CreateConfiguration();
            return Create(configuration);
        }

        public static CozyTownServices CreateEmpty()
        {
            return Create(CozyTownConfiguration.Empty());
        }
    }
}
