using System;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Unity.Editor
{
    public sealed class CozyTownPixelArtTexturePostprocessor : AssetPostprocessor
    {
        private const string ProductionPrefix = "Assets/CozyTown/Art/Production/";
        private const float PixelsPerUnit = 16f;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ProductionPrefix, StringComparison.Ordinal))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);

            importer.textureType = TextureImporterType.Sprite;
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
