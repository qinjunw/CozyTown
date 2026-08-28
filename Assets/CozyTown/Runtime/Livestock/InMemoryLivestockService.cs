using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Livestock
{
    public sealed class InMemoryLivestockService : ILivestockService
    {
        private readonly Dictionary<string, AnimalDefinition> _species;
        private readonly Dictionary<string, AnimalState> _animals;
        private readonly IInventory _inventory;
        private int _lastProcessedDay;

        public InMemoryLivestockService(
            IEnumerable<AnimalSnapshot> animals,
            IEnumerable<AnimalDefinition> species,
            IInventory inventory,
            int startingDay = 1)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _lastProcessedDay = startingDay < 1 ? 1 : startingDay;
            _species = (species ?? Array.Empty<AnimalDefinition>())
                .Where(IsValidDefinition)
                .GroupBy(definition => definition.SpeciesId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            _animals = new Dictionary<string, AnimalState>(StringComparer.Ordinal);

            foreach (AnimalSnapshot animal in animals ?? Array.Empty<AnimalSnapshot>())
            {
                if (!string.IsNullOrWhiteSpace(animal.AnimalId)
                    && _species.ContainsKey(animal.SpeciesId ?? string.Empty)
                    && !_animals.ContainsKey(animal.AnimalId))
                {
                    _animals.Add(animal.AnimalId, new AnimalState(animal));
                }
            }
        }

        public IReadOnlyCollection<AnimalSnapshot> Animals => _animals.Values
            .OrderBy(animal => animal.Id, StringComparer.Ordinal)
            .Select(ToSnapshot)
            .ToArray();

        public OperationResult Feed(string animalId)
        {
            if (!_animals.TryGetValue(animalId ?? string.Empty, out AnimalState animal))
            {
                return OperationResult.Failure("livestock.animal_missing");
            }

            if (animal.FedToday)
            {
                return OperationResult.Failure("livestock.already_fed");
            }

            AnimalDefinition definition = _species[animal.SpeciesId];
            OperationResult consumeFeed = _inventory.Remove(definition.FeedItemId, 1);
            if (!consumeFeed.IsSuccess)
            {
                return consumeFeed;
            }

            animal.FedToday = true;
            return OperationResult.Success();
        }

        public OperationResult AdvanceDay(int newDay)
        {
            if (newDay <= _lastProcessedDay)
            {
                return OperationResult.Failure("livestock.day_not_advanced");
            }

            foreach (AnimalState animal in _animals.Values)
            {
                if (animal.FedToday)
                {
                    animal.ProductReady = true;
                }

                animal.FedToday = false;
            }

            _lastProcessedDay = newDay;
            return OperationResult.Success();
        }

        public OperationResult CollectProduct(string animalId)
        {
            if (!_animals.TryGetValue(animalId ?? string.Empty, out AnimalState animal))
            {
                return OperationResult.Failure("livestock.animal_missing");
            }

            if (!animal.ProductReady)
            {
                return OperationResult.Failure("livestock.product_not_ready");
            }

            AnimalDefinition definition = _species[animal.SpeciesId];
            OperationResult addProduct = _inventory.Add(
                definition.ProductItemId,
                definition.ProductQuantity);
            if (!addProduct.IsSuccess)
            {
                return addProduct;
            }

            animal.ProductReady = false;
            return OperationResult.Success();
        }

        public LivestockSnapshot CaptureSnapshot()
        {
            return new LivestockSnapshot(_lastProcessedDay, Animals.ToArray());
        }

        public OperationResult Restore(LivestockSnapshot snapshot)
        {
            if (snapshot == null || snapshot.LastProcessedDay < 1 || snapshot.Animals.Length != _animals.Count)
            {
                return OperationResult.Failure("livestock.snapshot_invalid");
            }

            var proposed = new Dictionary<string, AnimalSnapshot>(StringComparer.Ordinal);
            foreach (AnimalSnapshot animal in snapshot.Animals)
            {
                if (!_animals.ContainsKey(animal.AnimalId ?? string.Empty)
                    || !_species.ContainsKey(animal.SpeciesId ?? string.Empty)
                    || proposed.ContainsKey(animal.AnimalId)
                    || _animals[animal.AnimalId].SpeciesId != animal.SpeciesId)
                {
                    return OperationResult.Failure("livestock.snapshot_invalid");
                }

                proposed.Add(animal.AnimalId, animal);
            }

            foreach (KeyValuePair<string, AnimalSnapshot> pair in proposed)
            {
                AnimalState target = _animals[pair.Key];
                target.FedToday = pair.Value.FedToday;
                target.ProductReady = pair.Value.ProductReady;
            }

            _lastProcessedDay = snapshot.LastProcessedDay;
            return OperationResult.Success();
        }

        private static bool IsValidDefinition(AnimalDefinition definition)
        {
            return definition != null
                && !string.IsNullOrWhiteSpace(definition.SpeciesId)
                && !string.IsNullOrWhiteSpace(definition.FeedItemId)
                && !string.IsNullOrWhiteSpace(definition.ProductItemId)
                && definition.ProductQuantity > 0;
        }

        private static AnimalSnapshot ToSnapshot(AnimalState animal)
        {
            return new AnimalSnapshot(
                animal.Id,
                animal.SpeciesId,
                animal.FedToday,
                animal.ProductReady);
        }

        private sealed class AnimalState
        {
            public AnimalState(AnimalSnapshot snapshot)
            {
                Id = snapshot.AnimalId;
                SpeciesId = snapshot.SpeciesId;
                FedToday = snapshot.FedToday;
                ProductReady = snapshot.ProductReady;
            }

            public string Id { get; }

            public string SpeciesId { get; }

            public bool FedToday { get; set; }

            public bool ProductReady { get; set; }
        }
    }
}
