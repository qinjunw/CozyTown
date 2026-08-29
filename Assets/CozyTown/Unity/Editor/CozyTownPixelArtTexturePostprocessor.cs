using System;
using UnityEditor;

namespace CozyTown.Unity.Editor
{
    public sealed class CozyTownPixelArtTexturePostprocessor : AssetPostprocessor
    {
        private const string ProductionPrefix = "Assets/CozyTown/Art/Production/";
        private const string A0CarrotProbePath =
            "Assets/CozyTown/Art/References/A0/a0_item_crop_carrot.png";
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
            CozyTownPixelArtImportProfile.Apply(importer);
            if (string.Equals(
                    assetPath,
                    A0CarrotProbePath,
                    StringComparison.Ordinal))
            {
                importer.spriteImportMode = SpriteImportMode.Single;
            }

        }
    }
}
