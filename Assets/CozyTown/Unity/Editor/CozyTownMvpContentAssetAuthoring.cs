using System;
using System.IO;
using CozyTown.Unity.Content;
using CozyTown.Unity.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownMvpContentAssetAuthoring
    {
        public const string DefaultAssetPath =
            "Assets/CozyTown/Content/DefaultMvpContent.asset";
        private const string ContentFolder = "Assets/CozyTown/Content";
        private const string DevelopmentScenePath =
            "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        [MenuItem("CozyTown/Content/Ensure Default MVP Content Asset")]
        public static void EnsureDefaultAssetAndWireDevelopmentScene()
        {
            CozyTownMvpContentAsset asset = EnsureDefaultAsset();
            if (!File.Exists(DevelopmentScenePath))
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"Created default MVP content asset at {DefaultAssetPath}.");
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(DevelopmentScenePath);
            bool closeWhenFinished = !scene.IsValid() || !scene.isLoaded;
            if (closeWhenFinished)
            {
                scene = EditorSceneManager.OpenScene(
                    DevelopmentScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                CozyTownBootstrap bootstrap = FindBootstrap(scene);
                bootstrap.ConfigureContentAsset(asset);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, DevelopmentScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    $"Ensured default MVP content asset and wired {DevelopmentScenePath}.");
            }
            finally
            {
                if (closeWhenFinished && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        internal static CozyTownMvpContentAsset EnsureDefaultAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<CozyTownMvpContentAsset>(
                DefaultAssetPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureContentFolder();
            CozyTownMvpContentAsset asset =
                CozyTownMvpContentAsset.CreateDefaultForEditor();
            AssetDatabase.CreateAsset(asset, DefaultAssetPath);
            return asset;
        }

        private static CozyTownBootstrap FindBootstrap(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                CozyTownBootstrap bootstrap = root.GetComponent<CozyTownBootstrap>();
                if (bootstrap != null)
                {
                    return bootstrap;
                }
            }

            throw new InvalidOperationException(
                "Development scene is missing CozyTownBootstrap.");
        }

        private static void EnsureContentFolder()
        {
            if (!AssetDatabase.IsValidFolder(ContentFolder))
            {
                AssetDatabase.CreateFolder("Assets/CozyTown", "Content");
            }
        }
    }
}
