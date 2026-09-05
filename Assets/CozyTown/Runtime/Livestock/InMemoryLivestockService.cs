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
        private Dictionary<string, AnimalState> _animals;
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
                    && !(animal.FedToday && animal.ProductReady)
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

            if (animal.ProductReady)
            {
                return OperationResult.Failure("livestock.product_pending");
            }

            if (animal.FedToday)
            {
                return OperationResult.Failure("livestock.already_fed");
            }

            AnimalDefinition definition = _species[animal.SpeciesId];
            InventorySnapshot inventoryBefore = _inventory.CaptureSnapshot();
            if (inventoryBefore == null)
            {
                return OperationResult.Failure("livestock.inventory_snapshot_invalid");
            }

            OperationResult consumeFeed = _inventory.Remove(definition.FeedItemId, 1);
            if (!consumeFeed.IsSuccess)
            {
                return RollBackInventory(inventoryBefore, consumeFeed.ErrorCode);
            }

            animal.FedToday = true;
            return OperationResult.Success();
        }

        public OperationResult AdvanceDay(int newDay)
        {
            OperationResult<LivestockSnapshot> candidate = CreateDayCandidate(CaptureSnapshot(), newDay);
            return candidate.IsSuccess
                ? Restore(candidate.Value)
                : OperationResult.Failure(candidate.ErrorCode);
        }

        internal OperationResult<LivestockSnapshot> CreateDayCandidate(LivestockSnapshot current, int newDay)
        {
            OperationResult<Action> prepared = PrepareRestore(current);
            if (!prepared.IsSuccess)
            {
                return OperationResult<LivestockSnapshot>.Failure(prepared.ErrorCode);
            }

            if (newDay <= current.LastProcessedDay)
            {
                return OperationResult<LivestockSnapshot>.Failure("livestock.day_not_advanced");
            }

            if (current.LastProcessedDay == int.MaxValue || newDay != current.LastProcessedDay + 1)
            {
                return OperationResult<LivestockSnapshot>.Failure("livestock.day_not_consecutive");
            }

            AnimalSnapshot[] animals = current.Animals.Select(animal => new AnimalSnapshot(
                animal.AnimalId,
                animal.SpeciesId,
                fedToday: false,
                productReady: animal.ProductReady || animal.FedToday)).ToArray();
            return OperationResult<LivestockSnapshot>.Success(new LivestockSnapshot(newDay, animals));
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
            InventorySnapshot inventoryBefore = _inventory.CaptureSnapshot();
            if (inventoryBefore == null)
            {
                return OperationResult.Failure("livestock.inventory_snapshot_invalid");
            }

            OperationResult addProduct = _inventory.Add(
                definition.ProductItemId,
                definition.ProductQuantity);
            if (!addProduct.IsSuccess)
            {
                return RollBackInventory(inventoryBefore, addProduct.ErrorCode);
            }

            animal.ProductReady = false;
            return OperationResult.Success();
        }

        private OperationResult RollBackInventory(
            InventorySnapshot snapshot,
            string originalError)
        {
            OperationResult restore = _inventory.Restore(snapshot);
            return restore.IsSuccess
                ? OperationResult.Failure(originalError)
                : OperationResult.Failure("livestock.rollback_inventory_failed");
        }

        public LivestockSnapshot CaptureSnapshot()
        {
            return new LivestockSnapshot(_lastProcessedDay, Animals.ToArray());
        }

        public OperationResult Restore(LivestockSnapshot snapshot)
        {
            OperationResult<Action> prepared = PrepareRestore(snapshot);
            if (!prepared.IsSuccess)
            {
                return OperationResult.Failure(prepared.ErrorCode);
            }

            prepared.Value();
            return OperationResult.Success();
        }

        internal OperationResult<Action> PrepareRestore(LivestockSnapshot snapshot)
        {
            if (snapshot == null || snapshot.LastProcessedDay < 1 || snapshot.Animals.Length != _animals.Count)
            {
                return OperationResult<Action>.Failure("livestock.snapshot_invalid");
            }

            var proposed = new Dictionary<string, AnimalState>(StringComparer.Ordinal);
            foreach (AnimalSnapshot animal in snapshot.Animals)
            {
                if (!_animals.ContainsKey(animal.AnimalId ?? string.Empty)
                    || !_species.ContainsKey(animal.SpeciesId ?? string.Empty)
                    || proposed.ContainsKey(animal.AnimalId)
                    || _animals[animal.AnimalId].SpeciesId != animal.SpeciesId
                    || (animal.FedToday && animal.ProductReady))
                {
                    return OperationResult<Action>.Failure("livestock.snapshot_invalid");
                }

                proposed.Add(animal.AnimalId, new AnimalState(animal));
            }

            int completedDay = snapshot.LastProcessedDay;
            return OperationResult<Action>.Success(() =>
            {
                _animals = proposed;
                _lastProcessedDay = completedDay;
            });
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
