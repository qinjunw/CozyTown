using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Farming;
using CozyTown.Unity.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Farm
{
    public sealed class CozyTownFarmDebugView : CozyTownModalDebugViewBase
    {
        private const int MaximumSeedButtons = 3;
        private const int WaterButtonIndex = 3;
        private const int HarvestButtonIndex = 4;

        [SerializeField] private GameObject panel;
        [SerializeField] private Text feedbackText;
        [SerializeField] private CozyTownUiListRow[] rows = Array.Empty<CozyTownUiListRow>();
        [SerializeField] private Button closeButton;
        [SerializeField] private CozyTownUiIconCatalog iconCatalog;

        public event Action<string, string> PlantRequested;
        public event Action<string> WaterRequested;
        public event Action<string> HarvestRequested;
        public FarmViewState State { get; private set; }

        public void ConfigureUi(
            GameObject targetPanel,
            Text targetFeedbackText,
            CozyTownUiListRow[] targetRows,
            Button targetCloseButton,
            CozyTownUiIconCatalog targetIconCatalog)
        {
            if (targetPanel == null)
            {
                throw new ArgumentNullException(nameof(targetPanel));
            }

            if (targetFeedbackText == null)
            {
                throw new ArgumentNullException(nameof(targetFeedbackText));
            }

            if (targetRows == null)
            {
                throw new ArgumentNullException(nameof(targetRows));
            }

            if (Array.Exists(targetRows, row => row == null))
            {
                throw new ArgumentException("Farm UI rows must not contain null entries.", nameof(targetRows));
            }

            if (targetCloseButton == null)
            {
                throw new ArgumentNullException(nameof(targetCloseButton));
            }

            if (targetIconCatalog == null)
            {
                throw new ArgumentNullException(nameof(targetIconCatalog));
            }

            RemoveCloseListener();
            panel = targetPanel;
            feedbackText = targetFeedbackText;
            rows = (CozyTownUiListRow[])targetRows.Clone();
            closeButton = targetCloseButton;
            iconCatalog = targetIconCatalog;
            BindCloseListener();
            RefreshUi();
        }

        public void Show(FarmViewState state, string feedback)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            ShowBase(feedback);
            RefreshUi();
        }

        public new void Hide()
        {
            base.Hide();
            RefreshUi();
        }

        public void RequestPlant(string plotId, string seedId)
        {
            if (IsVisible)
            {
                PlantRequested?.Invoke(plotId, seedId);
            }
        }

        public void RequestWater(string plotId)
        {
            if (IsVisible)
            {
                WaterRequested?.Invoke(plotId);
            }
        }

        public void RequestHarvest(string plotId)
        {
            if (IsVisible)
            {
                HarvestRequested?.Invoke(plotId);
            }
        }

        private void OnEnable()
        {
            BindCloseListener();
            RefreshUi();
        }

        private void OnDisable()
        {
            RemoveCloseListener();
            ClearRows();
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        private void RefreshUi()
        {
            ClearRows();

            if (panel != null)
            {
                panel.SetActive(IsVisible);
            }

            if (feedbackText != null)
            {
                feedbackText.text = Feedback;
            }

            if (!IsVisible || State == null || rows == null || iconCatalog == null)
            {
                return;
            }

            int visibleRowCount = Math.Min(rows.Length, State.Plots.Count);
            for (var index = 0; index < visibleRowCount; index++)
            {
                RefreshRow(rows[index], State.Plots[index]);
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

        private void RefreshRow(CozyTownUiListRow row, FarmPlotView plot)
        {
            row.SetContent(BuildPlotStatus(plot), ResolvePlotIcon(plot));

            if (plot.Status == FarmPlotStatus.Empty)
            {
                int seedButtonCount = Math.Min(MaximumSeedButtons, State.SeedOptions.Count);
                for (var index = 0; index < seedButtonCount; index++)
                {
                    FarmSeedOption seed = State.SeedOptions[index];
                    string plotId = plot.PlotId;
                    string seedItemId = seed.SeedItemId;
                    row.SetButton(
                        index,
                        $"Plant {seed.DisplayName} ({seed.OwnedQuantity})",
                        seed.OwnedQuantity > 0,
                        () => RequestPlant(plotId, seedItemId));
                }

                return;
            }

            if (plot.Status == FarmPlotStatus.Growing)
            {
                string plotId = plot.PlotId;
                row.SetButton(
                    WaterButtonIndex,
                    "Water",
                    !plot.WateredToday,
                    () => RequestWater(plotId));
                return;
            }

            if (plot.Status == FarmPlotStatus.Ready)
            {
                string plotId = plot.PlotId;
                row.SetButton(
                    HarvestButtonIndex,
                    "Harvest",
                    true,
                    () => RequestHarvest(plotId));
            }
        }

        private Sprite ResolvePlotIcon(FarmPlotView plot)
        {
            if (plot.Status == FarmPlotStatus.Empty)
            {
                return State.SeedOptions.Count > 0
                    ? iconCatalog.GetItemSprite(State.SeedOptions[0].SeedItemId)
                    : null;
            }

            foreach (FarmSeedOption seed in State.SeedOptions)
            {
                if (string.Equals(seed.CropId, plot.CropId, StringComparison.Ordinal))
                {
                    return iconCatalog.GetItemSprite(seed.SeedItemId);
                }
            }

            return null;
        }

        private void BindCloseListener()
        {
            if (closeButton == null)
            {
                return;
            }

            closeButton.onClick.RemoveListener(RequestClose);
            closeButton.onClick.AddListener(RequestClose);
        }

        private void RemoveCloseListener()
        {
            closeButton?.onClick.RemoveListener(RequestClose);
        }

        private static string BuildPlotStatus(FarmPlotView plot)
        {
            if (plot.Status == FarmPlotStatus.Empty)
            {
                return $"{plot.PlotId} — Empty";
            }

            string watered = plot.WateredToday ? "Watered" : "Needs water";
            return $"{plot.PlotId} — {plot.CropDisplayName} — "
                + $"{plot.Status} — {plot.GrowthProgressDays}/{plot.GrowthDays} days — {watered}";
        }
    }
}
