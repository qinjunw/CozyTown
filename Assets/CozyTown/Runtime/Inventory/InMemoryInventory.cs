using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Inventory
{
    public sealed class InMemoryInventory : IInventory
    {
        private readonly Dictionary<string, ItemDefinition> _catalog;
        private readonly Dictionary<string, int> _quantities = new Dictionary<string, int>();

        public InMemoryInventory(IEnumerable<ItemDefinition> catalog, int capacitySlots)
        {
            if (capacitySlots <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacitySlots));
            }

            CapacitySlots = capacitySlots;
            _catalog = (catalog ?? Array.Empty<ItemDefinition>())
                .Where(IsValidDefinition)
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        public int CapacitySlots { get; }

        public int Count(string itemId)
        {
            return itemId != null && _quantities.TryGetValue(itemId, out int quantity) ? quantity : 0;
        }

        public bool Contains(string itemId, int quantity)
        {
            return quantity > 0 && Count(itemId) >= quantity;
        }

        public OperationResult Add(string itemId, int quantity)
        {
            if (quantity <= 0)
            {
                return OperationResult.Failure("inventory.quantity_invalid");
            }

            if (!_catalog.TryGetValue(itemId ?? string.Empty, out ItemDefinition definition))
            {
                return OperationResult.Failure("inventory.item_unknown");
            }

            int existing = Count(itemId);
            if (existing > int.MaxValue - quantity)
            {
                return OperationResult.Failure("inventory.quantity_overflow");
            }

            int proposed = existing + quantity;
            long currentSlots = UsedSlots();
            long existingSlots = SlotsFor(existing, definition.MaxStack);
            long proposedSlots = SlotsFor(proposed, definition.MaxStack);
            if (currentSlots - existingSlots + proposedSlots > CapacitySlots)
            {
                return OperationResult.Failure("inventory.capacity_exceeded");
            }

            _quantities[itemId] = proposed;
            return OperationResult.Success();
        }

        public OperationResult Remove(string itemId, int quantity)
        {
            if (quantity <= 0)
            {
                return OperationResult.Failure("inventory.quantity_invalid");
            }

            int existing = Count(itemId);
            if (existing < quantity)
            {
                return OperationResult.Failure("inventory.insufficient_quantity");
            }

            int remainder = existing - quantity;
            if (remainder == 0)
            {
                _quantities.Remove(itemId);
            }
            else
            {
                _quantities[itemId] = remainder;
            }

            return OperationResult.Success();
        }

        public InventorySnapshot CaptureSnapshot()
        {
            ItemStack[] items = _quantities
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ItemStack(pair.Key, pair.Value))
                .ToArray();
            return new InventorySnapshot(items);
        }

        public OperationResult Restore(InventorySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return OperationResult.Failure("inventory.snapshot_missing");
            }

            var proposed = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ItemStack stack in snapshot.Items)
            {
                if (stack.Quantity <= 0 || !_catalog.ContainsKey(stack.ItemId ?? string.Empty))
                {
                    return OperationResult.Failure("inventory.snapshot_invalid");
                }

                if (proposed.ContainsKey(stack.ItemId))
                {
                    return OperationResult.Failure("inventory.snapshot_duplicate_item");
                }

                proposed.Add(stack.ItemId, stack.Quantity);
            }

            if (UsedSlots(proposed) > CapacitySlots)
            {
                return OperationResult.Failure("inventory.capacity_exceeded");
            }

            _quantities.Clear();
            foreach (KeyValuePair<string, int> pair in proposed)
            {
                _quantities.Add(pair.Key, pair.Value);
            }

            return OperationResult.Success();
        }

        private static bool IsValidDefinition(ItemDefinition definition)
        {
            return definition != null
                && !string.IsNullOrWhiteSpace(definition.Id)
                && definition.MaxStack > 0;
        }

        private long UsedSlots()
        {
            return UsedSlots(_quantities);
        }

        private long UsedSlots(IReadOnlyDictionary<string, int> quantities)
        {
            long slots = 0;
            foreach (KeyValuePair<string, int> pair in quantities)
            {
                slots += SlotsFor(pair.Value, _catalog[pair.Key].MaxStack);
            }

            return slots;
        }

        private static long SlotsFor(int quantity, int maxStack)
        {
            return quantity == 0 ? 0 : ((long)quantity + maxStack - 1) / maxStack;
        }
    }
}
