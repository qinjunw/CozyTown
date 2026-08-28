using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Farming;
using CozyTown.Unity.Hud;
using UnityEngine;

namespace CozyTown.Unity.Farm
{
    public sealed class CozyTownFarmDebugView : CozyTownModalDebugViewBase
    {
        private Vector2 _scrollPosition;

        public event Action<string, string> PlantRequested;
        public event Action<string> WaterRequested;
        public event Action<string> HarvestRequested;
        public FarmViewState State { get; private set; }

        public void Show(FarmViewState state, string feedback)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            ShowBase(feedback);
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

        private void OnGUI()
        {
            if (!BeginPanel("Farm") || State == null)
            {
                return;
            }

            _scrollPosition = GUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.ExpandHeight(true));
            foreach (var plot in State.Plots)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(
                    BuildPlotStatus(plot),
                    LabelStyle,
                    GUILayout.ExpandWidth(true));
                DrawActions(plot);
                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();
            EndPanel();
        }

        private void DrawActions(FarmPlotView plot)
        {
            if (plot.Status == FarmPlotStatus.Empty)
            {
                foreach (var seed in State.SeedOptions)
                {
                    if (GUILayout.Button(
                            $"Plant {seed.DisplayName} ({seed.OwnedQuantity})",
                            ButtonStyle,
                            GUILayout.ExpandWidth(true)))
                    {
                        RequestPlant(plot.PlotId, seed.SeedItemId);
                    }
                }
            }

            if (plot.Status == FarmPlotStatus.Growing
                && GUILayout.Button("Water", ButtonStyle, GUILayout.ExpandWidth(true)))
            {
                RequestWater(plot.PlotId);
            }

            if (plot.Status == FarmPlotStatus.Ready
                && GUILayout.Button("Harvest", ButtonStyle, GUILayout.ExpandWidth(true)))
            {
                RequestHarvest(plot.PlotId);
            }
        }

        private static string BuildPlotStatus(FarmPlotView plot)
        {
            if (plot.Status == FarmPlotStatus.Empty)
            {
                return $"{plot.PlotId} — Empty";
            }

            var watered = plot.WateredToday ? "Watered" : "Needs water";
            return $"{plot.PlotId} — {plot.CropDisplayName} — "
                + $"{plot.Status} — {plot.GrowthProgressDays}/{plot.GrowthDays} days — {watered}";
        }
    }
}
