using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Unity.Editor
{
    public enum PixelArtBackgroundMode
    {
        Alpha,
        WhiteBackground
    }

    public enum PixelArtPivotKind
    {
        Center,
        BottomCenter
    }

    [Flags]
    public enum PixelArtRoadConnection
    {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3,
        All = North | East | South | West,
        NotRoad = 1 << 4
    }

    public sealed class PixelArtBatchDefinition
    {
        public PixelArtBatchDefinition(
            string sourceRelativePath,
            string outputAssetPath,
            string previewRelativePath,
            int columns,
            int rows,
            int frameWidth,
            int frameHeight,
            int contentPadding,
            PixelArtPivotKind pivot,
            PixelArtBackgroundMode backgroundMode,
            IReadOnlyList<Color32> palette,
            IReadOnlyList<string> spriteNames,
            int previewScale = 4,
            byte sourceAlphaThreshold = 16,
            byte whiteBackgroundTolerance = 18,
            float minimumCoverage = 0.22f,
            bool opaqueOutput = false,
            Color32? opaqueFillColor = null,
            IReadOnlyList<int> sourceCellIndices = null,
            IReadOnlyList<Vector4> spriteBorders = null,
            IReadOnlyList<PixelArtRoadConnection> roadConnections = null,
            IReadOnlyList<Color32[]> insetBandColors = null)
        {
            if (string.IsNullOrWhiteSpace(sourceRelativePath))
            {
                throw new ArgumentException("Source path is required.", nameof(sourceRelativePath));
            }

            if (string.IsNullOrWhiteSpace(outputAssetPath)
                || !outputAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Output path must be a project-relative path under Assets/.",
                    nameof(outputAssetPath));
            }

            if (columns <= 0 || rows <= 0 || frameWidth <= 0 || frameHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columns),
                    "Grid and frame dimensions must be positive.");
            }

            if (contentPadding < 0
                || contentPadding * 2 >= frameWidth
                || contentPadding * 2 >= frameHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(contentPadding),
                    "Content padding must leave a positive drawable area.");
            }

            if (palette == null || palette.Count == 0)
            {
                throw new ArgumentException("At least one palette color is required.", nameof(palette));
            }

            if (spriteNames == null || spriteNames.Count != columns * rows)
            {
                throw new ArgumentException(
                    "Sprite names must contain exactly one entry for every grid cell.",
                    nameof(spriteNames));
            }

            if (spriteNames.Any(string.IsNullOrWhiteSpace)
                || spriteNames.Distinct(StringComparer.Ordinal).Count() != spriteNames.Count)
            {
                throw new ArgumentException(
                    "Sprite names must be non-empty and unique.",
                    nameof(spriteNames));
            }

            if (spriteNames.Count == 1
                && !string.Equals(
                    Path.GetFileNameWithoutExtension(outputAssetPath),
                    spriteNames[0],
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A Single Sprite name must match its output file name.",
                    nameof(spriteNames));
            }

            if (previewScale < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(previewScale));
            }

            if (minimumCoverage < 0f || minimumCoverage > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumCoverage));
            }

            if (opaqueOutput && !opaqueFillColor.HasValue)
            {
                throw new ArgumentException(
                    "Opaque output requires a fill color.",
                    nameof(opaqueFillColor));
            }

            int cellCount = columns * rows;
            int[] resolvedSourceCellIndices = sourceCellIndices == null
                ? Enumerable.Range(0, cellCount).ToArray()
                : sourceCellIndices.ToArray();
            if (resolvedSourceCellIndices.Length != cellCount
                || resolvedSourceCellIndices.Any(index => index < 0 || index >= cellCount)
                || resolvedSourceCellIndices.Distinct().Count() != cellCount)
            {
                throw new ArgumentException(
                    "Source cell indices must be a permutation with one entry for every output cell.",
                    nameof(sourceCellIndices));
            }

            Vector4[] resolvedSpriteBorders = spriteBorders == null
                ? Enumerable.Repeat(Vector4.zero, cellCount).ToArray()
                : spriteBorders.ToArray();
            if (resolvedSpriteBorders.Length != cellCount
                || resolvedSpriteBorders.Any(border =>
                    border.x < 0f
                    || border.y < 0f
                    || border.z < 0f
                    || border.w < 0f
                    || border.x + border.z > frameWidth
                    || border.y + border.w > frameHeight))
            {
                throw new ArgumentException(
                    "Sprite borders must contain one non-negative, frame-bounded entry per cell.",
                    nameof(spriteBorders));
            }

            PixelArtRoadConnection[] resolvedRoadConnections = roadConnections?.ToArray();
            if (resolvedRoadConnections != null
                && (resolvedRoadConnections.Length != cellCount
                    || resolvedRoadConnections.Any(connection =>
                        connection != PixelArtRoadConnection.NotRoad
                        && (connection & ~PixelArtRoadConnection.All) != 0)))
            {
                throw new ArgumentException(
                    "Road connections must contain one valid entry per cell.",
                    nameof(roadConnections));
            }

            Color32[][] resolvedInsetBandColors = insetBandColors == null
                ? new Color32[cellCount][]
                : insetBandColors.Select(colors => colors?.ToArray()).ToArray();
            if (resolvedInsetBandColors.Length != cellCount
                || resolvedInsetBandColors.Any(colors =>
                    colors != null
                    && (colors.Length == 0
                        || colors.Any(color => !palette.Contains(color)))))
            {
                throw new ArgumentException(
                    "Inset band colors must contain one non-empty palette-color list or null per cell.",
                    nameof(insetBandColors));
            }

            SourceRelativePath = sourceRelativePath;
            OutputAssetPath = outputAssetPath;
            PreviewRelativePath = previewRelativePath;
            Columns = columns;
            Rows = rows;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            ContentPadding = contentPadding;
            Pivot = pivot;
            BackgroundMode = backgroundMode;
            Palette = palette.ToArray();
            SpriteNames = spriteNames.ToArray();
            PreviewScale = previewScale;
            SourceAlphaThreshold = sourceAlphaThreshold;
            WhiteBackgroundTolerance = whiteBackgroundTolerance;
            MinimumCoverage = minimumCoverage;
            OpaqueOutput = opaqueOutput;
            OpaqueFillColor = opaqueFillColor.GetValueOrDefault();
            SourceCellIndices = resolvedSourceCellIndices;
            SpriteBorders = resolvedSpriteBorders;
            RoadConnections = resolvedRoadConnections;
            InsetBandColors = resolvedInsetBandColors;
        }

        public string SourceRelativePath { get; }
        public string OutputAssetPath { get; }
        public string PreviewRelativePath { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int FrameWidth { get; }
        public int FrameHeight { get; }
        public int ContentPadding { get; }
        public PixelArtPivotKind Pivot { get; }
        public PixelArtBackgroundMode BackgroundMode { get; }
        public IReadOnlyList<Color32> Palette { get; }
        public IReadOnlyList<string> SpriteNames { get; }
        public int PreviewScale { get; }
        public byte SourceAlphaThreshold { get; }
        public byte WhiteBackgroundTolerance { get; }
        public float MinimumCoverage { get; }
        public bool OpaqueOutput { get; }
        public Color32 OpaqueFillColor { get; }
        public IReadOnlyList<int> SourceCellIndices { get; }
        public IReadOnlyList<Vector4> SpriteBorders { get; }
        public IReadOnlyList<PixelArtRoadConnection> RoadConnections { get; }
        public IReadOnlyList<Color32[]> InsetBandColors { get; }
        public int OutputWidth => Columns * FrameWidth;
        public int OutputHeight => Rows * FrameHeight;
        public SpriteImportMode ImportMode => SpriteNames.Count == 1
            ? SpriteImportMode.Single
            : SpriteImportMode.Multiple;
        public Vector2 NormalizedPivot => Pivot == PixelArtPivotKind.Center
            ? new Vector2(0.5f, 0.5f)
            : new Vector2(0.5f, 0f);
    }

    public static class CozyTownPixelArtPalettes
    {
        public static readonly Color32[] WarmRural32 =
        {
            Rgb(0x1F, 0x1B, 0x24), Rgb(0x3B, 0x1F, 0x1B),
            Rgb(0xFF, 0xF4, 0xD6), Rgb(0xE6, 0xD5, 0xB8),
            Rgb(0xB4, 0x9A, 0x7A), Rgb(0x6F, 0x5A, 0x4A),
            Rgb(0x21, 0x4E, 0x24), Rgb(0x3E, 0x7B, 0x32),
            Rgb(0x76, 0xB9, 0x47), Rgb(0xB7, 0xD6, 0x5C),
            Rgb(0x5B, 0x2E, 0x1A), Rgb(0x8A, 0x3B, 0x12),
            Rgb(0xA9, 0x5A, 0x2A), Rgb(0xD2, 0x8A, 0x48),
            Rgb(0xD7, 0x5B, 0x18), Rgb(0xF6, 0x8B, 0x2B),
            Rgb(0xFF, 0xC0, 0x5A), Rgb(0xF2, 0xD3, 0x5A),
            Rgb(0x8F, 0x2D, 0x2D), Rgb(0xD9, 0x48, 0x3B),
            Rgb(0xF3, 0x6B, 0x4F), Rgb(0x1F, 0x4E, 0x66),
            Rgb(0x2F, 0x78, 0x91), Rgb(0x58, 0xA6, 0xB8),
            Rgb(0x8E, 0xD0, 0xCE), Rgb(0x6A, 0x4C, 0x93),
            Rgb(0x9B, 0x6B, 0xCB), Rgb(0x8C, 0x4F, 0x32),
            Rgb(0xC9, 0x82, 0x56), Rgb(0xF0, 0xB4, 0x7A),
            Rgb(0xFF, 0xD3, 0xA1), Rgb(0xD7, 0xDE, 0xE8)
        };

        private static Color32 Rgb(byte red, byte green, byte blue)
        {
            return new Color32(red, green, blue, 255);
        }
    }

    public static class CozyTownPixelArtBatchCompiler
    {
        public static void BuildAll(IReadOnlyList<PixelArtBatchDefinition> definitions)
        {
            if (definitions == null || definitions.Count == 0)
            {
                throw new ArgumentException("At least one batch definition is required.", nameof(definitions));
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve the Unity project root.");

            var outputPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (PixelArtBatchDefinition definition in definitions)
            {
                if (definition == null)
                {
                    throw new ArgumentException(
                        "Batch definitions cannot contain null entries.",
                        nameof(definitions));
                }

                if (!outputPaths.Add(definition.OutputAssetPath))
                {
                    throw new InvalidOperationException(
                        $"Duplicate batch output path: {definition.OutputAssetPath}");
                }

                string sourcePath = Path.Combine(projectRoot, definition.SourceRelativePath);
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        $"Pixel-art batch source is missing for '{definition.OutputAssetPath}'.",
                        sourcePath);
                }
            }

            foreach (PixelArtBatchDefinition definition in definitions)
            {
                BuildOne(projectRoot, definition);
            }
        }

        private static void BuildOne(string projectRoot, PixelArtBatchDefinition definition)
        {
            string sourcePath = Path.Combine(projectRoot, definition.SourceRelativePath);
            string outputPath = Path.Combine(projectRoot, definition.OutputAssetPath);
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(sourcePath), false))
                {
                    throw new InvalidDataException($"Could not decode source PNG: {sourcePath}");
                }

                Color32[] outputPixels = CompileSheet(source, definition);
                WritePng(
                    outputPath,
                    definition.OutputWidth,
                    definition.OutputHeight,
                    outputPixels);

                string previewRelativePath = ResolvePreviewRelativePath(definition);
                if (!string.IsNullOrWhiteSpace(previewRelativePath)
                    && definition.PreviewScale > 0)
                {
                    string previewPath = Path.Combine(projectRoot, previewRelativePath);
                    WritePng(
                        previewPath,
                        definition.OutputWidth * definition.PreviewScale,
                        definition.OutputHeight * definition.PreviewScale,
                        ScaleNearest(
                            outputPixels,
                            definition.OutputWidth,
                            definition.OutputHeight,
                            definition.PreviewScale));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            ImportAndSlice(definition);
            Debug.Log($"Built pixel-art batch asset: {definition.OutputAssetPath}");
        }

        private static string ResolvePreviewRelativePath(PixelArtBatchDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(definition.PreviewRelativePath))
            {
                return definition.PreviewRelativePath;
            }

            const string productionPrefix = "Assets/CozyTown/Art/Production/";
            if (!definition.OutputAssetPath.StartsWith(productionPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            string outputFileName = Path.GetFileNameWithoutExtension(definition.OutputAssetPath);
            return $"ArtSource/Previews/A1/{outputFileName}_4x.png";
        }

        private static Color32[] CompileSheet(
            Texture2D source,
            PixelArtBatchDefinition definition)
        {
            Color32[] sourcePixels = source.GetPixels32();
            var output = new Color32[definition.OutputWidth * definition.OutputHeight];

            for (var outputIndex = 0;
                 outputIndex < definition.SourceCellIndices.Count;
                 outputIndex++)
            {
                int sourceIndex = definition.SourceCellIndices[outputIndex];
                int sourceRowFromTop = sourceIndex / definition.Columns;
                int sourceColumn = sourceIndex % definition.Columns;
                int outputRowFromTop = outputIndex / definition.Columns;
                int outputColumn = outputIndex % definition.Columns;
                RectInt sourceCell = GetSourceCell(
                    source.width,
                    source.height,
                    definition.Columns,
                    definition.Rows,
                    sourceColumn,
                    sourceRowFromTop);
                Color32[] cellPixels = ExtractCell(
                    sourcePixels,
                    source.width,
                    sourceCell);
                ApplyBackgroundMode(
                    cellPixels,
                    sourceCell.width,
                    sourceCell.height,
                    definition);
                Color32[] compiledCell = CompileCell(
                    cellPixels,
                    sourceCell.width,
                    sourceCell.height,
                    definition);
                if (definition.RoadConnections != null)
                {
                    RebuildRoadConnections(
                        compiledCell,
                        definition.FrameWidth,
                        definition.FrameHeight,
                        definition.RoadConnections[outputIndex]);
                }
                Color32[] insetBandColors = definition.InsetBandColors[outputIndex];
                if (insetBandColors != null)
                {
                    FillInsetBands(
                        compiledCell,
                        definition.FrameWidth,
                        definition.FrameHeight,
                        insetBandColors);
                }
                CopyCellToSheet(
                    compiledCell,
                    output,
                    definition,
                    outputColumn,
                    outputRowFromTop);
            }

            if (definition.OpaqueOutput)
            {
                FillTransparentPixels(output, definition.OpaqueFillColor);
            }

            return output;
        }

        private static void RebuildRoadConnections(
            Color32[] pixels,
            int width,
            int height,
            PixelArtRoadConnection connections)
        {
            if (connections == PixelArtRoadConnection.NotRoad)
            {
                return;
            }

            var grass = new Color32(0x76, 0xB9, 0x47, 0xFF);
            var roadBorder = new Color32(0x8A, 0x3B, 0x12, 0xFF);
            var roadCore = new Color32(0xD2, 0x8A, 0x48, 0xFF);
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = grass;
            }

            const int corridorWidth = 6;
            int corridorMinX = (width - corridorWidth) / 2;
            int corridorMaxX = corridorMinX + corridorWidth - 1;
            int corridorMinY = (height - corridorWidth) / 2;
            int corridorMaxY = corridorMinY + corridorWidth - 1;

            if ((connections & PixelArtRoadConnection.North) != 0)
            {
                DrawVerticalCorridor(
                    pixels, width, corridorMinX, corridorMaxX,
                    corridorMaxY + 1, height - 1, roadBorder, roadCore);
            }

            if ((connections & PixelArtRoadConnection.South) != 0)
            {
                DrawVerticalCorridor(
                    pixels, width, corridorMinX, corridorMaxX,
                    0, corridorMinY - 1, roadBorder, roadCore);
            }

            if ((connections & PixelArtRoadConnection.East) != 0)
            {
                DrawHorizontalCorridor(
                    pixels, width, corridorMaxX + 1, width - 1,
                    corridorMinY, corridorMaxY, roadBorder, roadCore);
            }

            if ((connections & PixelArtRoadConnection.West) != 0)
            {
                DrawHorizontalCorridor(
                    pixels, width, 0, corridorMinX - 1,
                    corridorMinY, corridorMaxY, roadBorder, roadCore);
            }

            for (var y = corridorMinY; y <= corridorMaxY; y++)
            {
                for (var x = corridorMinX; x <= corridorMaxX; x++)
                {
                    pixels[y * width + x] = roadCore;
                }
            }
        }

        private static void DrawVerticalCorridor(
            Color32[] pixels,
            int width,
            int minX,
            int maxX,
            int minY,
            int maxY,
            Color32 border,
            Color32 core)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    pixels[y * width + x] = x == minX || x == maxX ? border : core;
                }
            }
        }

        private static void DrawHorizontalCorridor(
            Color32[] pixels,
            int width,
            int minX,
            int maxX,
            int minY,
            int maxY,
            Color32 border,
            Color32 core)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    pixels[y * width + x] = y == minY || y == maxY ? border : core;
                }
            }
        }

        private static void FillTransparentPixels(Color32[] pixels, Color32 fillColor)
        {
            fillColor.a = 255;
            for (var index = 0; index < pixels.Length; index++)
            {
                if (pixels[index].a == 0)
                {
                    pixels[index] = fillColor;
                }
            }
        }

        private static void FillInsetBands(
            Color32[] pixels,
            int width,
            int height,
            IReadOnlyList<Color32> colors)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int edgeDistance = Math.Min(
                        Math.Min(x, width - 1 - x),
                        Math.Min(y, height - 1 - y));
                    pixels[y * width + x] = colors[Math.Min(edgeDistance, colors.Count - 1)];
                }
            }
        }

        private static RectInt GetSourceCell(
            int sourceWidth,
            int sourceHeight,
            int columns,
            int rows,
            int column,
            int rowFromTop)
        {
            int xMin = Mathf.FloorToInt((float)column * sourceWidth / columns);
            int xMax = Mathf.FloorToInt((float)(column + 1) * sourceWidth / columns);
            int rowFromBottom = rows - 1 - rowFromTop;
            int yMin = Mathf.FloorToInt((float)rowFromBottom * sourceHeight / rows);
            int yMax = Mathf.FloorToInt((float)(rowFromBottom + 1) * sourceHeight / rows);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static Color32[] ExtractCell(
            IReadOnlyList<Color32> sourcePixels,
            int sourceWidth,
            RectInt cell)
        {
            var extracted = new Color32[cell.width * cell.height];
            for (var y = 0; y < cell.height; y++)
            {
                for (var x = 0; x < cell.width; x++)
                {
                    extracted[y * cell.width + x] =
                        sourcePixels[(cell.yMin + y) * sourceWidth + cell.xMin + x];
                }
            }

            return extracted;
        }

        private static void ApplyBackgroundMode(
            Color32[] pixels,
            int width,
            int height,
            PixelArtBatchDefinition definition)
        {
            if (definition.BackgroundMode == PixelArtBackgroundMode.Alpha)
            {
                return;
            }

            RemoveConnectedWhiteBackground(
                pixels,
                width,
                height,
                definition.WhiteBackgroundTolerance);
        }

        private static void RemoveConnectedWhiteBackground(
            Color32[] pixels,
            int width,
            int height,
            byte tolerance)
        {
            var background = new bool[pixels.Length];
            var pending = new Queue<int>();

            for (var x = 0; x < width; x++)
            {
                EnqueueWhite(x, 0, width, pixels, background, pending, tolerance);
                EnqueueWhite(x, height - 1, width, pixels, background, pending, tolerance);
            }

            for (var y = 1; y < height - 1; y++)
            {
                EnqueueWhite(0, y, width, pixels, background, pending, tolerance);
                EnqueueWhite(width - 1, y, width, pixels, background, pending, tolerance);
            }

            while (pending.Count > 0)
            {
                int index = pending.Dequeue();
                int x = index % width;
                int y = index / width;
                EnqueueWhite(x - 1, y, width, pixels, background, pending, tolerance);
                EnqueueWhite(x + 1, y, width, pixels, background, pending, tolerance);
                EnqueueWhite(x, y - 1, width, pixels, background, pending, tolerance);
                EnqueueWhite(x, y + 1, width, pixels, background, pending, tolerance);
            }

            for (var index = 0; index < pixels.Length; index++)
            {
                Color32 pixel = pixels[index];
                pixels[index] = background[index]
                    ? new Color32(0, 0, 0, 0)
                    : new Color32(pixel.r, pixel.g, pixel.b, 255);
            }
        }

        private static void EnqueueWhite(
            int x,
            int y,
            int width,
            IReadOnlyList<Color32> pixels,
            IList<bool> background,
            Queue<int> pending,
            byte tolerance)
        {
            int height = pixels.Count / width;
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            int index = y * width + x;
            if (background[index] || !IsNearWhite(pixels[index], tolerance))
            {
                return;
            }

            background[index] = true;
            pending.Enqueue(index);
        }

        private static bool IsNearWhite(Color32 pixel, byte tolerance)
        {
            return pixel.a == 0
                || 255 - pixel.r <= tolerance
                && 255 - pixel.g <= tolerance
                && 255 - pixel.b <= tolerance;
        }

        private static Color32[] CompileCell(
            IReadOnlyList<Color32> pixels,
            int width,
            int height,
            PixelArtBatchDefinition definition)
        {
            RectInt bounds = FindOpaqueBounds(
                pixels,
                width,
                height,
                definition.SourceAlphaThreshold);
            int contentWidth = definition.FrameWidth - definition.ContentPadding * 2;
            int contentHeight = definition.FrameHeight - definition.ContentPadding * 2;
            float scale = Math.Min(
                (float)contentWidth / bounds.width,
                (float)contentHeight / bounds.height);
            int targetWidth = Math.Max(1, Mathf.RoundToInt(bounds.width * scale));
            int targetHeight = Math.Max(1, Mathf.RoundToInt(bounds.height * scale));
            int offsetX = (definition.FrameWidth - targetWidth) / 2;
            int offsetY = definition.Pivot == PixelArtPivotKind.BottomCenter
                ? definition.ContentPadding
                : (definition.FrameHeight - targetHeight) / 2;
            var output = new Color32[definition.FrameWidth * definition.FrameHeight];

            for (var targetY = 0; targetY < targetHeight; targetY++)
            {
                for (var targetX = 0; targetX < targetWidth; targetX++)
                {
                    output[(offsetY + targetY) * definition.FrameWidth + offsetX + targetX] =
                        SampleCell(
                            pixels,
                            width,
                            bounds,
                            targetX,
                            targetY,
                            targetWidth,
                            targetHeight,
                            definition);
                }
            }

            if (definition.Pivot == PixelArtPivotKind.BottomCenter)
            {
                GroundBottomCenter(
                    output,
                    definition.FrameWidth,
                    definition.FrameHeight,
                    definition.ContentPadding);
            }

            return output;
        }

        private static void GroundBottomCenter(
            Color32[] pixels,
            int width,
            int height,
            int groundY)
        {
            var minimumOpaqueY = height;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a > 0)
                    {
                        minimumOpaqueY = Math.Min(minimumOpaqueY, y);
                    }
                }
            }

            int shift = minimumOpaqueY - groundY;
            if (shift <= 0 || minimumOpaqueY == height)
            {
                return;
            }

            var grounded = new Color32[pixels.Length];
            for (var y = minimumOpaqueY; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    grounded[(y - shift) * width + x] = pixels[y * width + x];
                }
            }

            Array.Copy(grounded, pixels, pixels.Length);
        }

        private static RectInt FindOpaqueBounds(
            IReadOnlyList<Color32> pixels,
            int width,
            int height,
            byte alphaThreshold)
        {
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a <= alphaThreshold)
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
                throw new InvalidDataException("A source grid cell contains no visible pixels.");
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
            int targetHeight,
            PixelArtBatchDefinition definition)
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
            if (coverage < definition.MinimumCoverage || alphaWeight == 0)
            {
                return new Color32(0, 0, 0, 0);
            }

            var averaged = new Color32(
                (byte)(red / alphaWeight),
                (byte)(green / alphaWeight),
                (byte)(blue / alphaWeight),
                255);
            return FindNearestPaletteColor(averaged, definition.Palette);
        }

        private static Color32 FindNearestPaletteColor(
            Color32 color,
            IReadOnlyList<Color32> palette)
        {
            Color32 nearest = palette[0];
            var nearestDistance = int.MaxValue;
            foreach (Color32 candidate in palette)
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

        private static void CopyCellToSheet(
            IReadOnlyList<Color32> cell,
            Color32[] sheet,
            PixelArtBatchDefinition definition,
            int column,
            int rowFromTop)
        {
            int destinationX = column * definition.FrameWidth;
            int destinationY = (definition.Rows - 1 - rowFromTop) * definition.FrameHeight;
            for (var y = 0; y < definition.FrameHeight; y++)
            {
                for (var x = 0; x < definition.FrameWidth; x++)
                {
                    sheet[(destinationY + y) * definition.OutputWidth + destinationX + x] =
                        cell[y * definition.FrameWidth + x];
                }
            }
        }

        private static Color32[] ScaleNearest(
            IReadOnlyList<Color32> pixels,
            int width,
            int height,
            int scale)
        {
            int scaledWidth = width * scale;
            int scaledHeight = height * scale;
            var scaled = new Color32[scaledWidth * scaledHeight];
            for (var y = 0; y < scaledHeight; y++)
            {
                for (var x = 0; x < scaledWidth; x++)
                {
                    scaled[y * scaledWidth + x] = pixels[(y / scale) * width + x / scale];
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

        private static void ImportAndSlice(PixelArtBatchDefinition definition)
        {
            AssetDatabase.ImportAsset(
                definition.OutputAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(definition.OutputAssetPath) as TextureImporter
                ?? throw new InvalidOperationException(
                    $"Generated PNG did not receive TextureImporter: {definition.OutputAssetPath}");

            CozyTownPixelArtImportProfile.Apply(importer);
            importer.spriteImportMode = definition.ImportMode;
            if (definition.ImportMode == SpriteImportMode.Multiple)
            {
                var metadata = new SpriteMetaData[definition.SpriteNames.Count];
                for (var index = 0; index < metadata.Length; index++)
                {
                    int rowFromTop = index / definition.Columns;
                    int column = index % definition.Columns;
                    metadata[index] = new SpriteMetaData
                    {
                        name = definition.SpriteNames[index],
                        rect = new Rect(
                            column * definition.FrameWidth,
                            (definition.Rows - 1 - rowFromTop) * definition.FrameHeight,
                            definition.FrameWidth,
                            definition.FrameHeight),
                        alignment = (int)SpriteAlignment.Custom,
                        pivot = definition.NormalizedPivot,
                        border = definition.SpriteBorders[index]
                    };
                }

#pragma warning disable 618
                importer.spritesheet = metadata;
#pragma warning restore 618
            }
            else
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = definition.NormalizedPivot;
                importer.SetTextureSettings(settings);
            }

            importer.SaveAndReimport();
        }
    }

    public static class CozyTownPixelArtImportProfile
    {
        public static void Apply(TextureImporter importer)
        {
            if (importer == null)
            {
                throw new ArgumentNullException(nameof(importer));
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 16f;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.sRGBTexture = true;

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            if (standalone.overridden)
            {
                standalone.overridden = false;
                importer.SetPlatformTextureSettings(standalone);
            }
        }
    }
}
