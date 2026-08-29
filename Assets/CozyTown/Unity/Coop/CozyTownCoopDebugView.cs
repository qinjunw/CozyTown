using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Coop
{
    public sealed class CozyTownCoopDebugView : CozyTownModalDebugViewBase
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text feedbackText;
        [SerializeField] private CozyTownUiListRow[] rows = Array.Empty<CozyTownUiListRow>();
        [SerializeField] private Button closeButton;
        [SerializeField] private CozyTownUiIconCatalog iconCatalog;

        private bool _closeListenerAttached;

        public event Action<string> FeedRequested;
        public event Action<string> CollectRequested;
        public LivestockViewState State { get; private set; }

        public void ConfigureUi(
            GameObject configuredPanel,
            Text configuredFeedbackText,
            CozyTownUiListRow[] configuredRows,
            Button configuredCloseButton,
            CozyTownUiIconCatalog configuredIconCatalog)
        {
            DetachCloseListener();
            panel = configuredPanel != null
                ? configuredPanel
                : throw new ArgumentNullException(nameof(configuredPanel));
            feedbackText = configuredFeedbackText != null
                ? configuredFeedbackText
                : throw new ArgumentNullException(nameof(configuredFeedbackText));
            closeButton = configuredCloseButton != null
                ? configuredCloseButton
                : throw new ArgumentNullException(nameof(configuredCloseButton));
            iconCatalog = configuredIconCatalog != null
                ? configuredIconCatalog
                : throw new ArgumentNullException(nameof(configuredIconCatalog));
            rows = CopyRows(configuredRows, minimumButtonCount: 2);

            panel.SetActive(IsVisible);
            AttachCloseListener();
            RefreshUi();
        }

        public void Show(LivestockViewState state, string feedback)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            ShowBase(feedback);
            RefreshUi();
        }

        public new void Hide()
        {
            base.Hide();
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        public void RequestFeed(string id)
        {
            if (IsVisible)
            {
                FeedRequested?.Invoke(id);
            }
        }

        public void RequestCollect(string id)
        {
            if (IsVisible)
            {
                CollectRequested?.Invoke(id);
            }
        }

        private void OnEnable()
        {
            AttachCloseListener();
        }

        private void OnDisable()
        {
            DetachCloseListener();
        }

        private void RefreshUi()
        {
            if (panel == null
                || feedbackText == null
                || iconCatalog == null
                || State == null)
            {
                return;
            }

            panel.SetActive(IsVisible);
            feedbackText.text = Feedback;
            var visibleCount = Mathf.Min(State.Animals.Count, rows.Length);
            for (var index = 0; index < visibleCount; index++)
            {
                var animal = State.Animals[index];
                var animalId = animal.AnimalId;
                var iconId = animal.ProductReady
                    ? animal.ProductItemId
                    : animal.FeedItemId;
                var row = rows[index];
                row.SetContent(
                    $"{animal.AnimalId}  Feed: {animal.OwnedFeedQuantity}  Product: {animal.ProductQuantity}",
                    iconCatalog.GetItemSprite(iconId));
                row.SetButton(
                    0,
                    "Feed",
                    !animal.FedToday && animal.OwnedFeedQuantity > 0,
                    () => RequestFeed(animalId));
                row.SetButton(
                    1,
                    "Collect",
                    animal.ProductReady,
                    () => RequestCollect(animalId));
                row.HideUnusedButtons(2);
            }

            ClearRowsFrom(visibleCount);
        }

        private void ClearRowsFrom(int firstUnusedIndex)
        {
            for (var index = firstUnusedIndex; index < rows.Length; index++)
            {
                rows[index].Clear();
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
            if (!_closeListenerAttached)
            {
                return;
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(RequestClose);
            }

            _closeListenerAttached = false;
        }

        private static CozyTownUiListRow[] CopyRows(
            CozyTownUiListRow[] configuredRows,
            int minimumButtonCount)
        {
            if (configuredRows == null)
            {
                throw new ArgumentNullException(nameof(configuredRows));
            }

            if (configuredRows.Length == 0
                || Array.Exists(
                    configuredRows,
                    row => row == null || row.Buttons.Count < minimumButtonCount))
            {
                throw new ArgumentException("Coop UI requires configured two-button rows.", nameof(configuredRows));
            }

            return (CozyTownUiListRow[])configuredRows.Clone();
        }
    }
}
