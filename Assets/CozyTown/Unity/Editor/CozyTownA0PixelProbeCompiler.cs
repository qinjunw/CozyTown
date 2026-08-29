using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownA0PixelProbeCompiler
    {
        private const int OutputSize = 16;
        private const int ContentSize = 14;
        private const int PreviewScale = 4;
        private const byte SourceAlphaThreshold = 16;
        private const float MinimumCoverage = 0.22f;

        private const string SourceRelativePath =
            "ArtSource/Generated/A0/item_crop_carrot_source.png";
        private const string OutputAssetPath =
            "Assets/CozyTown/Art/References/A0/a0_item_crop_carrot.png";
        private const string PreviewRelativePath =
            "ArtSource/Previews/A0/item_crop_carrot_4x.png";

        private static readonly Color32[] Palette =
        {
            new Color32(59, 31, 27, 255),
            new Color32(33, 78, 36, 255),
            new Color32(62, 123, 50, 255),
            new Color32(118, 185, 71, 255),
            new Color32(138, 59, 18, 255),
            new Color32(215, 91, 24, 255),
            new Color32(246, 139, 43, 255),
            new Color32(255, 192, 90, 255)
        };

        [MenuItem("CozyTown/Art/Build A0 Carrot Pixel Probe")]
        public static void Build()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve the Unity project root.");
            string sourcePath = Path.Combine(projectRoot, SourceRelativePath);
            string outputPath = Path.Combine(projectRoot, OutputAssetPath);
            string previewPath = Path.Combine(projectRoot, PreviewRelativePath);

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "The generated carrot source image is missing.",
                    sourcePath);
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(
                        source,
                        File.ReadAllBytes(sourcePath),
                        false))
                {
                    throw new InvalidDataException($"Could not decode source PNG: {sourcePath}");
                }

                Color32[] compiledPixels = Compile(source);
                WritePng(outputPath, OutputSize, OutputSize, compiledPixels);
                WritePng(
                    previewPath,
                    OutputSize * PreviewScale,
                    OutputSize * PreviewScale,
                    ScaleNearest(compiledPixels, OutputSize, PreviewScale));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            AssetDatabase.ImportAsset(
                OutputAssetPath,
                ImportAssetOptions.ForceSynchronousImport
                    | ImportAssetOptions.ForceUpdate);
            Debug.Log(
                $"Built A0 carrot pixel probe: {OutputAssetPath}; preview: {PreviewRelativePath}");
        }

        private static Color32[] Compile(Texture2D source)
        {
            Color32[] sourcePixels = source.GetPixels32();
            RectInt bounds = FindOpaqueBounds(sourcePixels, source.width, source.height);

            float scale = Math.Min(
                (float)ContentSize / bounds.width,
                (float)ContentSize / bounds.height);
            int targetWidth = Math.Max(1, Mathf.RoundToInt(bounds.width * scale));
            int targetHeight = Math.Max(1, Mathf.RoundToInt(bounds.height * scale));
            int offsetX = (OutputSize - targetWidth) / 2;
            int offsetY = (OutputSize - targetHeight) / 2;
            var output = new Color32[OutputSize * OutputSize];

            for (var targetY = 0; targetY < targetHeight; targetY++)
            {
                for (var targetX = 0; targetX < targetWidth; targetX++)
                {
                    Color32 sampled = SampleCell(
                        sourcePixels,
                        source.width,
                        bounds,
                        targetX,
                        targetY,
                        targetWidth,
                        targetHeight);
                    output[(offsetY + targetY) * OutputSize + offsetX + targetX] = sampled;
                }
            }

            return output;
        }

        private static RectInt FindOpaqueBounds(
            IReadOnlyList<Color32> pixels,
            int width,
            int height)
        {
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a <= SourceAlphaThreshold)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
            {
                throw new InvalidDataException("Source image contains no visible pixels.");
            }

            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static Color32 SampleCell(
            IReadOnlyList<Color32> pixels,
            int sourceWidth,
            RectInt bounds,
            int targetX,
            int targetY,
            int targetWidth,
            int targetHeight)
        {
            int startX = bounds.xMin + Mathf.FloorToInt(
                (float)targetX * bounds.width / targetWidth);
            int endX = bounds.xMin + Mathf.CeilToInt(
                (float)(targetX + 1) * bounds.width / targetWidth);
            int startY = bounds.yMin + Mathf.FloorToInt(
                (float)targetY * bounds.height / targetHeight);
            int endY = bounds.yMin + Mathf.CeilToInt(
                (float)(targetY + 1) * bounds.height / targetHeight);

            long red = 0;
            long green = 0;
            long blue = 0;
            long alphaWeight = 0;
            var sampleCount = 0;

            for (var y = startY; y < endY; y++)
            {
                for (var x = startX; x < endX; x++)
                {
                    Color32 pixel = pixels[y * sourceWidth + x];
                    red += pixel.r * pixel.a;
                    green += pixel.g * pixel.a;
                    blue += pixel.b * pixel.a;
                    alphaWeight += pixel.a;
                    sampleCount++;
                }
            }

            float coverage = sampleCount == 0
                ? 0f
                : (float)alphaWeight / (sampleCount * 255f);
            if (coverage < MinimumCoverage || alphaWeight == 0)
            {
                return new Color32(0, 0, 0, 0);
            }

            var averaged = new Color32(
                (byte)(red / alphaWeight),
                (byte)(green / alphaWeight),
                (byte)(blue / alphaWeight),
                255);
            return FindNearestPaletteColor(averaged);
        }

        private static Color32 FindNearestPaletteColor(Color32 color)
        {
            Color32 nearest = Palette[0];
            var nearestDistance = int.MaxValue;

            foreach (Color32 candidate in Palette)
            {
                int red = color.r - candidate.r;
                int green = color.g - candidate.g;
                int blue = color.b - candidate.b;
                int distance = red * red + green * green + blue * blue;
                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearest = candidate;
                nearestDistance = distance;
            }

            return nearest;
        }

        private static Color32[] ScaleNearest(
            IReadOnlyList<Color32> pixels,
            int size,
            int scale)
        {
            int scaledSize = size * scale;
            var scaled = new Color32[scaledSize * scaledSize];
            for (var y = 0; y < scaledSize; y++)
            {
                for (var x = 0; x < scaledSize; x++)
                {
                    scaled[y * scaledSize + x] = pixels[(y / scale) * size + x / scale];
                }
            }

            return scaled;
        }

        private static void WritePng(
            string path,
            int width,
            int height,
            Color32[] pixels)
        {
            string directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException($"Could not resolve directory for {path}.");
            Directory.CreateDirectory(directory);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
