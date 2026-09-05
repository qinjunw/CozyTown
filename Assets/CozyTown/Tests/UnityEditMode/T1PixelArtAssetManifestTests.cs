using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class T1PixelArtAssetManifestTests
    {
        private const string Production = "Assets/CozyTown/Art/Production/";
        private static readonly HashSet<int> Palette = new HashSet<int>
        {
            0x1F1B24, 0x3B1F1B, 0xFFF4D6, 0xE6D5B8, 0xB49A7A, 0x6F5A4A, 0x214E24, 0x3E7B32,
            0x76B947, 0xB7D65C, 0x5B2E1A, 0x8A3B12, 0xA95A2A, 0xD28A48, 0xD75B18, 0xF68B2B,
            0xFFC05A, 0xF2D35A, 0x8F2D2D, 0xD9483B, 0xF36B4F, 0x1F4E66, 0x2F7891, 0x58A6B8,
            0x8ED0CE, 0x6A4C93, 0x9B6BCB, 0x8C4F32, 0xC98256, 0xF0B47A, 0xFFD3A1, 0xD7DEE8
        };

        [TestCase("Characters/npc_shopkeeper_mina_move_24x32.png", 72, 128, 12)]
        [TestCase("Characters/npc_farmer_eli_move_24x32.png", 72, 128, 12)]
        [TestCase("Characters/npc_fisher_ren_move_24x32.png", 72, 128, 12)]
        [TestCase("Characters/npc_cook_sora_move_24x32.png", 72, 128, 12)]
        [TestCase("Buildings/bld_npc_homes_64.png", 128, 128, 4)]
        [TestCase("Buildings/bld_npc_home_roofs_64.png", 128, 128, 4)]
        public void ProductionSheet_UsesNativePaletteImportAndExactFourTimesPreview(
            string relativePath, int width, int height, int spriteCount)
        {
            string path = Production + relativePath;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null, path);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.alphaIsTransparency, Is.True);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count(), Is.EqualTo(spriteCount));

            Texture2D native = ReadPng(path);
            Texture2D preview = null;
            try
            {
                Assert.That(native.width, Is.EqualTo(width));
                Assert.That(native.height, Is.EqualTo(height));
                Color32[] pixels = native.GetPixels32();
                Assert.That(pixels.Any(pixel => pixel.a == 0), Is.True);
                Assert.That(pixels.Any(pixel => pixel.a == 255), Is.True);
                foreach (Color32 pixel in pixels)
                {
                    Assert.That(pixel.a == 0 || pixel.a == 255, Is.True, "Alpha must be binary.");
                    if (pixel.a != 0)
                        Assert.That(Palette.Contains(pixel.r << 16 | pixel.g << 8 | pixel.b), Is.True);
                }

                preview = ReadPng("ArtSource/Previews/T1/" + Path.GetFileNameWithoutExtension(path) + "_4x.png");
                Assert.That(preview.width, Is.EqualTo(width * 4));
                Assert.That(preview.height, Is.EqualTo(height * 4));
                Color32[] enlarged = preview.GetPixels32();
                for (int y = 0; y < preview.height; y++)
                    for (int x = 0; x < preview.width; x++)
                        Assert.That(enlarged[y * preview.width + x], Is.EqualTo(pixels[y / 4 * width + x / 4]));
            }
            finally
            {
                Object.DestroyImmediate(native);
                if (preview != null) Object.DestroyImmediate(preview);
            }
        }

        [TestCase("shopkeeper_mina")]
        [TestCase("farmer_eli")]
        [TestCase("fisher_ren")]
        [TestCase("cook_sora")]
        public void MovementFrames_KeepCompleteGroundedBodiesAndMirroredSideSilhouettes(string owner)
        {
            Texture2D sheet = ReadPng(Production + "Characters/npc_" + owner + "_move_24x32.png");
            try
            {
                Color32[] pixels = sheet.GetPixels32();
                int smallestHeight = 32, largestHeight = 0;
                for (int index = 0; index < 12; index++)
                {
                    Color32[] frame = Cell(pixels, sheet.width, sheet.height, index, 3, 24, 32);
                    int minX = 24, maxX = -1, minY = 32, maxY = -1, bottomPixels = 0;
                    for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 24; x++)
                        {
                            if (frame[y * 24 + x].a == 0) continue;
                            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                            minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                            if (y == 0) bottomPixels++;
                        }
                    Assert.That(minX, Is.GreaterThanOrEqualTo(2), owner + " frame " + index);
                    Assert.That(maxX, Is.LessThanOrEqualTo(21));
                    Assert.That((minX + maxX) * 0.5f, Is.InRange(11f, 12f));
                    Assert.That(minY, Is.Zero);
                    Assert.That(maxY, Is.InRange(29, 30));
                    Assert.That(bottomPixels, Is.GreaterThanOrEqualTo(2));
                    AssertConnectedToFeet(frame, owner + " frame " + index);
                    smallestHeight = Mathf.Min(smallestHeight, maxY + 1);
                    largestHeight = Mathf.Max(largestHeight, maxY + 1);
                }
                Assert.That(largestHeight - smallestHeight, Is.LessThanOrEqualTo(1));
                for (int pose = 0; pose < 3; pose++)
                {
                    Color32[] left = Cell(pixels, sheet.width, sheet.height, 3 + pose, 3, 24, 32);
                    Color32[] right = Cell(pixels, sheet.width, sheet.height, 6 + pose, 3, 24, 32);
                    Assert.That(MirroredSilhouetteOverlap(left, right, 32), Is.GreaterThanOrEqualTo(0.9f));
                    Assert.That(MirroredSilhouetteOverlap(left, right, 8), Is.GreaterThanOrEqualTo(0.85f));
                }
                for (int direction = 0; direction < 4; direction++)
                {
                    Color32[] first = Cell(pixels, sheet.width, sheet.height, direction * 3 + 1, 3, 24, 32);
                    Color32[] second = Cell(pixels, sheet.width, sheet.height, direction * 3 + 2, 3, 24, 32);
                    Assert.That(first.Take(24 * 8).SequenceEqual(second.Take(24 * 8)), Is.False,
                        owner + " direction " + direction + " must have two different walking phases.");
                }
            }
            finally { Object.DestroyImmediate(sheet); }
        }

        [Test]
        public void HomeRoofs_PreserveEachOwnersUpperPixelsAndClearTheLowerThirtyEightRows()
        {
            string homePath = Production + "Buildings/bld_npc_homes_64.png";
            string roofPath = Production + "Buildings/bld_npc_home_roofs_64.png";
            string[] owners = { "shopkeeper_mina", "fisher_ren", "cook_sora", "farmer_eli" };
            Sprite[] homes = AssetDatabase.LoadAllAssetsAtPath(homePath).OfType<Sprite>().ToArray();
            Sprite[] roofs = AssetDatabase.LoadAllAssetsAtPath(roofPath).OfType<Sprite>().ToArray();
            Assert.That(homes.Select(sprite => sprite.name),
                Is.EquivalentTo(owners.Select(owner => "bld_home_" + owner)));
            Assert.That(roofs.Select(sprite => sprite.name),
                Is.EquivalentTo(owners.Select(owner => "bld_home_" + owner + "_roof_foreground")));

            Texture2D homeSheet = ReadPng(homePath);
            Texture2D roofSheet = ReadPng(roofPath);
            try
            {
                for (int index = 0; index < owners.Length; index++)
                {
                    Sprite home = homes.Single(sprite => sprite.name == "bld_home_" + owners[index]);
                    Sprite roof = roofs.Single(sprite => sprite.name == home.name + "_roof_foreground");
                    Rect expectedRect = new Rect(index % 2 * 64, (1 - index / 2) * 64, 64, 64);
                    Assert.That(home.rect, Is.EqualTo(expectedRect));
                    Assert.That(roof.rect, Is.EqualTo(expectedRect));
                    Assert.That(home.pivot, Is.EqualTo(new Vector2(32, 0)));
                    Assert.That(roof.pivot, Is.EqualTo(home.pivot));
                    Assert.That(home.pixelsPerUnit, Is.EqualTo(16));
                    Assert.That(roof.pixelsPerUnit, Is.EqualTo(16));
                    Color32[] body = Cell(homeSheet.GetPixels32(), 128, 128, index, 2, 64, 64);
                    Color32[] overlay = Cell(roofSheet.GetPixels32(), 128, 128, index, 2, 64, 64);
                    Assert.That(body[4 * 64 + 38].a, Is.EqualTo(255), owners[index] + " entry point");
                    Assert.That(body.Take(64).Any(pixel => pixel.a == 255), Is.True,
                        owners[index] + " foundation must touch y0.");
                    Assert.That(overlay.Any(pixel => pixel.a == 255), Is.True);
                    for (int y = 0; y < 64; y++)
                        for (int x = 0; x < 64; x++)
                        {
                            if (y < 38) Assert.That(overlay[y * 64 + x].a, Is.Zero);
                            else Assert.That(overlay[y * 64 + x], Is.EqualTo(body[y * 64 + x]));
                        }
                }
            }
            finally
            {
                Object.DestroyImmediate(homeSheet);
                Object.DestroyImmediate(roofSheet);
            }
        }

        private static void AssertConnectedToFeet(Color32[] frame, string label)
        {
            var visited = new HashSet<int>();
            var pending = new Queue<int>();
            for (int x = 0; x < 24; x++)
                if (frame[x].a != 0) { visited.Add(x); pending.Enqueue(x); }
            while (pending.Count > 0)
            {
                int index = pending.Dequeue();
                int x = index % 24, y = index / 24;
                if (x > 0) EnqueueOpaque(frame, index - 1, visited, pending);
                if (x < 23) EnqueueOpaque(frame, index + 1, visited, pending);
                if (y > 0) EnqueueOpaque(frame, index - 24, visited, pending);
                if (y < 31) EnqueueOpaque(frame, index + 24, visited, pending);
            }
            Assert.That(visited.Count, Is.EqualTo(frame.Count(pixel => pixel.a != 0)), label + " detached pixels");
        }

        private static void EnqueueOpaque(Color32[] frame, int index, HashSet<int> visited, Queue<int> pending)
        {
            if (frame[index].a != 0 && visited.Add(index)) pending.Enqueue(index);
        }

        private static float MirroredSilhouetteOverlap(Color32[] left, Color32[] right, int rows)
        {
            int intersection = 0, union = 0;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < 24; x++)
                {
                    bool a = left[y * 24 + x].a != 0, b = right[y * 24 + 23 - x].a != 0;
                    if (a && b) intersection++;
                    if (a || b) union++;
                }
            return union == 0 ? 0f : (float)intersection / union;
        }

        private static Color32[] Cell(Color32[] sheet, int width, int height, int index, int columns,
            int frameWidth, int frameHeight)
        {
            var result = new Color32[frameWidth * frameHeight];
            int left = index % columns * frameWidth;
            int bottom = height - (index / columns + 1) * frameHeight;
            for (int y = 0; y < frameHeight; y++)
                for (int x = 0; x < frameWidth; x++)
                    result[y * frameWidth + x] = sheet[(bottom + y) * width + left + x];
            return result;
        }

        private static Texture2D ReadPng(string relativePath)
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, relativePath);
            Assert.That(File.Exists(path), Is.True, relativePath + " is missing.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.That(texture.LoadImage(File.ReadAllBytes(path), false), Is.True, relativePath);
            return texture;
        }
    }
}
