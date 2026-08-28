using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Fishing;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Npc;

namespace CozyTown.Runtime.Content
{
    public static class DefaultMvpContent
    {
        public static CozyTownConfiguration CreateConfiguration()
        {
            return new CozyTownConfiguration(
                CreateItems(),
                CreateShopOffers(),
                CreateCrops(),
                new[]
                {
                    "plot.01",
                    "plot.02",
                    "plot.03",
                    "plot.04",
                    "plot.05",
                    "plot.06"
                },
                new[]
                {
                    new AnimalDefinition(
                        DefaultMvpIds.Livestock.ChickenSpecies,
                        DefaultMvpIds.Items.ChickenFeed,
                        DefaultMvpIds.Items.Egg,
                        productQuantity: 1)
                },
                new[]
                {
                    new AnimalSnapshot(
                        DefaultMvpIds.Livestock.Hen,
                        DefaultMvpIds.Livestock.ChickenSpecies,
                        fedToday: false,
                        productReady: false)
                },
                CreateFishingEntries(),
                CreateRecipes(),
                inventoryCapacitySlots: 24,
                startingBalance: 300,
                startingDay: 1,
                startingMinuteOfDay: 6 * 60,
                fallbackDialogue: "It's a quiet day in town.",
                npcs: CreateNpcs());
        }

        private static ItemDefinition[] CreateItems()
        {
            return new[]
            {
                new ItemDefinition(DefaultMvpIds.Items.PotatoSeed, "Potato Seed", ItemCategory.Seed, 99),
                new ItemDefinition(DefaultMvpIds.Items.CarrotSeed, "Carrot Seed", ItemCategory.Seed, 99),
                new ItemDefinition(DefaultMvpIds.Items.TomatoSeed, "Tomato Seed", ItemCategory.Seed, 99),
                new ItemDefinition(DefaultMvpIds.Items.Potato, "Potato", ItemCategory.Crop, 99),
                new ItemDefinition(DefaultMvpIds.Items.Carrot, "Carrot", ItemCategory.Crop, 99),
                new ItemDefinition(DefaultMvpIds.Items.Tomato, "Tomato", ItemCategory.Crop, 99),
                new ItemDefinition(DefaultMvpIds.Items.ChickenFeed, "Chicken Feed", ItemCategory.Feed, 99),
                new ItemDefinition(DefaultMvpIds.Items.Egg, "Egg", ItemCategory.AnimalProduct, 99),
                new ItemDefinition(DefaultMvpIds.Items.Carp, "Carp", ItemCategory.Fish, 99),
                new ItemDefinition(DefaultMvpIds.Items.Trout, "Trout", ItemCategory.Fish, 99),
                new ItemDefinition(DefaultMvpIds.Items.Bass, "Bass", ItemCategory.Fish, 99),
                new ItemDefinition(DefaultMvpIds.Items.Salt, "Salt", ItemCategory.Material, 99),
                new ItemDefinition(DefaultMvpIds.Items.Flour, "Flour", ItemCategory.Material, 99),
                new ItemDefinition(DefaultMvpIds.Items.BakedPotato, "Baked Potato", ItemCategory.Food, 99),
                new ItemDefinition(DefaultMvpIds.Items.VegetableSoup, "Vegetable Soup", ItemCategory.Food, 99),
                new ItemDefinition(DefaultMvpIds.Items.GrilledFish, "Grilled Fish", ItemCategory.Food, 99),
                new ItemDefinition(DefaultMvpIds.Items.TomatoEgg, "Tomato and Egg", ItemCategory.Food, 99),
                new ItemDefinition(DefaultMvpIds.Items.FishPie, "Fish Pie", ItemCategory.Food, 99)
            };
        }

        private static ShopOffer[] CreateShopOffers()
        {
            return new[]
            {
                new ShopOffer(DefaultMvpIds.Items.PotatoSeed, 20, 0),
                new ShopOffer(DefaultMvpIds.Items.CarrotSeed, 25, 0),
                new ShopOffer(DefaultMvpIds.Items.TomatoSeed, 30, 0),
                new ShopOffer(DefaultMvpIds.Items.ChickenFeed, 10, 0),
                new ShopOffer(DefaultMvpIds.Items.Salt, 5, 0),
                new ShopOffer(DefaultMvpIds.Items.Flour, 10, 0),
                new ShopOffer(DefaultMvpIds.Items.Potato, 0, 20),
                new ShopOffer(DefaultMvpIds.Items.Carrot, 0, 25),
                new ShopOffer(DefaultMvpIds.Items.Tomato, 0, 30),
                new ShopOffer(DefaultMvpIds.Items.Egg, 0, 20),
                new ShopOffer(DefaultMvpIds.Items.Carp, 0, 25),
                new ShopOffer(DefaultMvpIds.Items.Trout, 0, 35),
                new ShopOffer(DefaultMvpIds.Items.Bass, 0, 45),
                new ShopOffer(DefaultMvpIds.Items.BakedPotato, 0, 50),
                new ShopOffer(DefaultMvpIds.Items.VegetableSoup, 0, 60),
                new ShopOffer(DefaultMvpIds.Items.GrilledFish, 0, 55),
                new ShopOffer(DefaultMvpIds.Items.TomatoEgg, 0, 75),
                new ShopOffer(DefaultMvpIds.Items.FishPie, 0, 100)
            };
        }

