using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;

namespace CozyTown.Runtime.Application
{
    public sealed class LivestockGameplayCoordinator : ILivestockGameplayCoordinator
    {
        private readonly IReadOnlyDictionary<string, AnimalDefinition> _definitions;
        private readonly IReadOnlyDictionary<string, string> _displayNames;
        private readonly ILivestockService _livestock;
        private readonly IInventory _inventory;

        public LivestockGameplayCoordinator(
            IEnumerable<ItemDefinition> items,
            IEnumerable<AnimalDefinition> definitions,
            ILivestockService livestock,
            IInventory inventory)
        {
            _livestock = livestock ?? throw new ArgumentNullException(nameof(livestock));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _displayNames = BuildDisplayNames(items);
            _definitions = BuildDefinitions(definitions, _displayNames);
        }

        public LivestockViewState GetCurrentState()
        {
            AnimalView[] animals = _livestock.Animals
                .OrderBy(animal => animal.AnimalId, StringComparer.Ordinal)
                .Select(ToAnimalView)
                .ToArray();
            return new LivestockViewState(animals);
        }

        public OperationResult Feed(string animalId) => _livestock.Feed(animalId);

        public OperationResult CollectProduct(string animalId) =>
            _livestock.CollectProduct(animalId);

        private AnimalView ToAnimalView(AnimalSnapshot animal)
        {
            if (!_definitions.TryGetValue(
                    animal.SpeciesId ?? string.Empty,
                    out AnimalDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Animal '{animal.AnimalId}' references an unknown species.");
            }

            return new AnimalView(
                animal.AnimalId,
                animal.SpeciesId,
                definition.FeedItemId,
                _displayNames[definition.FeedItemId],
                _inventory.Count(definition.FeedItemId),
                definition.ProductItemId,
                _displayNames[definition.ProductItemId],
                definition.ProductQuantity,
                animal.FedToday,
                animal.ProductReady);
        }

        private static IReadOnlyDictionary<string, string> BuildDisplayNames(
            IEnumerable<ItemDefinition> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item == null
                    || string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.DisplayName)
                    || !displayNames.TryAdd(item.Id, item.DisplayName))
                {
                    throw new ArgumentException(
                        "Item definitions must have unique IDs and display names.",
                        nameof(items));
                }
            }

            return displayNames;
        }

        private static IReadOnlyDictionary<string, AnimalDefinition> BuildDefinitions(
            IEnumerable<AnimalDefinition> definitions,
            IReadOnlyDictionary<string, string> displayNames)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var result = new Dictionary<string, AnimalDefinition>(StringComparer.Ordinal);
            foreach (AnimalDefinition definition in definitions)
            {
                if (definition == null
                    || string.IsNullOrWhiteSpace(definition.SpeciesId)
                    || !displayNames.ContainsKey(definition.FeedItemId ?? string.Empty)
                    || !displayNames.ContainsKey(definition.ProductItemId ?? string.Empty)
                    || !result.TryAdd(definition.SpeciesId, definition))
                {
                    throw new ArgumentException(
                        "Animal definitions must have unique species and known items.",
                        nameof(definitions));
                }
            }

            return result;
        }
    }
}
