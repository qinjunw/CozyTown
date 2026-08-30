using System;
using CozyTown.Runtime.Inventory;
using CozyTown.Unity.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Inventory
{
    public sealed class CozyTownBackpackView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private CozyTownInventorySlotView[] slots =
            Array.Empty<CozyTownInventorySlotView>();
        [SerializeField] private Button closeButton;
        [SerializeField] private CozyTownUiIconCatalog iconCatalog;

        public event Action CloseRequested;

        public bool IsVisible { get; private set; }

        public void ConfigureUi(
            GameObject targetPanel,
            CozyTownInventorySlotView[] targetSlots,
            Button targetCloseButton,
            CozyTownUiIconCatalog targetIconCatalog)
        {
            panel = targetPanel != null
                ? targetPanel
                : throw new ArgumentNullException(nameof(targetPanel));
            slots = targetSlots != null
                ? (CozyTownInventorySlotView[])targetSlots.Clone()
                : throw new ArgumentNullException(nameof(targetSlots));
            closeButton = targetCloseButton != null
                ? targetCloseButton
                : throw new ArgumentNullException(nameof(targetCloseButton));
            iconCatalog = targetIconCatalog != null
                ? targetIconCatalog
                : throw new ArgumentNullException(nameof(targetIconCatalog));
            ValidateSlots(slots);

            BindButton();
            RefreshVisibility();
        }

        public void Show(InventoryProjection projection)
        {
            if (projection == null)
            {
                throw new ArgumentNullException(nameof(projection));
            }
            if (projection.CapacitySlots != slots.Length)
            {
                throw new ArgumentException(
                    "Backpack slot count must match the inventory projection capacity.",
                    nameof(projection));
            }

            for (var index = 0; index < slots.Length; index++)
            {
                InventorySlotProjection slot = projection.Slots[index];
                slots[index].Render(slot, iconCatalog.GetItemSprite(slot.ItemId), false);
            }

            IsVisible = true;
            RefreshVisibility();
        }

        public void Hide()
        {
            IsVisible = false;
            RefreshVisibility();
        }

        public void RequestClose()
        {
            if (IsVisible)
            {
                CloseRequested?.Invoke();
            }
        }

        private void OnEnable()
        {
            BindButton();
            RefreshVisibility();
        }

        private void OnDisable()
        {
            RemoveButtonListener();
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        private void BindButton()
        {
            if (closeButton == null)
            {
                return;
            }

            closeButton.onClick.RemoveListener(RequestClose);
            closeButton.onClick.AddListener(RequestClose);
        }

        private void RemoveButtonListener()
        {
            closeButton?.onClick.RemoveListener(RequestClose);
        }

        private void RefreshVisibility()
        {
            if (panel != null)
            {
                panel.SetActive(IsVisible);
            }
        }

        private static void ValidateSlots(CozyTownInventorySlotView[] configuredSlots)
        {
            for (var index = 0; index < configuredSlots.Length; index++)
            {
                if (configuredSlots[index] == null)
                {
                    throw new ArgumentException("Backpack slots must not contain null entries.");
                }
            }
        }
    }
}
