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
            return Create(configuration, npcDialogue: null, saveStorage: null);
        }

        public static CozyTownServices Create(
            CozyTownConfiguration configuration,
            INpcDialogueGenerator npcDialogue,
            ISaveStorage saveStorage)
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
            var worldSeed = new InMemoryWorldSeedState(configuration.StartingWorldSeed);
            var initialInventory = new InMemoryInventory(
                configuration.Items,
                configuration.InventoryCapacitySlots);
            var initialCharacter = new CharacterEconomySnapshot(
                DefaultMvpIds.Characters.Player,
                initialInventory.CaptureSnapshot(),
                new WalletSnapshot(configuration.StartingBalance));
            IShopStockReplacementPolicy restockPolicy = null;
            ShopEconomySnapshot[] initialShops = Array.Empty<ShopEconomySnapshot>();
            if (configuration.ShopRestockRules.Length > 0)
            {
                restockPolicy = new DeterministicShopStockReplacementPolicy(
                    configuration.ShopRestockRules,
                    minimumDistinctItems: 4);
                var initialShop = new ShopEconomySnapshot(
                    DefaultMvpIds.Shops.TownGeneral,
                    new InventorySnapshot(Array.Empty<ItemStack>()),
                    new WalletSnapshot(DefaultMvpContent.DefaultShopStartingBalance),
                    configuration.StartingDay - 1,
                    DeterministicShopStockReplacementPolicy.VersionOne);
                OperationResult<ShopEconomySnapshot> initialRestock =
                    restockPolicy.CreateCandidate(
                        worldSeed.Value,
                        initialShop,
                        configuration.StartingDay);
                if (!initialRestock.IsSuccess)
                {
                    throw new ArgumentException(
                        $"Initial shop restock failed: {initialRestock.ErrorCode}",
                        nameof(configuration));
                }

                initialShops = new[] { initialRestock.Value };
            }

            IEconomyStateStore economyState = new InMemoryEconomyStateStore(
                new[] { initialCharacter },
                initialShops,
                configuration.Items,
                configuration.InventoryCapacitySlots);
            var inventory = new CharacterInventoryAdapter(
                configuration.Items,
                configuration.InventoryCapacitySlots,
                DefaultMvpIds.Characters.Player,
                economyState);
            var wallet = new CharacterWalletAdapter(
                DefaultMvpIds.Characters.Player,
                economyState);
            var shopTrading = new CharacterShopTradingCoordinator(
                configuration.Items,
                configuration.ShopOffers,
                configuration.InventoryCapacitySlots,
                economyState);
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
            IDayTransitionCoordinator dayTransition = restockPolicy == null
                ? new DayTransitionCoordinator(time, farm, livestock)
                : new DayTransitionCoordinator(
                    time,
                    farm,
                    livestock,
                    economyState,
                    restockPolicy,
                    worldSeed,
                    DefaultMvpIds.Shops.TownGeneral);
            npcDialogue = npcDialogue ?? (configuration.Npcs.Length == 0
                ? (INpcDialogueGenerator)new FixedFallbackDialogueGenerator(
                    configuration.FallbackDialogue)
                : new ConfiguredFallbackDialogueGenerator(
                    configuration.Npcs,
                    configuration.FallbackDialogue));
            saveStorage = saveStorage ?? new InMemorySaveStorage();
            var npcDialogueGameplay = new NpcDialogueCoordinator(
                configuration.Npcs,
                npcDialogue,
                () => time.Current);
            var gameSave = new GameSaveCoordinator(
                worldSeed,
                time,
                economyState,
                farm,
                livestock,
                saveStorage);

            return new CozyTownServices(
                dayTransition,
                time,
                inventory,
                wallet,
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
                npcDialogueGameplay,
                saveStorage,
                gameSave,
                economyState,
                worldSeed);
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
