using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Tests.EditMode.TestDoubles
{
    internal sealed class FaultInjectingInventory : IInventory
    {
        private readonly IInventory _inner;

        public FaultInjectingInventory(IInventory inner)
        {
            _inner = inner;
        }

        public bool FailAddAfterMutation { get; set; }
        public bool FailRemoveAfterMutation { get; set; }
        public bool FailRestore { get; set; }
        public int RestoreCallCount { get; private set; }
        public int CapacitySlots => _inner.CapacitySlots;

        public int Count(string itemId) => _inner.Count(itemId);

        public bool Contains(string itemId, int quantity) =>
            _inner.Contains(itemId, quantity);

        public OperationResult Add(string itemId, int quantity)
        {
            OperationResult result = _inner.Add(itemId, quantity);
            return result.IsSuccess && FailAddAfterMutation
                ? OperationResult.Failure("injected.add_failure")
                : result;
        }

        public OperationResult Remove(string itemId, int quantity)
        {
            OperationResult result = _inner.Remove(itemId, quantity);
            return result.IsSuccess && FailRemoveAfterMutation
                ? OperationResult.Failure("injected.remove_failure")
                : result;
        }

        public InventorySnapshot CaptureSnapshot() => _inner.CaptureSnapshot();

        public OperationResult Restore(InventorySnapshot snapshot)
        {
            RestoreCallCount++;
            return FailRestore
                ? OperationResult.Failure("injected.restore_failure")
                : _inner.Restore(snapshot);
        }
    }
}
