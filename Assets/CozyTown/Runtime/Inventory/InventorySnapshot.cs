using System;

namespace CozyTown.Runtime.Inventory
{
    [Serializable]
    public readonly struct ItemStack
    {
        public ItemStack(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public string ItemId { get; }

        public int Quantity { get; }
    }

    [Serializable]
    public sealed class InventorySnapshot
    {
        public InventorySnapshot(ItemStack[] items)
        {
            Items = items ?? Array.Empty<ItemStack>();
        }

        public ItemStack[] Items { get; }
    }
}
