using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Inventory
{
    public interface IInventory
    {
        int CapacitySlots { get; }

        int Count(string itemId);

        bool Contains(string itemId, int quantity);

        OperationResult Add(string itemId, int quantity);

        OperationResult Remove(string itemId, int quantity);

        InventorySnapshot CaptureSnapshot();

        OperationResult Restore(InventorySnapshot snapshot);
    }
}
