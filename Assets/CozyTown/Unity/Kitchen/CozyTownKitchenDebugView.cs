using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Kitchen
{
    public sealed class CozyTownKitchenDebugView : CozyTownModalDebugViewBase
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text feedbackText;
        [SerializeField] private CozyTownUiListRow[] rows = Array.Empty<CozyTownUiListRow>();
        [SerializeField] private Button closeButton;
        [SerializeField] private CozyTownUiIconCatalog iconCatalog;

        private bool _closeListenerAttached;

        public event Action<string> CookRequested;
        public CookingViewState State { get; private set; }

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
            rows = CopyRows(configuredRows);

            panel.SetActive(IsVisible);
            AttachCloseListener();
            RefreshUi();
        }

        public void Show(CookingViewState state, string feedback)
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

        public void RequestCook(string recipeId)
        {
            if (IsVisible)
            {
                CookRequested?.Invoke(recipeId);
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
                || feedbackText == null
                || iconCatalog == null
                || State == null)
            {
                return;
            }

            panel.SetActive(IsVisible);
            feedbackText.text = Feedback;
            var visibleCount = Mathf.Min(State.Recipes.Count, rows.Length);
            for (var index = 0; index < visibleCount; index++)
            {
                var recipe = State.Recipes[index];
                var recipeId = recipe.RecipeId;
                var row = rows[index];
                row.SetContent(
                    $"{recipe.OutputDisplayName} x{recipe.OutputQuantity}",
                    iconCatalog.GetItemSprite(recipe.OutputItemId));
                row.SetButton(
                    0,
                    "Cook",
                    recipe.HasIngredients,
                    () => RequestCook(recipeId));
                row.HideUnusedButtons(1);
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

        private static CozyTownUiListRow[] CopyRows(CozyTownUiListRow[] configuredRows)
        {
            if (configuredRows == null)
            {
                throw new ArgumentNullException(nameof(configuredRows));
            }

            if (configuredRows.Length == 0
                || Array.Exists(
                    configuredRows,
                    row => row == null || row.Buttons.Count < 1))
            {
                throw new ArgumentException("Kitchen UI requires configured one-button rows.", nameof(configuredRows));
            }

            return (CozyTownUiListRow[])configuredRows.Clone();
        }
    }
}
