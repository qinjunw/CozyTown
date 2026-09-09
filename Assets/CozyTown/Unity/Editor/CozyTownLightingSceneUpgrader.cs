using System;
using System.IO;
using System.Linq;
using CozyTown.Unity.Core;
using CozyTown.Unity.Lighting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownLightingSceneUpgrader
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private const string ProfilePath = "Assets/CozyTown/Content/DefaultTownLighting.asset";
        private const string LampArtPath = "Assets/CozyTown/Art/Production/Props/prop_town_lamp_16x32.png";
        private const string LampSourcePath = "ArtSource/Authored/L1/prop_town_lamp.pixels";
        private const string GlowSourcePath = "ArtSource/Authored/L1/prop_town_lamp_glow.pixels";
        private const string MaterialRoot = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/";

        private static readonly LampPlacement[] Placements =
        {
            new LampPlacement("Home Mina Lamp", -8.3125f, 9.5f),
            new LampPlacement("Home Ren Lamp", -1.3125f, 9.5f),
            new LampPlacement("Home Sora Lamp", 5.6875f, 9.5f),
            new LampPlacement("Home Eli Lamp", 12.6875f, 9.5f),
            new LampPlacement("Player Home Lamp", -4.8125f, -4.5f),
            new LampPlacement("Shop Door Lamp", -9.1875f, 0.625f),
            new LampPlacement("Coop Door Lamp", 2.1875f, 1.25f),
            new LampPlacement("Kitchen Door Lamp", 8.6875f, 0.625f),
            new LampPlacement("West Main Road Lamp", -11f, -0.25f),
            new LampPlacement("Shop Main Road Lamp", -4.1875f, -0.25f),
            new LampPlacement("East Main Road Lamp", 11f, -0.25f),
            new LampPlacement("West Lane South Lamp", -3.4375f, 3.625f),
            new LampPlacement("West Lane North Lamp", -3.4375f, 6.875f),
            new LampPlacement("East Lane South Lamp", 4.375f, 3.625f),
            new LampPlacement("East Lane North Lamp", 4.375f, 6.875f),
            new LampPlacement("Residential West Lamp", -6.6875f, 7.625f),
            new LampPlacement("Residential Centre Lamp", 0.375f, 7.625f),
            new LampPlacement("Residential East Lamp", 8f, 7.625f)
        };

        [MenuItem("CozyTown/Art/Build Town Lighting Pixel Art")]
        public static void BuildLightingArt()
        {
            CozyTownPixelArtBatchCompiler.BuildAll(new[]
            {
                new PixelArtBatchDefinition(
                    LampSourcePath,
                    LampArtPath,
                    "ArtSource/Previews/L1/prop_town_lamp_16x32_4x.png",
                    2, 1, 16, 32, 0,
                    PixelArtPivotKind.BottomCenter,
                    PixelArtBackgroundMode.Alpha,
                    CozyTownPixelArtPalettes.WarmRural32,
                    new[] { "prop_town_lamp", "prop_town_lamp_glow" },
                    authoredCellSourcePaths: new[] { LampSourcePath, GlowSourcePath })
            });
        }

        [MenuItem("CozyTown/Upgrade Development Scene Lighting")]
        public static void UpgradeDevelopmentSceneLighting()
        {
            if (!File.Exists(ScenePath))
            {
                throw new FileNotFoundException("Development scene was not found.", ScenePath);
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool closeWhenFinished = !scene.IsValid() || !scene.isLoaded;
            if (closeWhenFinished)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                UpgradeLighting(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Updated development scene lighting at {ScenePath}.");
            }
            finally
            {
                if (closeWhenFinished && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        public static void UpgradeLighting(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException("Lighting upgrades require a loaded scene.", nameof(scene));
            }

            var world = RequireRoot(scene, "World");
            var bootstrap = RequireRoot(scene, "CozyTown").GetComponent<CozyTownBootstrap>()
                ?? throw new InvalidOperationException("Development scene is missing CozyTownBootstrap.");
            var lampSprite = LoadLampSprite("prop_town_lamp");
            var glowSprite = LoadLampSprite("prop_town_lamp_glow");
            var lampMaterial = LoadMaterial("Sprite-Lit-Default.mat");
            var glowMaterial = LoadMaterial("Sprite-Unlit-Default.mat");
            var profile = LoadOrCreateProfile();
            var preview = profile.Evaluate(360);

            var lightingRoot = GetOrCreateChild(world.transform, "Town Lighting");
            var ambientObject = GetOrCreateChild(lightingRoot, "Ambient Light");
            var ambient = GetOrAdd<Light2D>(ambientObject.gameObject);
            ambient.lightType = Light2D.LightType.Global;
            ambient.blendStyleIndex = 0;
            ambient.targetSortingLayers = new[] { SortingLayer.NameToID("Default") };
            ambient.color = preview.AmbientColor;
            ambient.intensity = preview.AmbientIntensity;
            ambient.enabled = true;

            var lamps = new TownLamp2D[Placements.Length];
            for (int index = 0; index < Placements.Length; index++)
            {
                lamps[index] = ConfigureLamp(lightingRoot, Placements[index],
                    lampSprite, glowSprite, lampMaterial, glowMaterial);
                lamps[index].Apply(preview.LampStrength);
            }

            var controller = GetOrAdd<TownLightingController>(lightingRoot.gameObject);
            controller.Configure(profile, ambient, lamps);
            bootstrap.RegisterTownLighting(controller);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(bootstrap);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static TownLamp2D ConfigureLamp(Transform parent, LampPlacement placement,
            Sprite lampSprite, Sprite glowSprite, Material lampMaterial, Material glowMaterial)
        {
            var lampRoot = GetOrCreateChild(parent, placement.Name);
            lampRoot.position = placement.Position;
            lampRoot.localRotation = Quaternion.identity;
            lampRoot.localScale = Vector3.one;

            // The group uses the pole's foot position for the same Y sorting as the characters.
            var sorting = GetOrAdd<SortingGroup>(lampRoot.gameObject);
            sorting.sortingLayerID = SortingLayer.NameToID("Default");
            sorting.sortingOrder = 20;
            ConfigureSprite(lampRoot, "Pole", lampSprite, lampMaterial, 0);
            var glow = ConfigureSprite(lampRoot, "Glass Glow", glowSprite, glowMaterial, 1);

            var lightObject = GetOrCreateChild(lampRoot, "Illumination");
            lightObject.localPosition = new Vector3(0f, 1.5f, 0f);
            var illumination = GetOrAdd<Light2D>(lightObject.gameObject);
            illumination.lightType = Light2D.LightType.Point;
            illumination.blendStyleIndex = 0;
            illumination.targetSortingLayers = new[] { SortingLayer.NameToID("Default") };
            illumination.color = new Color(1f, 0.78f, 0.48f);
            illumination.pointLightInnerRadius = 0.25f;
            illumination.pointLightOuterRadius = 3f;
            illumination.pointLightInnerAngle = 360f;
            illumination.pointLightOuterAngle = 360f;
            illumination.falloffIntensity = 0.65f;

            var lamp = GetOrAdd<TownLamp2D>(lampRoot.gameObject);
            lamp.Configure(illumination, glow, 0.85f);
            return lamp;
        }

        private static SpriteRenderer ConfigureSprite(Transform parent, string name,
            Sprite sprite, Material material, int sortingOrder)
        {
            var child = GetOrCreateChild(parent, name);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            var renderer = GetOrAdd<SpriteRenderer>(child.gameObject);
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.color = Color.white;
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static TownLightingProfile LoadOrCreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<TownLightingProfile>(ProfilePath);
            if (profile != null)
            {
                return profile;
            }

            profile = ScriptableObject.CreateInstance<TownLightingProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            return profile;
        }

        private static Sprite LoadLampSprite(string name)
        {
            return AssetDatabase.LoadAllAssetsAtPath(LampArtPath).OfType<Sprite>()
                .FirstOrDefault(sprite => sprite.name == name)
                ?? throw new InvalidOperationException(
                    $"Lamp sprite '{name}' is missing. Run CozyTown/Art/Build Town Lighting Pixel Art first.");
        }

        private static Material LoadMaterial(string name)
        {
            string path = MaterialRoot + name;
            return AssetDatabase.LoadAssetAtPath<Material>(path)
                ?? throw new FileNotFoundException("URP 2D sprite material was not found.", path);
        }

        private static Transform GetOrCreateChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
            {
                return child;
            }

            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            return Array.Find(scene.GetRootGameObjects(), root => root.name == name)
                ?? throw new InvalidOperationException($"Development scene is missing root '{name}'.");
        }

        private readonly struct LampPlacement
        {
            public string Name { get; }
            public Vector3 Position { get; }

            public LampPlacement(string name, float x, float y)
            {
                Name = name;
                Position = new Vector3(x, y, 0f);
            }
        }
    }
}
