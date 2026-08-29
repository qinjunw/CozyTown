using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Shop
{
    public sealed class CozyTownShopDebugView : MonoBehaviour, ICozyTownShopDebugView
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text balanceText;
        [SerializeField] private Text feedbackText;
        [SerializeField] private CozyTownUiListRow[] rows = Array.Empty<CozyTownUiListRow>();
        [SerializeField] private Button closeButton;
        [SerializeField] private CozyTownUiIconCatalog iconCatalog;

        private bool _closeListenerAttached;

        public event Action<string> BuyRequested;

        public event Action<string> SellRequested;

        public event Action CloseRequested;

        public bool IsVisible { get; private set; }

        public ShopViewState State { get; private set; }

        public string Feedback { get; private set; } = string.Empty;

        public void ConfigureUi(
            GameObject configuredPanel,
            Text configuredBalanceText,
            Text configuredFeedbackText,
            CozyTownUiListRow[] configuredRows,
            Button configuredCloseButton,
            CozyTownUiIconCatalog configuredIconCatalog)
        {
            DetachCloseListener();
            panel = configuredPanel != null
                ? configuredPanel
                : throw new ArgumentNullException(nameof(configuredPanel));
            balanceText = configuredBalanceText != null
                ? configuredBalanceText
                : throw new ArgumentNullException(nameof(configuredBalanceText));
            feedbackText = configuredFeedbackText != null
                ? configuredFeedbackText
                : throw new ArgumentNullException(nameof(configuredFeedbackText));
            closeButton = configuredCloseButton != null
                ? configuredCloseButton
                : throw new ArgumentNullException(nameof(configuredCloseButton));
            iconCatalog = configuredIconCatalog != null
                ? configuredIconCatalog
                : throw new ArgumentNullException(nameof(configuredIconCatalog));

            if (configuredRows == null)
            {
                throw new ArgumentNullException(nameof(configuredRows));
            }

            if (configuredRows.Length == 0
                || Array.Exists(configuredRows, row => row == null))
            {
                throw new ArgumentException(
                    "Shop UI requires at least one configured row.",
                    nameof(configuredRows));
            }

            rows = (CozyTownUiListRow[])configuredRows.Clone();
            panel.SetActive(IsVisible);
            AttachCloseListener();
            RefreshUi();
        }

        public void Show(ShopViewState state, string feedback)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Feedback = feedback ?? string.Empty;
            IsVisible = true;
            RefreshUi();
        }

        public void Hide()
        {
            IsVisible = false;
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        public void RequestBuy(string itemId)
        {
            if (IsVisible && !string.IsNullOrWhiteSpace(itemId))
            {
                BuyRequested?.Invoke(itemId);
            }
        }

        public void RequestSell(string itemId)
        {
            if (IsVisible && !string.IsNullOrWhiteSpace(itemId))
            {
                SellRequested?.Invoke(itemId);
            }
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
            AttachCloseListener();
            RefreshUi();
        }

        private void OnDisable()
        {
            DetachCloseListener();
            ClearRows();
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        private void RefreshUi()
        {
            if (panel == null
                || balanceText == null
                || feedbackText == null
                || iconCatalog == null
                || State == null)
            {
                return;
            }

            panel.SetActive(IsVisible);
            balanceText.text = $"Town Shop — Coins: {State.Balance}";
            feedbackText.text = Feedback;

            var visibleCount = Mathf.Min(State.Items.Count, rows.Length);
            for (var index = 0; index < visibleCount; index++)
            {
                var item = State.Items[index];
                var stableItemId = item.ItemId;
                var row = rows[index];
                row.SetContent(
                    $"{item.DisplayName}  Owned: {item.OwnedQuantity}",
                    iconCatalog.GetItemSprite(stableItemId));
                row.SetButton(
                    0,
                    $"Buy 1 ({item.BuyPrice})",
                    item.BuyPrice > 0 && State.Balance >= item.BuyPrice,
                    () => RequestBuy(stableItemId));
                row.SetButton(
                    1,
                    $"Sell 1 ({item.SellPrice})",
                    item.SellPrice > 0 && item.OwnedQuantity > 0,
                    () => RequestSell(stableItemId));
                row.HideUnusedButtons(2);
            }

            for (var index = visibleCount; index < rows.Length; index++)
            {
                rows[index].Clear();
            }
        }

        private void ClearRows()
        {
            if (rows == null)
            {
                return;
            }

            foreach (var row in rows)
            {
                row?.Clear();
            }
        }

        private void AttachCloseListener()
        {
            if (_closeListenerAttached || closeButton == null || !isActiveAndEnabled)
            {
                return;
            }

            closeButton.onClick.AddListener(RequestClose);
            _closeListenerAttached = true;
        }

        private void DetachCloseListener()
        {
            if (!_closeListenerAttached || closeButton == null)
            {
                return;
            }

            closeButton.onClick.RemoveListener(RequestClose);
            _closeListenerAttached = false;
        }
    }
}
