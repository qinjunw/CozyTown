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
            IShopService shop,
            IShopTradingCoordinator shopTrading,
            IFarmService farm,
            ILivestockService livestock,
            IFishingService fishing,
            ICookingService cooking,
            INpcDialogueGenerator npcDialogue,
            ISaveStorage saveStorage)
        {
            DayTransition = dayTransition ?? throw new ArgumentNullException(nameof(dayTransition));
            Time = time ?? throw new ArgumentNullException(nameof(time));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            Shop = shop ?? throw new ArgumentNullException(nameof(shop));
            ShopTrading = shopTrading ?? throw new ArgumentNullException(nameof(shopTrading));
            Farm = farm ?? throw new ArgumentNullException(nameof(farm));
            Livestock = livestock ?? throw new ArgumentNullException(nameof(livestock));
            Fishing = fishing ?? throw new ArgumentNullException(nameof(fishing));
            Cooking = cooking ?? throw new ArgumentNullException(nameof(cooking));
            NpcDialogue = npcDialogue ?? throw new ArgumentNullException(nameof(npcDialogue));
            SaveStorage = saveStorage ?? throw new ArgumentNullException(nameof(saveStorage));
        }

        public IDayTransitionCoordinator DayTransition { get; }

        public ITimeService Time { get; }

        public IInventory Inventory { get; }

        public IWallet Wallet { get; }

        public IShopService Shop { get; }

        public IShopTradingCoordinator ShopTrading { get; }

        public IFarmService Farm { get; }

        public ILivestockService Livestock { get; }

        public IFishingService Fishing { get; }

        public ICookingService Cooking { get; }

        public INpcDialogueGenerator NpcDialogue { get; }

        public ISaveStorage SaveStorage { get; }
    }
}
