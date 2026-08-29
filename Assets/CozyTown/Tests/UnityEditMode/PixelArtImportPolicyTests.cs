using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class PixelArtImportPolicyTests
    {
        private const string ProbeFolder =
            "Assets/CozyTown/Art/Production/__ImportPolicyTest";
        private const string ProbePath = ProbeFolder + "/pixel_import_probe.png";

        [SetUp]
        public void SetUp()
        {
            Directory.CreateDirectory(ProbeFolder);
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color32[16 * 16];
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = index == 0
                    ? new Color32(0, 0, 0, 0)
                    : new Color32(124, 184, 72, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(ProbePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(
                ProbePath,
                ImportAssetOptions.ForceSynchronousImport
                    | ImportAssetOptions.ForceUpdate);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(ProbePath);
            AssetDatabase.DeleteAsset(ProbeFolder);
        }

        [Test]
        public void ProductionTexture_ImportsWithPurePixelProfile()
        {
            var importer = AssetImporter.GetAtPath(ProbePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);

            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");

            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(textureSettings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
            Assert.That(standalone.overridden, Is.False);
        }
    }
}