        private static CropDefinition[] CreateCrops()
        {
            return new[]
            {
                new CropDefinition(
                    DefaultMvpIds.Crops.Potato,
                    DefaultMvpIds.Items.PotatoSeed,
                    DefaultMvpIds.Items.Potato,
                    growthDays: 2,
                    harvestQuantity: 2),
                new CropDefinition(
                    DefaultMvpIds.Crops.Carrot,
                    DefaultMvpIds.Items.CarrotSeed,
                    DefaultMvpIds.Items.Carrot,
                    growthDays: 3,
                    harvestQuantity: 2),
                new CropDefinition(
                    DefaultMvpIds.Crops.Tomato,
                    DefaultMvpIds.Items.TomatoSeed,
                    DefaultMvpIds.Items.Tomato,
                    growthDays: 4,
                    harvestQuantity: 3)
            };
        }

        private static FishingEntry[] CreateFishingEntries()
        {
            return new[]
            {
                new FishingEntry(DefaultMvpIds.Fish.Carp, DefaultMvpIds.Items.Carp, 0, 40),
                new FishingEntry(DefaultMvpIds.Fish.Trout, DefaultMvpIds.Items.Trout, 40, 70),
                new FishingEntry(DefaultMvpIds.Fish.Bass, DefaultMvpIds.Items.Bass, 70, 90)
            };
        }

        private static RecipeDefinition[] CreateRecipes()
        {
            return new[]
            {
                new RecipeDefinition(
                    DefaultMvpIds.Recipes.BakedPotato,
                    new[]
                    {
                        new RecipeIngredient(DefaultMvpIds.Items.Potato, 1),
                        new RecipeIngredient(DefaultMvpIds.Items.Salt, 1)
                    },
                    DefaultMvpIds.Items.BakedPotato,
                    1),
                new RecipeDefinition(
                    DefaultMvpIds.Recipes.VegetableSoup,
                    new[]
                    {
                        new RecipeIngredient(DefaultMvpIds.Items.Carrot, 1),
                        new RecipeIngredient(DefaultMvpIds.Items.Tomato, 1)
                    },
                    DefaultMvpIds.Items.VegetableSoup,
                    1),
                new RecipeDefinition(
                    DefaultMvpIds.Recipes.GrilledFish,
                    new[]
                    {
                        new RecipeIngredient(DefaultMvpIds.Items.Carp, 1),
                        new RecipeIngredient(DefaultMvpIds.Items.Salt, 1)
                    },
                    DefaultMvpIds.Items.GrilledFish,
                    1),
                new RecipeDefinition(
                    DefaultMvpIds.Recipes.TomatoEgg,
                    new[]
                    {
                        new RecipeIngredient(DefaultMvpIds.Items.Tomato, 1),
                        new RecipeIngredient(DefaultMvpIds.Items.Egg, 1)
                    },
                    DefaultMvpIds.Items.TomatoEgg,
                    1),
                new RecipeDefinition(
                    DefaultMvpIds.Recipes.FishPie,
                    new[]
                    {
                        new RecipeIngredient(DefaultMvpIds.Items.Trout, 1),
                        new RecipeIngredient(DefaultMvpIds.Items.Egg, 1),
                        new RecipeIngredient(DefaultMvpIds.Items.Flour, 1)
                    },
                    DefaultMvpIds.Items.FishPie,
                    1)
            };
        }

        private static NpcDefinition[] CreateNpcs()
        {
            return new[]
            {
                new NpcDefinition(
                    DefaultMvpIds.Npcs.Shopkeeper,
                    "Mina",
                    "A practical and kind shopkeeper who values fair trades.",
                    "Fresh supplies are ready whenever you need them."),
                new NpcDefinition(
                    DefaultMvpIds.Npcs.Farmer,
                    "Eli",
                    "A patient farmer who explains crops in simple terms.",
                    "Watered crops grow a little stronger each day."),
                new NpcDefinition(
                    DefaultMvpIds.Npcs.Fisher,
                    "Ren",
                    "A quiet fisher who notices small changes around the pond.",
                    "The pond is calm today. Take your time."),
                new NpcDefinition(
                    DefaultMvpIds.Npcs.Cook,
                    "Sora",
                    "An energetic cook who enjoys combining local ingredients.",
                    "Bring good ingredients, and the recipe will do the rest.")
            };
        }
    }
}
