param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [ValidateSet('shopkeeper_mina', 'farmer_eli', 'fisher_ren', 'cook_sora')]
    [string]$NpcSuffix,
    [string]$AlternativeSourcePath,
    [string]$OutputRoot,
    [switch]$Homes,
    [switch]$PortraitColorNormalization,
    [switch]$RemoveNeutralBackground,
    [switch]$MirrorRightFromLeft
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# All twelve source poses share one scale. Only translation varies by pose.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

public static class T1NativeSpriteNormalizer
{
    private const string Codes = "0123456789ABCDEFGHIJKLMNOPQRSTUV";
    private static readonly string[] PaletteHex = {
        "1F1B24", "3B1F1B", "FFF4D6", "E6D5B8", "B49A7A", "6F5A4A", "214E24", "3E7B32",
        "76B947", "B7D65C", "5B2E1A", "8A3B12", "A95A2A", "D28A48", "D75B18", "F68B2B",
        "FFC05A", "F2D35A", "8F2D2D", "D9483B", "F36B4F", "1F4E66", "2F7891", "58A6B8",
        "8ED0CE", "6A4C93", "9B6BCB", "8C4F32", "C98256", "F0B47A", "FFD3A1", "D7DEE8"
    };

    public static string[] Normalize(string sourcePath, string authoredRoot, string suffix,
        bool removeNeutralBackground, bool mirrorRightFromLeft, bool normalizePortraitColors)
    {
        var messages = new List<string>();
        var palette = new Color[PaletteHex.Length];
        for (int index = 0; index < palette.Length; index++)
            palette[index] = Color.FromArgb(255, ColorTranslator.FromHtml("#" + PaletteHex[index]));

        using (var source = new Bitmap(sourcePath))
        using (var sheet = new Bitmap(72, 128, PixelFormat.Format32bppArgb))
        {
            bool[,] background = ExteriorBackground(source, removeNeutralBackground);
            Rectangle[] sourceRows = FindSourceRows(source, background);
            var cells = new Rectangle[12];
            var bounds = new Rectangle[12];
            double scale = double.MaxValue;
            for (int frame = 0; frame < 12; frame++)
            {
                int column = frame % 3;
                int row = frame / 3;
                int left = column * source.Width / 3;
                int top = sourceRows[row].Top;
                int right = (column + 1) * source.Width / 3;
                int bottom = sourceRows[row].Bottom;
                cells[frame] = Rectangle.FromLTRB(left, top, right, bottom);
                bounds[frame] = OpaqueBounds(source, cells[frame], background);
                if (!mirrorRightFromLeft || frame < 6 || frame > 8)
                    scale = Math.Min(scale, Math.Min(20.0 / bounds[frame].Width, 31.0 / bounds[frame].Height));
            }

            string folder = Path.Combine(authoredRoot, "Characters", suffix);
            string reviewFolder = Path.Combine(authoredRoot, "Review");
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(reviewFolder);
            messages.Add("Uniform source-to-native scale: " + scale.ToString("F8", System.Globalization.CultureInfo.InvariantCulture));
            string[] directions = { "down", "left", "right", "up" };
            string[] actions = { "idle", "walk", "walk" };

            for (int frame = 0; frame < 12; frame++)
            {
                bool mirror = mirrorRightFromLeft && frame >= 6 && frame <= 8;
                Rectangle body = bounds[mirror ? frame - 3 : frame];
                double sourceLeft = body.Left + body.Width * 0.5 - 12.0 / scale;
                double sourceTop = body.Bottom - 32.0 / scale;
                var rows = new string[32];
                int minimumX = 24, maximumX = -1, minimumY = 32, maximumY = -1, groundPixels = 0;
                for (int y = 0; y < 32; y++)
                {
                    var codes = new char[24];
                    for (int x = 0; x < 24; x++)
                    {
                        int sampleX = mirror ? 23 - x : x;
                        int colorIndex = Sample(source, body, background, palette,
                            sourceLeft + sampleX / scale, sourceTop + y / scale,
                            sourceLeft + (sampleX + 1) / scale, sourceTop + (y + 1) / scale,
                            normalizePortraitColors);
                        if (normalizePortraitColors && suffix == "farmer_eli")
                            colorIndex = FarmerSkinColor(colorIndex, frame / 3, y);
                        if (suffix == "fisher_ren" && frame == 0 && x >= 10 && x <= 14)
                        {
                            // Restore the continuous rounded cap crown with a one-pixel dark outline.
                            if (y == 1)
                            {
                                if (colorIndex != -1) throw new InvalidDataException("Ren cap crown requires an empty row above its outline.");
                                colorIndex = 0;
                            }
                            else if (y == 2)
                            {
                                if (colorIndex != 0) throw new InvalidDataException("Ren cap crown requires the existing dark top outline.");
                                colorIndex = 21;
                            }
                        }
                        codes[x] = colorIndex < 0 ? '.' : Codes[colorIndex];
                        Color pixel = colorIndex < 0 ? Color.Transparent : palette[colorIndex];
                        sheet.SetPixel(frame % 3 * 24 + x, frame / 3 * 32 + y, pixel);
                        if (colorIndex < 0) continue;
                        minimumX = Math.Min(minimumX, x); maximumX = Math.Max(maximumX, x);
                        minimumY = Math.Min(minimumY, y); maximumY = Math.Max(maximumY, y);
                        if (y == 31) groundPixels++;
                    }
                    rows[y] = new string(codes);
                }

                string name = "npc_" + suffix + "_" + actions[frame % 3] + "_" + directions[frame / 3];
                if (frame % 3 != 0) name += frame % 3 == 1 ? "_00" : "_01";
                File.WriteAllLines(Path.Combine(folder, name + ".pixels"), rows);
                messages.Add(name + ": x=" + minimumX + ".." + maximumX + ", top=" + minimumY
                    + ", bottom=" + maximumY + ", height=" + (maximumY - minimumY + 1) + ", ground=" + groundPixels);
            }

            sheet.Save(Path.Combine(reviewFolder, "npc_" + suffix + "_normalized_native.png"), ImageFormat.Png);
            using (var preview = new Bitmap(576, 1024, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < preview.Height; y++)
                    for (int x = 0; x < preview.Width; x++)
                        preview.SetPixel(x, y, sheet.GetPixel(x / 8, y / 8));
                preview.Save(Path.Combine(reviewFolder, "npc_" + suffix + "_normalized_8x.png"), ImageFormat.Png);
            }
        }
        return messages.ToArray();
    }

