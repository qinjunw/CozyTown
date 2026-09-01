using System;
using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Fishing;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Npc;

namespace CozyTown.Runtime.Core
{
    public sealed class CozyTownConfiguration
    {
        public CozyTownConfiguration(
            ItemDefinition[] items,
            ShopOffer[] shopOffers,
            CropDefinition[] crops,
            string[] farmPlotIds,
            AnimalDefinition[] animalDefinitions,
            AnimalSnapshot[] animals,
            FishingEntry[] fishingEntries,
            RecipeDefinition[] recipes,
            int inventoryCapacitySlots = 24,
            int startingBalance = 0,
            int startingDay = 1,
            int startingMinuteOfDay = 360,
            string fallbackDialogue = "It's a quiet day in town.",
            NpcDefinition[] npcs = null,
            ShopRestockRule[] shopRestockRules = null,
            int startingWorldSeed = 0,
            int startingShopBalance = 10000)
        {
            Items = Copy(items);
            ShopOffers = Copy(shopOffers);
            Crops = Copy(crops);
            FarmPlotIds = Copy(farmPlotIds);
            AnimalDefinitions = Copy(animalDefinitions);
            Animals = Copy(animals);
            FishingEntries = Copy(fishingEntries);
            Recipes = Copy(recipes);
            InventoryCapacitySlots = inventoryCapacitySlots;
            StartingBalance = startingBalance;
            StartingDay = startingDay;
            StartingMinuteOfDay = startingMinuteOfDay;
            FallbackDialogue = fallbackDialogue;
            Npcs = Copy(npcs);
            ShopRestockRules = Copy(shopRestockRules);
            StartingWorldSeed = startingWorldSeed;
            StartingShopBalance = startingShopBalance;
        }

        public ItemDefinition[] Items { get; }

        public ShopOffer[] ShopOffers { get; }

        public CropDefinition[] Crops { get; }

        public string[] FarmPlotIds { get; }

        public AnimalDefinition[] AnimalDefinitions { get; }

        public AnimalSnapshot[] Animals { get; }

        public FishingEntry[] FishingEntries { get; }

        public RecipeDefinition[] Recipes { get; }

        public int InventoryCapacitySlots { get; }

        public int StartingBalance { get; }

        public int StartingDay { get; }

        public int StartingMinuteOfDay { get; }

        public string FallbackDialogue { get; }

        public NpcDefinition[] Npcs { get; }

        public ShopRestockRule[] ShopRestockRules { get; }

        public int StartingWorldSeed { get; }

        public int StartingShopBalance { get; }

        public static CozyTownConfiguration Empty()
        {
            return new CozyTownConfiguration(
                Array.Empty<ItemDefinition>(),
                Array.Empty<ShopOffer>(),
                Array.Empty<CropDefinition>(),
                Array.Empty<string>(),
                Array.Empty<AnimalDefinition>(),
                Array.Empty<AnimalSnapshot>(),
                Array.Empty<FishingEntry>(),
                Array.Empty<RecipeDefinition>());
        }

        private static T[] Copy<T>(T[] source)
        {
            return source == null || source.Length == 0
                ? Array.Empty<T>()
                : (T[])source.Clone();
        }
    }
}
