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
        [SerializeField] private Button buyTab;
        [SerializeField] private Button sellTab;
        [SerializeField] private Text emptyStateText;
        [SerializeField] private ScrollRect itemList;
        [SerializeField] private Text listPositionText;
        [SerializeField] private CozyTownUiIconCatalog iconCatalog;

        private bool _closeListenerAttached;
        private bool _selling;
        private bool _resetScroll;
        private int _visibleRowCount;

        public event Action<string> BuyRequested;

        public event Action<string> SellRequested;

        public event Action CloseRequested;

        public bool IsVisible { get; private set; }

        public ShopTradingViewState State { get; private set; }

        public string Feedback { get; private set; } = string.Empty;

        public void ConfigureUi(
            GameObject configuredPanel,
            Text configuredBalanceText,
            Text configuredFeedbackText,
            CozyTownUiListRow[] configuredRows,
            Button configuredCloseButton,
            CozyTownUiIconCatalog configuredIconCatalog,
            Button configuredBuyTab,
            Button configuredSellTab,
            Text configuredEmptyStateText,
            ScrollRect configuredItemList,
            Text configuredListPositionText)
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
            buyTab = configuredBuyTab != null
                ? configuredBuyTab : throw new ArgumentNullException(nameof(configuredBuyTab));
            sellTab = configuredSellTab != null
                ? configuredSellTab : throw new ArgumentNullException(nameof(configuredSellTab));
            emptyStateText = configuredEmptyStateText != null
                ? configuredEmptyStateText : throw new ArgumentNullException(nameof(configuredEmptyStateText));
            itemList = configuredItemList != null
                ? configuredItemList : throw new ArgumentNullException(nameof(configuredItemList));
            listPositionText = configuredListPositionText != null
                ? configuredListPositionText : throw new ArgumentNullException(nameof(configuredListPositionText));

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

        public void Show(ShopTradingViewState state, string feedback)
        {
            if (!IsVisible)
            {
                _selling = false;
                _resetScroll = true;
            }
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
            balanceText.text = $"Shop · You: {State.CharacterBalance}c · Shop: {State.ShopBalance}c";
            feedbackText.text = Feedback;
            buyTab.interactable = _selling;
            sellTab.interactable = !_selling;

            var rowIndex = 0;
            var items = _selling ? State.SaleItems : State.PurchaseItems;
            emptyStateText.gameObject.SetActive(items.Count == 0);
            emptyStateText.text = _selling
                ? "No items to sell. Bring crops, eggs, fish or meals."
                : "No items in stock. Supplies refresh at 05:00.";
            foreach (ShopTradingLineItem item in items)
            {
                if (rowIndex >= rows.Length)
                {
                    break;
                }

                var stableItemId = item.ItemId;
                var row = rows[rowIndex++];
                row.Clear();
                string availability = DescribeAvailability(item);
                row.SetContent(
                    $"{item.DisplayName}\n{(_selling ? "Owned" : "Stock")}: {item.Quantity}"
                        + (availability.Length == 0 ? string.Empty : "\n" + availability),
                    iconCatalog.GetItemSprite(stableItemId));
                row.SetButton(
                    0,
                    $"{(_selling ? "Sell" : "Buy")} 1 ({item.UnitPrice})",
                    item.Quantity > 0 && (_selling ? State.ShopBalance : State.CharacterBalance) >= item.UnitPrice,
                    () => { if (_selling) RequestSell(stableItemId); else RequestBuy(stableItemId); });
                row.HideUnusedButtons(1);
            }

            for (var index = rowIndex; index < rows.Length; index++)
            {
                rows[index].Clear();
            }
            _visibleRowCount = rowIndex;
            FitListToRows();
        }

        private string DescribeAvailability(ShopTradingLineItem item)
        {
            if (item.Quantity <= 0)
            {
                return _selling ? "None owned" : "Sold out · Restock at 05:00";
            }
            if ((_selling ? State.ShopBalance : State.CharacterBalance) < item.UnitPrice)
            {
                return _selling ? $"Shop needs {item.UnitPrice} coins" : $"Need {item.UnitPrice} coins";
            }
            return string.Empty;
        }

        private void FitListToRows()
        {
            float height = 0f;
            if (_visibleRowCount > 0)
            {
                var lastRow = (RectTransform)rows[_visibleRowCount - 1].transform;
                height = -lastRow.anchoredPosition.y + lastRow.rect.height;
            }
            float viewportHeight = itemList.viewport.rect.height;
            itemList.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(viewportHeight, height));
            itemList.StopMovement();
            var offset = itemList.content.anchoredPosition;
            offset.x = 0f;
            offset.y = _resetScroll ? 0f : Mathf.Clamp(offset.y, 0f, Mathf.Max(0f, height - viewportHeight));
            itemList.content.anchoredPosition = offset;
            _resetScroll = false;
            RefreshListPosition(Vector2.zero);
        }

        private void RefreshListPosition(Vector2 unused)
        {
            if (listPositionText == null || itemList == null) return;
            if (_visibleRowCount == 0 || itemList.content.rect.height <= itemList.viewport.rect.height)
            {
                listPositionText.text = $"{_visibleRowCount} items";
                return;
            }
            var firstRow = (RectTransform)rows[0].transform;
            float spacing = rows.Length > 1
                ? Mathf.Abs(((RectTransform)rows[1].transform).anchoredPosition.y - firstRow.anchoredPosition.y)
                : firstRow.rect.height;
            spacing = Mathf.Max(1f, spacing);
            float offset = itemList.content.anchoredPosition.y;
            int first = Mathf.Clamp(Mathf.FloorToInt(offset / spacing) + 1, 1, _visibleRowCount);
            int last = Mathf.Clamp(Mathf.CeilToInt((offset + itemList.viewport.rect.height) / spacing), first, _visibleRowCount);
            listPositionText.text = $"{first}–{last}/{_visibleRowCount} · Scroll";
        }

        private void SelectBuy() => SelectTab(false);

        private void SelectSell() => SelectTab(true);

        private void SelectTab(bool selling)
        {
            if (!IsVisible || _selling == selling) return;
            _selling = selling;
            _resetScroll = true;
            Feedback = string.Empty;
            RefreshUi();
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
            if (_closeListenerAttached || closeButton == null || buyTab == null
                || sellTab == null || itemList == null || !isActiveAndEnabled)
            {
                return;
            }

            closeButton.onClick.AddListener(RequestClose);
            buyTab.onClick.AddListener(SelectBuy);
            sellTab.onClick.AddListener(SelectSell);
            itemList.onValueChanged.AddListener(RefreshListPosition);
            _closeListenerAttached = true;
        }

        private void DetachCloseListener()
        {
            if (!_closeListenerAttached || closeButton == null)
            {
                return;
            }

            closeButton.onClick.RemoveListener(RequestClose);
            buyTab.onClick.RemoveListener(SelectBuy);
            sellTab.onClick.RemoveListener(SelectSell);
            itemList.onValueChanged.RemoveListener(RefreshListPosition);
            _closeListenerAttached = false;
        }
    }
}