    public static string[] NormalizeHomes(string firstSourcePath, string secondSourcePath, string authoredRoot)
    {
        var messages = new List<string>();
        var palette = new Color[PaletteHex.Length];
        for (int index = 0; index < palette.Length; index++)
            palette[index] = Color.FromArgb(255, ColorTranslator.FromHtml("#" + PaletteHex[index]));

        using (var first = new Bitmap(firstSourcePath))
        using (var second = new Bitmap(secondSourcePath))
        using (var sheet = new Bitmap(128, 128, PixelFormat.Format32bppArgb))
        using (var roofs = new Bitmap(128, 128, PixelFormat.Format32bppArgb))
        {
            // Door centers measured from the generated source, before native normalization.
            Bitmap[] sources = { second, first, second, first };
            int[] doorCenters = { 363, 1018, 378, 1018 };
            string[] owners = { "shopkeeper_mina", "fisher_ren", "cook_sora", "farmer_eli" };
            bool[][,] masks = { ExteriorBackground(second, true), ExteriorBackground(first, true) };
            var bounds = new Rectangle[4];
            double scale = double.MaxValue;
            for (int index = 0; index < 4; index++)
            {
                Bitmap source = sources[index];
                int left = index % 2 * source.Width / 2;
                int top = index / 2 * source.Height / 2;
                var cell = Rectangle.FromLTRB(left, top, left + source.Width / 2, top + source.Height / 2);
                bounds[index] = OpaqueBounds(source, cell, masks[index % 2]);
                Rectangle body = bounds[index];
                scale = Math.Min(scale, Math.Min(63.0 / body.Height,
                    Math.Min(38.0 / (doorCenters[index] - body.Left), 26.0 / (body.Right - doorCenters[index]))));
            }
            string folder = Path.Combine(authoredRoot, "Buildings");
            string reviewFolder = Path.Combine(authoredRoot, "Review");
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(reviewFolder);
            messages.Add("Uniform house scale: " + scale.ToString("F8", System.Globalization.CultureInfo.InvariantCulture));
            for (int index = 0; index < 4; index++)
            {
                Rectangle body = bounds[index];
                double sourceLeft = doorCenters[index] - 38.0 / scale;
                double sourceTop = body.Bottom - 64.0 / scale;
                var rows = new string[64];
                var roofRows = new string[64];
                for (int y = 0; y < 64; y++)
                {
                    var codes = new char[64];
                    for (int x = 0; x < 64; x++)
                    {
                        int colorIndex = Sample(sources[index], body, masks[index % 2], palette,
                            sourceLeft + x / scale, sourceTop + y / scale,
                            sourceLeft + (x + 1) / scale, sourceTop + (y + 1) / scale);
                        codes[x] = colorIndex < 0 ? '.' : Codes[colorIndex];
                        Color pixel = colorIndex < 0 ? Color.Transparent : palette[colorIndex];
                        sheet.SetPixel(index % 2 * 64 + x, index / 2 * 64 + y, pixel);
                        roofs.SetPixel(index % 2 * 64 + x, index / 2 * 64 + y, y < 26 ? pixel : Color.Transparent);
                    }
                    rows[y] = new string(codes);
                    roofRows[y] = y < 26 ? rows[y] : new string('.', 64);
                }
                string name = "bld_home_" + owners[index];
                File.WriteAllLines(Path.Combine(folder, name + ".pixels"), rows);
                File.WriteAllLines(Path.Combine(folder, name + "_roof_foreground.pixels"), roofRows);
                messages.Add(name + ": source=" + (index % 2 == 0 ? "v02" : "v01") + ", sourceBounds=" + body
                    + ", doorCenter=" + doorCenters[index] + " -> native x38; roof retained top26 rows.");
            }
            SaveReview(sheet, reviewFolder, "bld_npc_homes");
            SaveReview(roofs, reviewFolder, "bld_npc_home_roofs");
        }
        return messages.ToArray();
    }

