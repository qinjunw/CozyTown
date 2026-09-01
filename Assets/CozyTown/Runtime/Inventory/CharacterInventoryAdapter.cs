using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;

namespace CozyTown.Runtime.Inventory
{
    public sealed class CharacterInventoryAdapter : IInventory, IInventoryProjection
    {
        private readonly ItemDefinition[] _catalog;
        private readonly string _characterId;
        private readonly IEconomyStateStore _stateStore;

        public CharacterInventoryAdapter(
            IEnumerable<ItemDefinition> catalog,
            int capacitySlots,
            string characterId,
            IEconomyStateStore stateStore)
        {
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            if (string.IsNullOrWhiteSpace(characterId))
            {
                throw new ArgumentException(
                    "Character ID must be provided.",
                    nameof(characterId));
            }

            _catalog = (catalog ?? Array.Empty<ItemDefinition>()).ToArray();
            CapacitySlots = capacitySlots;
            var backpack = new InMemoryInventory(_catalog, CapacitySlots);
            if (!_stateStore.TryGetCharacter(characterId, out CharacterEconomySnapshot character))
            {
                throw new ArgumentException(
                    "Character economy state must exist before creating an inventory adapter.",
                    nameof(characterId));
            }

            if (!backpack.Restore(character.Backpack).IsSuccess)
            {
                throw new ArgumentException(
                    "Character backpack must be valid for the supplied catalog and capacity.",
                    nameof(characterId));
            }

            _characterId = characterId;
        }

        public int CapacitySlots { get; }

        public int Count(string itemId)
        {
            return CurrentBackpack().Count(itemId);
        }

        public bool Contains(string itemId, int quantity)
        {
            return CurrentBackpack().Contains(itemId, quantity);
        }

        public OperationResult Add(string itemId, int quantity)
        {
            return Mutate(backpack => backpack.Add(itemId, quantity));
        }

        public OperationResult Remove(string itemId, int quantity)
        {
            return Mutate(backpack => backpack.Remove(itemId, quantity));
        }

        public InventorySnapshot CaptureSnapshot()
        {
            return CurrentBackpack().CaptureSnapshot();
        }

        public OperationResult Restore(InventorySnapshot snapshot)
        {
            return Mutate(backpack => backpack.Restore(snapshot));
        }

        public InventoryProjection CaptureProjection()
        {
            return CurrentBackpack().CaptureProjection();
        }

        private OperationResult Mutate(Func<InMemoryInventory, OperationResult> mutation)
        {
            if (!_stateStore.TryGetCharacter(
                    _characterId,
                    out CharacterEconomySnapshot character))
            {
                return OperationResult.Failure("economy.character_unknown");
            }

            var backpack = new InMemoryInventory(_catalog, CapacitySlots);
            OperationResult restore = backpack.Restore(character.Backpack);
            if (!restore.IsSuccess)
            {
                return restore;
            }

            OperationResult result = mutation(backpack);
            if (!result.IsSuccess)
            {
                return result;
            }

            return _stateStore.CommitCharacter(
                new CharacterEconomySnapshot(
                    character.CharacterId,
                    backpack.CaptureSnapshot(),
                    character.Wallet));
        }

        private InMemoryInventory CurrentBackpack()
        {
            if (!_stateStore.TryGetCharacter(
                    _characterId,
                    out CharacterEconomySnapshot character))
            {
                throw new InvalidOperationException(
                    "The character economy state is no longer available.");
            }

            var backpack = new InMemoryInventory(_catalog, CapacitySlots);
            OperationResult restore = backpack.Restore(character.Backpack);
            if (!restore.IsSuccess)
            {
                throw new InvalidOperationException(
                    "The character backpack is incompatible with its inventory adapter.");
            }

            return backpack;
        }
    }
}
