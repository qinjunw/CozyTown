using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Farming;
using UnityEngine;

namespace CozyTown.Unity.Farm
{
    public sealed class CozyTownFarmWorldView : MonoBehaviour
    {
        private const int PlotCount = 6;

        [SerializeField] private SpriteRenderer[] soilRenderers = new SpriteRenderer[PlotCount];
        [SerializeField] private SpriteRenderer[] cropRenderers = new SpriteRenderer[PlotCount];
        [SerializeField] private Sprite drySoil;
        [SerializeField] private Sprite wateredSoil;
        [SerializeField] private Sprite[] potatoStages = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] carrotStages = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] tomatoStages = Array.Empty<Sprite>();

        public void Configure(
            SpriteRenderer[] plotSoilRenderers,
            SpriteRenderer[] plotCropRenderers,
            Sprite drySoilSprite,
            Sprite wateredSoilSprite,
            Sprite[] potatoCropStages,
            Sprite[] carrotCropStages,
            Sprite[] tomatoCropStages)
        {
            soilRenderers = CopyRenderers(plotSoilRenderers, nameof(plotSoilRenderers));
            cropRenderers = CopyRenderers(plotCropRenderers, nameof(plotCropRenderers));
            drySoil = drySoilSprite ?? throw new ArgumentNullException(nameof(drySoilSprite));
            wateredSoil = wateredSoilSprite ?? throw new ArgumentNullException(nameof(wateredSoilSprite));
            potatoStages = CopySprites(potatoCropStages, 3, nameof(potatoCropStages));
            carrotStages = CopySprites(carrotCropStages, 4, nameof(carrotCropStages));
            tomatoStages = CopySprites(tomatoCropStages, 5, nameof(tomatoCropStages));
        }

        public void Show(FarmViewState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (!HasValidConfiguration() || state.Plots.Count != PlotCount)
            {
                throw new InvalidOperationException(
                    $"Farm world view requires {PlotCount} configured plots and state entries.");
            }

            for (var index = 0; index < PlotCount; index++)
            {
                var plot = state.Plots[index];
                soilRenderers[index].sprite = plot.WateredToday ? wateredSoil : drySoil;
                cropRenderers[index].sprite = plot.Status == FarmPlotStatus.Empty
                    ? null
                    : SelectCropSprite(plot);
            }
        }

        private Sprite SelectCropSprite(FarmPlotView plot)
        {
            Sprite[] stages;
            switch (plot.CropId)
            {
                case DefaultMvpIds.Crops.Potato:
                    stages = potatoStages;
                    break;
                case DefaultMvpIds.Crops.Carrot:
                    stages = carrotStages;
                    break;
                case DefaultMvpIds.Crops.Tomato:
                    stages = tomatoStages;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Farm world view cannot render crop '{plot.CropId}'.");
            }

            int stageIndex = plot.Status == FarmPlotStatus.Ready
                ? stages.Length - 1
                : Mathf.Clamp(plot.GrowthProgressDays, 0, stages.Length - 2);
            return stages[stageIndex];
        }

        private bool HasValidConfiguration()
        {
            return HasRenderers(soilRenderers)
                && HasRenderers(cropRenderers)
                && drySoil != null
                && wateredSoil != null
                && HasSprites(potatoStages, 3)
                && HasSprites(carrotStages, 4)
                && HasSprites(tomatoStages, 5);
        }

        private static SpriteRenderer[] CopyRenderers(SpriteRenderer[] source, string parameterName)
        {
            if (!HasRenderers(source))
            {
                throw new ArgumentException(
                    $"Expected {PlotCount} non-null SpriteRenderers.",
                    parameterName);
            }

            return (SpriteRenderer[])source.Clone();
        }

        private static Sprite[] CopySprites(Sprite[] source, int expectedLength, string parameterName)
        {
            if (!HasSprites(source, expectedLength))
            {
                throw new ArgumentException(
                    $"Expected {expectedLength} non-null Sprites.",
                    parameterName);
            }

            return (Sprite[])source.Clone();
        }

        private static bool HasRenderers(SpriteRenderer[] renderers)
        {
            if (renderers == null || renderers.Length != PlotCount)
            {
                return false;
            }

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasSprites(Sprite[] sprites, int expectedLength)
        {
            if (sprites == null || sprites.Length != expectedLength)
            {
                return false;
            }

            foreach (var sprite in sprites)
            {
                if (sprite == null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
