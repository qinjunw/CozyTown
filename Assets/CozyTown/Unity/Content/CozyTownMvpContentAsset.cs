using System;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Fishing;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Npc;
using UnityEngine;

namespace CozyTown.Unity.Content
{
    [CreateAssetMenu(
        fileName = "DefaultMvpContent",
        menuName = "CozyTown/Default MVP Content")]
    public sealed class CozyTownMvpContentAsset : ScriptableObject
    {
        [SerializeField, Min(1)] private int _inventoryCapacitySlots = 24;
        [SerializeField, Min(0)] private int _startingBalance = 300;
        [SerializeField, Min(0)] private int _startingShopBalance = 10000;
        [SerializeField, Min(1)] private int _startingDay = 1;
        [SerializeField, Range(0, 1439)] private int _startingMinuteOfDay = 360;
        [SerializeField, TextArea] private string _fallbackDialogue =
            "It's a quiet day in town.";
        [SerializeField] private NpcRecord[] _npcs = Array.Empty<NpcRecord>();
        [SerializeField] private ItemRecord[] _items = Array.Empty<ItemRecord>();
        [SerializeField] private ShopOfferRecord[] _shopOffers =
            Array.Empty<ShopOfferRecord>();
        [SerializeField] private ShopRestockRuleRecord[] _shopRestockRules =
            Array.Empty<ShopRestockRuleRecord>();
        [SerializeField] private CropRecord[] _crops = Array.Empty<CropRecord>();
        [SerializeField] private FishingEntryRecord[] _fishingEntries =
            Array.Empty<FishingEntryRecord>();
        [SerializeField] private RecipeRecord[] _recipes = Array.Empty<RecipeRecord>();

        public OperationResult<CozyTownConfiguration> Load()
        {
            CozyTownConfiguration defaults = DefaultMvpContent.CreateConfiguration();
            var configuration = new CozyTownConfiguration(
                Convert(_items, record => record?.ToDefinition()),
                Convert(_shopOffers, record => record?.ToDefinition()),
                Convert(_crops, record => record?.ToDefinition()),
                defaults.FarmPlotIds,
                defaults.AnimalDefinitions,
                defaults.Animals,
                Convert(_fishingEntries, record => record?.ToDefinition()),
                Convert(_recipes, record => record?.ToDefinition()),
                _inventoryCapacitySlots,
                _startingBalance,
                _startingDay,
                _startingMinuteOfDay,
                _fallbackDialogue,
                Convert(_npcs, record => record?.ToDefinition()),
                Convert(_shopRestockRules, record => record?.ToDefinition()),
                defaults.StartingWorldSeed,
                _startingShopBalance);
            OperationResult validation = MvpContentValidator.Validate(configuration);
            if (!validation.IsSuccess)
            {
                return OperationResult<CozyTownConfiguration>.Failure(validation.ErrorCode);
            }

            return OperationResult<CozyTownConfiguration>.Success(configuration);
        }

#if UNITY_EDITOR
        public static CozyTownMvpContentAsset CreateDefaultForEditor()
        {
            CozyTownConfiguration configuration = DefaultMvpContent.CreateConfiguration();
            var asset = CreateInstance<CozyTownMvpContentAsset>();
            asset._inventoryCapacitySlots = configuration.InventoryCapacitySlots;
            asset._startingBalance = configuration.StartingBalance;
            asset._startingShopBalance = configuration.StartingShopBalance;
            asset._startingDay = configuration.StartingDay;
            asset._startingMinuteOfDay = configuration.StartingMinuteOfDay;
            asset._fallbackDialogue = configuration.FallbackDialogue;
            asset._npcs = configuration.Npcs.Select(NpcRecord.From).ToArray();
            asset._items = configuration.Items.Select(ItemRecord.From).ToArray();
            asset._shopOffers = configuration.ShopOffers.Select(ShopOfferRecord.From).ToArray();
            asset._shopRestockRules = configuration.ShopRestockRules
                .Select(ShopRestockRuleRecord.From)
                .ToArray();
            asset._crops = configuration.Crops.Select(CropRecord.From).ToArray();
            asset._fishingEntries = configuration.FishingEntries
                .Select(FishingEntryRecord.From)
                .ToArray();
            asset._recipes = configuration.Recipes.Select(RecipeRecord.From).ToArray();
            return asset;
        }
#endif

        private static TDefinition[] Convert<TRecord, TDefinition>(
            TRecord[] source,
            Func<TRecord, TDefinition> convert)
        {
            return source == null || source.Length == 0
                ? Array.Empty<TDefinition>()
                : source.Select(convert).ToArray();
        }

        [Serializable]
        private sealed class NpcRecord
        {
            [SerializeField] private string _id = string.Empty;
            [SerializeField] private string _displayName = string.Empty;
            [SerializeField, TextArea] private string _persona = string.Empty;
            [SerializeField, TextArea] private string _fallbackDialogue = string.Empty;

            internal NpcDefinition ToDefinition()
            {
                return new NpcDefinition(
                    _id,
                    _displayName,
                    _persona,
                    _fallbackDialogue);
            }

            internal static NpcRecord From(NpcDefinition definition)
            {
                return new NpcRecord
                {
                    _id = definition.Id,
                    _displayName = definition.DisplayName,
                    _persona = definition.Persona,
                    _fallbackDialogue = definition.FallbackDialogue
                };
            }
        }

