using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Pond
{
    public sealed class CozyTownPondDebugView : CozyTownModalDebugViewBase
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text feedbackText;
        [SerializeField] private CozyTownUiListRow[] rows = Array.Empty<CozyTownUiListRow>();
        [SerializeField] private Button closeButton;
        [SerializeField] private Button castButton;
        [SerializeField] private CozyTownUiIconCatalog iconCatalog;

        private bool _listenersAttached;

        public event Action CatchRequested;
        public FishingViewState State { get; private set; }

        public void ConfigureUi(
            GameObject configuredPanel,
            Text configuredFeedbackText,
            CozyTownUiListRow[] configuredRows,
            Button configuredCloseButton,
            Button configuredCastButton,
            CozyTownUiIconCatalog configuredIconCatalog)
        {
            DetachListeners();
            panel = configuredPanel != null
                ? configuredPanel
                : throw new ArgumentNullException(nameof(configuredPanel));
            feedbackText = configuredFeedbackText != null
                ? configuredFeedbackText
                : throw new ArgumentNullException(nameof(configuredFeedbackText));
            closeButton = configuredCloseButton != null
                ? configuredCloseButton
                : throw new ArgumentNullException(nameof(configuredCloseButton));
            castButton = configuredCastButton != null
                ? configuredCastButton
                : throw new ArgumentNullException(nameof(configuredCastButton));
            iconCatalog = configuredIconCatalog != null
                ? configuredIconCatalog
                : throw new ArgumentNullException(nameof(configuredIconCatalog));
            rows = CopyRows(configuredRows);

            panel.SetActive(IsVisible);
            AttachListeners();
            RefreshUi();
        }

        public void Show(FishingViewState state, string feedback)
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

        public void RequestCatch()
        {
            if (IsVisible)
            {
                CatchRequested?.Invoke();
            }
        }

        private void OnEnable()
        {
            AttachListeners();
        }

        private void OnDisable()
        {
            DetachListeners();
        }

        private void RefreshUi()
        {
            if (panel == null
                || feedbackText == null
                || castButton == null
                || iconCatalog == null
                || State == null)
            {
                return;
            }

            panel.SetActive(IsVisible);
            feedbackText.text = Feedback;
            castButton.interactable = true;
            var visibleCount = Mathf.Min(State.Entries.Count, rows.Length);
            for (var index = 0; index < visibleCount; index++)
            {
                var fish = State.Entries[index];
                rows[index].SetContent(
                    $"{fish.DisplayName}  Owned: {fish.OwnedQuantity}",
                    iconCatalog.GetItemSprite(fish.ItemId));
                rows[index].HideUnusedButtons(0);
            }

            for (var index = visibleCount; index < rows.Length; index++)
            {
                rows[index].Clear();
            }
        }

        private void AttachListeners()
        {
            if (_listenersAttached
                || closeButton == null
                || castButton == null
                || !isActiveAndEnabled)
            {
                return;
            }

            closeButton.onClick.AddListener(RequestClose);
            castButton.onClick.AddListener(RequestCatch);
            _listenersAttached = true;
        }

        private void DetachListeners()
        {
            if (!_listenersAttached)
            {
                return;
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(RequestClose);
            }

            if (castButton != null)
            {
                castButton.onClick.RemoveListener(RequestCatch);
            }

            _listenersAttached = false;
        }

        private static CozyTownUiListRow[] CopyRows(CozyTownUiListRow[] configuredRows)
        {
            if (configuredRows == null)
            {
                throw new ArgumentNullException(nameof(configuredRows));
            }

            if (configuredRows.Length == 0 || Array.Exists(configuredRows, row => row == null))
            {
                throw new ArgumentException("Pond UI requires configured rows.", nameof(configuredRows));
            }

            return (CozyTownUiListRow[])configuredRows.Clone();
        }
    }
}
