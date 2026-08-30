using System;
using System.Collections.Generic;

namespace CozyTown.Runtime.Inventory
{
    public readonly struct InventorySlotProjection
    {
        public InventorySlotProjection(string itemId, string displayName, int quantity)
        {
            ItemId = itemId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Quantity = quantity;
        }

        public string ItemId { get; }

        public string DisplayName { get; }

        public int Quantity { get; }

        public bool IsEmpty => string.IsNullOrEmpty(ItemId);
    }

    public sealed class InventoryProjection
    {
        private readonly IReadOnlyList<InventorySlotProjection> _slots;

        public InventoryProjection(int capacitySlots, InventorySlotProjection[] slots)
        {
            if (capacitySlots < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacitySlots));
            }

            var copy = slots == null || slots.Length == 0
                ? Array.Empty<InventorySlotProjection>()
                : (InventorySlotProjection[])slots.Clone();
            if (copy.Length != capacitySlots)
            {
                throw new ArgumentException(
                    "Inventory projection slots must match its capacity.",
                    nameof(slots));
            }

            CapacitySlots = capacitySlots;
            _slots = Array.AsReadOnly(copy);
        }

        public int CapacitySlots { get; }

        public IReadOnlyList<InventorySlotProjection> Slots => _slots;
    }
}