        [Serializable]
        private sealed class ItemRecord
        {
            [SerializeField] private string _id = string.Empty;
            [SerializeField] private string _displayName = string.Empty;
            [SerializeField] private ItemCategory _category;
            [SerializeField, Min(1)] private int _maxStack = 1;

            internal ItemDefinition ToDefinition()
            {
                return new ItemDefinition(_id, _displayName, _category, _maxStack);
            }

            internal static ItemRecord From(ItemDefinition definition)
            {
                return new ItemRecord
                {
                    _id = definition.Id,
                    _displayName = definition.DisplayName,
                    _category = definition.Category,
                    _maxStack = definition.MaxStack
                };
            }
        }

        [Serializable]
        private sealed class ShopOfferRecord
        {
            [SerializeField] private string _itemId = string.Empty;
            [SerializeField, Min(0)] private int _buyPrice;
            [SerializeField, Min(0)] private int _sellPrice;

            internal ShopOffer ToDefinition()
            {
                return new ShopOffer(_itemId, _buyPrice, _sellPrice);
            }

            internal static ShopOfferRecord From(ShopOffer definition)
            {
                return new ShopOfferRecord
                {
                    _itemId = definition.ItemId,
                    _buyPrice = definition.BuyPrice,
                    _sellPrice = definition.SellPrice
                };
            }
        }

        [Serializable]
        private sealed class ShopRestockRuleRecord
        {
            [SerializeField] private string _itemId = string.Empty;
            [SerializeField, Range(0, 1000)] private int _appearancePermille;
            [SerializeField, Min(1)] private int _minQuantity = 1;
            [SerializeField, Min(1)] private int _maxQuantity = 1;

            internal ShopRestockRule ToDefinition()
            {
                return new ShopRestockRule(
                    _itemId,
                    _appearancePermille,
                    _minQuantity,
                    _maxQuantity);
            }

            internal static ShopRestockRuleRecord From(ShopRestockRule definition)
            {
                return new ShopRestockRuleRecord
                {
                    _itemId = definition.ItemId,
                    _appearancePermille = definition.AppearancePermille,
                    _minQuantity = definition.MinQuantity,
                    _maxQuantity = definition.MaxQuantity
                };
            }
        }

        [Serializable]
        private sealed class CropRecord
        {
            [SerializeField] private string _id = string.Empty;
            [SerializeField] private string _seedItemId = string.Empty;
            [SerializeField] private string _harvestItemId = string.Empty;
            [SerializeField, Min(1)] private int _growthDays = 1;
            [SerializeField, Min(1)] private int _harvestQuantity = 1;

            internal CropDefinition ToDefinition()
            {
                return new CropDefinition(
                    _id,
                    _seedItemId,
                    _harvestItemId,
                    _growthDays,
                    _harvestQuantity);
            }

            internal static CropRecord From(CropDefinition definition)
            {
                return new CropRecord
                {
                    _id = definition.Id,
                    _seedItemId = definition.SeedItemId,
                    _harvestItemId = definition.HarvestItemId,
                    _growthDays = definition.GrowthDays,
                    _harvestQuantity = definition.HarvestQuantity
                };
            }
        }

        [Serializable]
        private sealed class FishingEntryRecord
        {
            [SerializeField] private string _fishId = string.Empty;
            [SerializeField] private string _itemId = string.Empty;
            [SerializeField] private int _minRollInclusive;
            [SerializeField] private int _maxRollExclusive = 1;

            internal FishingEntry ToDefinition()
            {
                return new FishingEntry(
                    _fishId,
                    _itemId,
                    _minRollInclusive,
                    _maxRollExclusive);
            }

            internal static FishingEntryRecord From(FishingEntry definition)
            {
                return new FishingEntryRecord
                {
                    _fishId = definition.FishId,
                    _itemId = definition.ItemId,
                    _minRollInclusive = definition.MinRollInclusive,
                    _maxRollExclusive = definition.MaxRollExclusive
                };
            }
        }

        [Serializable]
        private sealed class RecipeRecord
        {
            [SerializeField] private string _id = string.Empty;
            [SerializeField] private RecipeIngredientRecord[] _ingredients =
                Array.Empty<RecipeIngredientRecord>();
            [SerializeField] private string _outputItemId = string.Empty;
            [SerializeField, Min(1)] private int _outputQuantity = 1;

            internal RecipeDefinition ToDefinition()
            {
                return new RecipeDefinition(
                    _id,
                    Convert(
                        _ingredients,
                        ingredient => ingredient == null
                            ? default
                            : ingredient.ToDefinition()),
                    _outputItemId,
                    _outputQuantity);
            }

            internal static RecipeRecord From(RecipeDefinition definition)
            {
                return new RecipeRecord
                {
                    _id = definition.Id,
                    _ingredients = definition.Ingredients
                        .Select(RecipeIngredientRecord.From)
                        .ToArray(),
                    _outputItemId = definition.OutputItemId,
                    _outputQuantity = definition.OutputQuantity
                };
            }
        }

        [Serializable]
        private sealed class RecipeIngredientRecord
        {
            [SerializeField] private string _itemId = string.Empty;
            [SerializeField, Min(1)] private int _quantity = 1;

            internal RecipeIngredient ToDefinition()
            {
                return new RecipeIngredient(_itemId, _quantity);
            }

            internal static RecipeIngredientRecord From(RecipeIngredient ingredient)
            {
                return new RecipeIngredientRecord
                {
                    _itemId = ingredient.ItemId,
                    _quantity = ingredient.Quantity
                };
            }
        }
    }
}
