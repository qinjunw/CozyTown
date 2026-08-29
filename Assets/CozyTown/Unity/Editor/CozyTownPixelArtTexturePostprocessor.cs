using System;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Unity.Editor
{
    public sealed class CozyTownPixelArtTexturePostprocessor : AssetPostprocessor
    {
        private const string ProductionPrefix = "Assets/CozyTown/Art/Production/";
        private const string A0CarrotProbePath =
            "Assets/CozyTown/Art/References/A0/a0_item_crop_carrot.png";
        private const float PixelsPerUnit = 16f;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ProductionPrefix, StringComparison.Ordinal)
                && !string.Equals(
                    assetPath,
                    A0CarrotProbePath,
                    StringComparison.Ordinal))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);

            importer.textureType = TextureImporterType.Sprite;
            if (string.Equals(
                    assetPath,
                    A0CarrotProbePath,
                    StringComparison.Ordinal))
            {
                importer.spriteImportMode = SpriteImportMode.Single;
            }

            importer.spritePixelsPerUnit = PixelsPerUnit;
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
