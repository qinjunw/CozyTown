using System;
using CozyTown.Runtime.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Inventory
{
    public sealed class CozyTownInventorySlotView : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private Text quantityText;
        [SerializeField] private GameObject selectionMarker;

        public string ItemId { get; private set; } = string.Empty;

        public bool IsSelected { get; private set; }

        public Image Icon => icon;

        public Text QuantityText => quantityText;

        public void ConfigureUi(
            Image targetIcon,
            Text targetQuantityText,
            GameObject targetSelectionMarker)
        {
            icon = targetIcon != null
                ? targetIcon
                : throw new ArgumentNullException(nameof(targetIcon));
            quantityText = targetQuantityText != null
                ? targetQuantityText
                : throw new ArgumentNullException(nameof(targetQuantityText));
            selectionMarker = targetSelectionMarker != null
                ? targetSelectionMarker
                : throw new ArgumentNullException(nameof(targetSelectionMarker));
        }

        public void Render(InventorySlotProjection slot, Sprite itemSprite, bool isSelected)
        {
            ItemId = slot.ItemId ?? string.Empty;
            IsSelected = isSelected;

            bool hasItem = !slot.IsEmpty;
            icon.sprite = hasItem ? itemSprite : null;
            icon.enabled = hasItem && itemSprite != null;
            quantityText.text = hasItem ? slot.Quantity.ToString() : string.Empty;
            selectionMarker.SetActive(isSelected);
        }
    }
}
