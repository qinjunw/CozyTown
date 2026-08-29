using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class PixelArtAnchorAssetTests
    {
        private const string AnchorFolder =
            "Assets/CozyTown/Art/References/A0";
        private const string ProjectAssetRoot = "Assets/CozyTown";

        private static readonly string[] AnchorPaths =
        {
            AnchorFolder + "/a0_style_town.png",
            AnchorFolder + "/a0_palette_board.png",
            AnchorFolder + "/a0_characters.png",
            AnchorFolder + "/a0_environment.png",
            AnchorFolder + "/a0_ui_items.png"
        };

        [Test]
        public void A0AnchorPack_ContainsFiveImportablePngs()
        {
            Assert.That(AnchorPaths, Has.Length.EqualTo(5));

            foreach (var path in AnchorPaths)
            {
                Assert.That(File.Exists(path), Is.True, $"Missing A0 anchor: {path}");
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<Texture2D>(path),
                    Is.Not.Null,
                    $"A0 anchor is not importable as a texture: {path}");
            }
        }

        [Test]
        public void CozyTownScenesAndPrefabs_DoNotDependOnA0ReferenceAnchors()
        {
            var assetPaths = new HashSet<string>();
            AddAssetPaths(assetPaths, "t:Scene");
            AddAssetPaths(assetPaths, "t:Prefab");

            Assert.That(
                AssetDatabase.FindAssets("t:Scene", new[] { ProjectAssetRoot }),
                Is.Not.Empty,
                "No CozyTown scene was found; the dependency check would be vacuous.");

            foreach (var assetPath in assetPaths)
            {
                var dependencies = AssetDatabase.GetDependencies(assetPath, true);
                foreach (var dependency in dependencies)
                {
                    Assert.That(
                        dependency.StartsWith(AnchorFolder),
                        Is.False,
                        $"{assetPath} references an A0-only asset: {dependency}");
                }
            }
        }

        private static void AddAssetPaths(ISet<string> assetPaths, string filter)
        {
            var guids = AssetDatabase.FindAssets(filter, new[] { ProjectAssetRoot });
            foreach (var guid in guids)
            {
                assetPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
        }
    }
}
