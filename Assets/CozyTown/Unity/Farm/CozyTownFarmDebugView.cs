using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Farming;
using CozyTown.Unity.Hud;
using UnityEngine;

namespace CozyTown.Unity.Farm
{
    public sealed class CozyTownFarmDebugView : CozyTownModalDebugViewBase
    {
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
            foreach (var plot in State.Plots)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{plot.PlotId}: {plot.Status} {plot.CropDisplayName} {plot.GrowthProgressDays}/{plot.GrowthDays}",
                    LabelStyle);
                DrawActions(plot);
                GUILayout.EndHorizontal();
            }
            EndPanel();
        }

        private void DrawActions(FarmPlotView plot)
        {
            if (plot.Status == FarmPlotStatus.Empty)
            {
                foreach (var seed in State.SeedOptions)
                {
                    if (GUILayout.Button($"Plant {seed.DisplayName} ({seed.OwnedQuantity})", ButtonStyle))
                    {
                        RequestPlant(plot.PlotId, seed.SeedItemId);
                    }
                }
            }
            if (plot.Status == FarmPlotStatus.Growing && GUILayout.Button("Water", ButtonStyle))
            {
                RequestWater(plot.PlotId);
            }
            if (plot.Status == FarmPlotStatus.Ready && GUILayout.Button("Harvest", ButtonStyle))
            {
                RequestHarvest(plot.PlotId);
            }
        }
    }
}
