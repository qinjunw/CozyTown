using System;
using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Fishing;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;

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
            string fallbackDialogue = "It's a quiet day in town.")
        {
            Items = items ?? Array.Empty<ItemDefinition>();
            ShopOffers = shopOffers ?? Array.Empty<ShopOffer>();
            Crops = crops ?? Array.Empty<CropDefinition>();
            FarmPlotIds = farmPlotIds ?? Array.Empty<string>();
            AnimalDefinitions = animalDefinitions ?? Array.Empty<AnimalDefinition>();
            Animals = animals ?? Array.Empty<AnimalSnapshot>();
            FishingEntries = fishingEntries ?? Array.Empty<FishingEntry>();
            Recipes = recipes ?? Array.Empty<RecipeDefinition>();
            InventoryCapacitySlots = inventoryCapacitySlots;
            StartingBalance = startingBalance;
            StartingDay = startingDay;
            StartingMinuteOfDay = startingMinuteOfDay;
            FallbackDialogue = fallbackDialogue;
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
    }
}
