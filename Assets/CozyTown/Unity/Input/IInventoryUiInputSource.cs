namespace CozyTown.Unity.Input
{
    public interface IInventoryUiInputSource
    {
        bool BackpackTogglePressedThisFrame { get; }

        int HotbarSelectionPressedThisFrame { get; }
    }
}
