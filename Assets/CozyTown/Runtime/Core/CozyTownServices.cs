using System;
using CozyTown.Runtime.Application;
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
    public sealed class CozyTownServices
    {
        public CozyTownServices(
            IDayTransitionCoordinator dayTransition,
            ITimeService time,
            IInventory inventory,
            IWallet wallet,
            ICharacterShopTradingCoordinator shopTrading,
            IFarmService farm,
            IFarmGameplayCoordinator farmGameplay,
            ILivestockService livestock,
            ILivestockGameplayCoordinator livestockGameplay,
            IFishingService fishing,
            IFishingGameplayCoordinator fishingGameplay,
            ICookingService cooking,
            ICookingGameplayCoordinator cookingGameplay,
            INpcDialogueGenerator npcDialogue,
            INpcDialogueCoordinator npcDialogueGameplay,
            ISaveStorage saveStorage,
            IGameSaveCoordinator gameSave,
            IEconomyStateStore economyState,
            IWorldSeedState worldSeed,
            IDaytimeClock daytimeClock)
        {
            DayTransition = dayTransition ?? throw new ArgumentNullException(nameof(dayTransition));
            Time = time ?? throw new ArgumentNullException(nameof(time));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            InventoryProjection = inventory as IInventoryProjection
                ?? throw new ArgumentException(
                    $"{nameof(inventory)} must implement {nameof(IInventoryProjection)}.",
                    nameof(inventory));
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            ShopTrading = shopTrading ?? throw new ArgumentNullException(nameof(shopTrading));
            Farm = farm ?? throw new ArgumentNullException(nameof(farm));
            FarmGameplay = farmGameplay ?? throw new ArgumentNullException(nameof(farmGameplay));
            Livestock = livestock ?? throw new ArgumentNullException(nameof(livestock));
            LivestockGameplay = livestockGameplay
                ?? throw new ArgumentNullException(nameof(livestockGameplay));
            Fishing = fishing ?? throw new ArgumentNullException(nameof(fishing));
            FishingGameplay = fishingGameplay
                ?? throw new ArgumentNullException(nameof(fishingGameplay));
            Cooking = cooking ?? throw new ArgumentNullException(nameof(cooking));
            CookingGameplay = cookingGameplay
                ?? throw new ArgumentNullException(nameof(cookingGameplay));
            NpcDialogue = npcDialogue ?? throw new ArgumentNullException(nameof(npcDialogue));
            NpcDialogueGameplay = npcDialogueGameplay
                ?? throw new ArgumentNullException(nameof(npcDialogueGameplay));
            SaveStorage = saveStorage ?? throw new ArgumentNullException(nameof(saveStorage));
            GameSave = gameSave ?? throw new ArgumentNullException(nameof(gameSave));
            EconomyState = economyState
                ?? throw new ArgumentNullException(nameof(economyState));
            WorldSeed = worldSeed ?? throw new ArgumentNullException(nameof(worldSeed));
            DaytimeClock = daytimeClock ?? throw new ArgumentNullException(nameof(daytimeClock));
        }

        public IDayTransitionCoordinator DayTransition { get; }

        public ITimeService Time { get; }

        public IDaytimeClock DaytimeClock { get; }

        public IInventory Inventory { get; }

        public IInventoryProjection InventoryProjection { get; }

        public IWallet Wallet { get; }

        public ICharacterShopTradingCoordinator ShopTrading { get; }

        public IFarmService Farm { get; }

        public IFarmGameplayCoordinator FarmGameplay { get; }

        public ILivestockService Livestock { get; }

        public ILivestockGameplayCoordinator LivestockGameplay { get; }

        public IFishingService Fishing { get; }

        public IFishingGameplayCoordinator FishingGameplay { get; }

        public ICookingService Cooking { get; }

        public ICookingGameplayCoordinator CookingGameplay { get; }

        public INpcDialogueGenerator NpcDialogue { get; }

        public INpcDialogueCoordinator NpcDialogueGameplay { get; }

        public ISaveStorage SaveStorage { get; }

        public IGameSaveCoordinator GameSave { get; }

        public IEconomyStateStore EconomyState { get; }

        public IWorldSeedState WorldSeed { get; }
    }
}
