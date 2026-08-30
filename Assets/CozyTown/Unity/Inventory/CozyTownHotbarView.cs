using System;
using CozyTown.Runtime.Inventory;
using CozyTown.Unity.Hud;
using UnityEngine;

namespace CozyTown.Unity.Inventory
{
    public sealed class CozyTownHotbarView : MonoBehaviour
    {
        public const int SlotCount = 5;

        [SerializeField] private CozyTownInventorySlotView[] slots =
            Array.Empty<CozyTownInventorySlotView>();
        [SerializeField] private CozyTownUiIconCatalog iconCatalog;

        public int SelectedIndex { get; private set; }

        public void ConfigureUi(
            CozyTownInventorySlotView[] targetSlots,
            CozyTownUiIconCatalog targetIconCatalog)
        {
            if (targetSlots == null)
            {
                throw new ArgumentNullException(nameof(targetSlots));
            }
            if (targetSlots.Length != SlotCount)
            {
                throw new ArgumentException(
                    $"Hotbar requires exactly {SlotCount} slots.",
                    nameof(targetSlots));
            }
            for (var index = 0; index < targetSlots.Length; index++)
            {
                if (targetSlots[index] == null)
                {
                    throw new ArgumentException(
                        "Hotbar slots must not contain null entries.",
                        nameof(targetSlots));
                }
            }

            slots = (CozyTownInventorySlotView[])targetSlots.Clone();
            iconCatalog = targetIconCatalog != null
                ? targetIconCatalog
                : throw new ArgumentNullException(nameof(targetIconCatalog));
        }

        public void Render(InventoryProjection projection, int selectedIndex)
        {
            if (projection == null)
            {
                throw new ArgumentNullException(nameof(projection));
            }
            if (selectedIndex < 0 || selectedIndex >= SlotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(selectedIndex));
            }

            SelectedIndex = selectedIndex;
            for (var index = 0; index < SlotCount; index++)
            {
                InventorySlotProjection slot = index < projection.Slots.Count
                    ? projection.Slots[index]
                    : new InventorySlotProjection(string.Empty, string.Empty, 0);
                slots[index].Render(
                    slot,
                    iconCatalog.GetItemSprite(slot.ItemId),
                    index == selectedIndex);
            }
        }
    }
}