    private static void SaveReview(Bitmap sheet, string folder, string name)
    {
        sheet.Save(Path.Combine(folder, name + "_normalized_native.png"), ImageFormat.Png);
        using (var preview = new Bitmap(sheet.Width * 8, sheet.Height * 8, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < preview.Height; y++)
                for (int x = 0; x < preview.Width; x++)
                    preview.SetPixel(x, y, sheet.GetPixel(x / 8, y / 8));
            preview.Save(Path.Combine(folder, name + "_normalized_8x.png"), ImageFormat.Png);
        }
    }

    private static Rectangle OpaqueBounds(Bitmap source, Rectangle cell, bool[,] background)
    {
        int left = cell.Right, top = cell.Bottom, right = cell.Left, bottom = cell.Top;
        for (int y = cell.Top; y < cell.Bottom; y++)
            for (int x = cell.Left; x < cell.Right; x++)
            {
                if (background[x, y] || source.GetPixel(x, y).A < 128) continue;
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
            }
        if (right <= left || bottom <= top) throw new InvalidDataException("A source cell has no opaque character pixels.");
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Rectangle[] FindSourceRows(Bitmap source, bool[,] background)
    {
        var rows = new List<Rectangle>();
        int start = -1;
        for (int y = 0; y <= source.Height; y++)
        {
            int opaque = 0;
            if (y < source.Height)
                for (int x = 0; x < source.Width; x++)
                    if (!background[x, y] && source.GetPixel(x, y).A >= 128) opaque++;
            bool occupied = opaque >= 5;
            if (occupied && start < 0) start = y;
            if (!occupied && start >= 0)
            {
                rows.Add(new Rectangle(0, start, source.Width, y - start));
                start = -1;
            }
        }
        if (rows.Count != 4)
            throw new InvalidDataException("Expected four separated sprite rows; found " + rows.Count + ".");
        return rows.ToArray();
    }

    private static int FarmerSkinColor(int colorIndex, int direction, int y)
    {
        bool face = direction < 3 && y >= 10 && y <= 17;
        bool hands = y >= 20 && y <= 25;
        if (!face && !hands) return colorIndex;
        // Keep the orange straw-hat ramp; use the portrait's peach ramp on exposed skin.
        if (colorIndex == 14) return 28;
        if (colorIndex == 15) return 29;
        if (colorIndex == 16) return 30;
        return colorIndex;
    }

    private static int Sample(Bitmap source, Rectangle body, bool[,] background, Color[] palette,
        double left, double top, double right, double bottom, bool averageWarmColors = false)
    {
        int[] counts = new int[palette.Length];
        int total = 0, opaque = 0;
        long redTotal = 0, greenTotal = 0, blueTotal = 0;
        for (int y = (int)Math.Floor(top); y < (int)Math.Ceiling(bottom); y++)
            for (int x = (int)Math.Floor(left); x < (int)Math.Ceiling(right); x++)
            {
                total++;
                if (!body.Contains(x, y) || background[x, y]) continue;
                Color pixel = source.GetPixel(x, y);
                if (pixel.A < 128) continue;
                opaque++;
                redTotal += pixel.R; greenTotal += pixel.G; blueTotal += pixel.B;
                int nearest = 0, minimumDistance = int.MaxValue;
                for (int index = 0; index < palette.Length; index++)
                {
                    int red = pixel.R - palette[index].R;
                    int green = pixel.G - palette[index].G;
                    int blue = pixel.B - palette[index].B;
                    int distance = red * red + green * green + blue * blue;
                    if (distance < minimumDistance) { minimumDistance = distance; nearest = index; }
                }
                counts[nearest]++;
            }
        if (opaque < total * 0.22) return -1;
        int result = 0;
        for (int index = 1; index < counts.Length; index++)
            if (counts[index] > counts[result]) result = index;
        if (averageWarmColors && result > 2 && result != 16
            && (result <= 5 || result >= 10 && result <= 17 || result >= 27))
        {
            double red = (double)redTotal / opaque, green = (double)greenTotal / opaque, blue = (double)blueTotal / opaque;
            double minimumDistance = double.MaxValue;
            for (int index = 0; index < palette.Length; index++)
            {
                double dr = red - palette[index].R, dg = green - palette[index].G, db = blue - palette[index].B;
                double distance = dr * dr + dg * dg + db * db;
                if (distance < minimumDistance) { minimumDistance = distance; result = index; }
            }
        }
        return result;
    }

    private static bool[,] ExteriorBackground(Bitmap source, bool removeNeutralBackground)
    {
        var background = new bool[source.Width, source.Height];
        if (!removeNeutralBackground) return background;
        var pending = new Queue<Point>();
        for (int x = 0; x < source.Width; x++)
        {
            pending.Enqueue(new Point(x, 0));
            pending.Enqueue(new Point(x, source.Height - 1));
        }
        for (int y = 0; y < source.Height; y++)
        {
            pending.Enqueue(new Point(0, y));
            pending.Enqueue(new Point(source.Width - 1, y));
        }
        while (pending.Count > 0)
        {
            Point point = pending.Dequeue();
            int x = point.X, y = point.Y;
            if (x < 0 || y < 0 || x >= source.Width || y >= source.Height || background[x, y]) continue;
            Color pixel = source.GetPixel(x, y);
            int minimum = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
            int maximum = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
            if (pixel.A >= 128 && (minimum < 220 || maximum - minimum > 15)) continue;
            background[x, y] = true;
            pending.Enqueue(new Point(x - 1, y)); pending.Enqueue(new Point(x + 1, y));
            pending.Enqueue(new Point(x, y - 1)); pending.Enqueue(new Point(x, y + 1));
        }
        return background;
    }
}
'@

$sourceFile = (Resolve-Path -LiteralPath $SourcePath).Path
$authoredRoot = if ($OutputRoot) { $OutputRoot } else { $PSScriptRoot }
if ($Homes) {
    $alternativeFile = (Resolve-Path -LiteralPath $AlternativeSourcePath).Path
    [T1NativeSpriteNormalizer]::NormalizeHomes($sourceFile, $alternativeFile, $authoredRoot)
} else {
    if (-not $NpcSuffix) { throw 'NpcSuffix is required for character normalization.' }
    [T1NativeSpriteNormalizer]::Normalize($sourceFile, $authoredRoot, $NpcSuffix,
        $RemoveNeutralBackground.IsPresent, $MirrorRightFromLeft.IsPresent, $PortraitColorNormalization.IsPresent)
}
