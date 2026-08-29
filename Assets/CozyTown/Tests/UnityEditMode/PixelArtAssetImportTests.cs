using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class PixelArtAssetImportTests
    {
        private const string A0ProbePath =
            "Assets/CozyTown/Art/References/A0/a0_item_crop_carrot.png";

        [Test]
        public void A0PurePixelProbe_UsesApprovedImportContract()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(A0ProbePath);
            Assert.That(
                texture,
                Is.Not.Null,
                $"A0 pure-pixel probe was not imported from '{A0ProbePath}'.");

            var importer = AssetImporter.GetAtPath(A0ProbePath) as TextureImporter;
            Assert.That(
                importer,
                Is.Not.Null,
                $"A0 pure-pixel probe at '{A0ProbePath}' must use TextureImporter.");

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            Assert.That(texture.width, Is.EqualTo(16), "Probe width must be 16 pixels.");
            Assert.That(texture.height, Is.EqualTo(16), "Probe height must be 16 pixels.");
            Assert.That(importer.DoesSourceTextureHaveAlpha(), Is.True,
                "Probe source PNG must contain a real alpha channel.");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(A0ProbePath);
            Assert.That(sprite, Is.Not.Null, "Probe must be loadable as a Single Sprite.");
            Assert.That(sprite.rect.width, Is.EqualTo(16f));
            Assert.That(sprite.rect.height, Is.EqualTo(16f));
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(16f));
            Assert.That(sprite.pivot, Is.EqualTo(new Vector2(8f, 8f)));

            var decodedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(
                        decodedTexture,
                        File.ReadAllBytes(A0ProbePath),
                        false),
                    Is.True,
                    "Probe PNG must decode through Unity's public image API.");
                Assert.That(
                    decodedTexture.width,
                    Is.EqualTo(16),
                    "Source PNG width must be 16 pixels before Unity import settings apply.");
                Assert.That(
                    decodedTexture.height,
                    Is.EqualTo(16),
                    "Source PNG height must be 16 pixels before Unity import settings apply.");

                var opaqueColors = new HashSet<int>();
                var transparentPixelCount = 0;
                var opaquePixelCount = 0;
                var partialAlphaPixelCount = 0;

                foreach (var pixel in decodedTexture.GetPixels32())
                {
                    if (pixel.a == 0)
                    {
                        transparentPixelCount++;
                        continue;
                    }

                    if (pixel.a != 255)
                    {
                        partialAlphaPixelCount++;
                        continue;
                    }

                    opaquePixelCount++;
                    opaqueColors.Add((pixel.r << 16) | (pixel.g << 8) | pixel.b);
                }

                Assert.That(transparentPixelCount, Is.GreaterThan(0));
                Assert.That(opaquePixelCount, Is.GreaterThan(0));
                Assert.That(
                    partialAlphaPixelCount,
                    Is.Zero,
                    "Probe alpha must be binary: every pixel is either fully transparent or fully opaque.");
                Assert.That(
                    opaqueColors.Count,
                    Is.LessThanOrEqualTo(8),
                    "Probe must use no more than eight opaque palette colors.");
            }
            finally
            {
                Object.DestroyImmediate(decodedTexture);
            }
        }
    }
}
