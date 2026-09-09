#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Lighting;
using CozyTown.Unity.Time;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CozyTown.Tests.PlayMode
{
    public sealed class TownLightingScenePlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private Scene _scene;
        private Camera _camera;
        private RenderTexture _target;
        private RenderTexture _previousTarget;
        private Texture2D _readback;

        [UnityTest]
        public IEnumerator DevelopmentScene_HasDoorAndRoadLampsBoundToTheSharedClock()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
            GameObject[] roots = _scene.GetRootGameObjects();
            var driver = roots.Single(root => root.name == "CozyTown").GetComponent<DaytimeClockDriver>();
            driver.SetApplicationFocus(false);
            var world = roots.Single(root => root.name == "World");
            var controllers = world.GetComponentsInChildren<TownLightingController>(true);
            Assert.That(controllers, Has.Length.EqualTo(1), "The town needs one shared lighting controller.");
            var lamps = world.GetComponentsInChildren<TownLamp2D>(true);
            CollectionAssert.AreEquivalent(new[]
            {
                "Home Mina Lamp", "Home Ren Lamp", "Home Sora Lamp", "Home Eli Lamp",
                "Player Home Lamp", "Shop Door Lamp", "Coop Door Lamp", "Kitchen Door Lamp",
                "West Main Road Lamp", "Shop Main Road Lamp", "East Main Road Lamp",
                "West Lane South Lamp", "West Lane North Lamp", "East Lane South Lamp", "East Lane North Lamp",
                "Residential West Lamp", "Residential Centre Lamp", "Residential East Lamp"
            }, lamps.Select(lamp => lamp.name));
            var lights = world.GetComponentsInChildren<Light2D>(true);
            Assert.That(lights.Count(light => light.lightType == Light2D.LightType.Global), Is.EqualTo(1));
            Assert.That(lights.Count(light => light.lightType == Light2D.LightType.Point), Is.EqualTo(18));
            var ambient = lights.Single(light => light.lightType == Light2D.LightType.Global);
            Color morningColor = ambient.color;

            foreach (var lamp in lamps)
            {
                Assert.That(lamp.GetComponentsInChildren<Collider2D>(true), Is.Empty, lamp.name);
                Assert.That(lamp.GetComponentsInChildren<MonoBehaviour>(true).OfType<IInteractable>(),
                    Is.Empty, lamp.name);
                var sprites = lamp.GetComponentsInChildren<SpriteRenderer>(true);
                Assert.That(sprites, Has.Length.EqualTo(2), lamp.name);
                foreach (var sprite in sprites)
                {
                    Assert.That(sprite.sprite, Is.Not.Null, lamp.name);
                    Assert.That(sprite.sprite.pixelsPerUnit, Is.EqualTo(16), lamp.name);
                    Assert.That(AssetDatabase.GetAssetPath(sprite.sprite),
                        Does.StartWith("Assets/CozyTown/Art/Production/Props/"), lamp.name);
                }
                Assert.That(lamp.GetComponentInChildren<Light2D>(true).enabled, Is.False, lamp.name);
                Assert.That(sprites.Single(sprite => sprite.name == "Glass Glow").enabled, Is.False, lamp.name);
            }

            var hud = roots.Single(root => root.name == "Debug HUD");
            var canvases = hud.GetComponentsInChildren<Canvas>(true);
            Assert.That(canvases, Is.Not.Empty);
            foreach (var canvas in canvases)
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay), canvas.name);
            Assert.That(hud.GetComponentsInChildren<Light2D>(true), Is.Empty);
            var graphics = hud.GetComponentsInChildren<Graphic>(true);
            var hudColors = graphics.Select(graphic => graphic.color).ToArray();

            AdvanceMinutes(driver, 750);
            Assert.That(driver.IsSimulationPaused, Is.True);
            yield return null;

            var clockText = hud.GetComponentsInChildren<Text>(true).Single(text => text.name == "Clock Text");
            Assert.That(clockText.text, Is.EqualTo("Day 1  18:30"));
            Assert.That(ambient.color, Is.Not.EqualTo(morningColor));
            foreach (var lamp in lamps)
            {
                var illumination = lamp.GetComponentInChildren<Light2D>(true);
                Assert.That(illumination.enabled, Is.True, lamp.name);
                Assert.That(illumination.intensity, Is.InRange(0.1f, 0.75f), lamp.name);
                Assert.That(lamp.GetComponentsInChildren<SpriteRenderer>(true)
                    .Single(sprite => sprite.name == "Glass Glow").enabled, Is.True, lamp.name);
            }
            CollectionAssert.AreEqual(hudColors, graphics.Select(graphic => graphic.color));
        }

        [UnityTest]
        [Category("GraphicsRequired")]
        public IEnumerator DevelopmentScene_RendersSixSkyColorsAndIlluminatedRoadSurfaces()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Requires a graphics-enabled Unity test run.");

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
            GameObject[] roots = _scene.GetRootGameObjects();
            var driver = roots.Single(root => root.name == "CozyTown").GetComponent<DaytimeClockDriver>();
            var world = roots.Single(root => root.name == "World");
            var controller = world.GetComponentInChildren<TownLightingController>();
            var lamps = world.GetComponentsInChildren<TownLamp2D>(true);
            var services = CozyTownCompositionRoot.CreateDefault();
            controller.Bind(services.WorldTimeFlow);
            driver.Bind(services.DaytimeClock);
            driver.SetApplicationFocus(false);

            var player = roots.Single(root => root.name == "Player");
            var body = player.GetComponent<Rigidbody2D>();
            body.linearVelocity = Vector2.zero;
            body.position = new Vector2(2f, -0.5f);
            player.transform.position = body.position;
            Physics2D.SyncTransforms();
            _camera = roots.Single(root => root.name == "Main Camera").GetComponent<Camera>();
            _previousTarget = _camera.targetTexture;
            _target = new RenderTexture(960, 540, 24);
            _readback = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            _camera.targetTexture = _target;

            int[] minutes = { 540, 900, 1080, 1200, 120, 330 };
            string[] names = { "0900-day", "1500-afternoon", "1800-dusk", "2000-night", "0200-deep-night", "0530-dawn" };
            var imageColors = new Vector3[minutes.Length];
            float illuminatedRoad = 0f;
            float unlitRoad = 0f;
            for (int index = 0; index < minutes.Length; index++)
            {
                AdvanceToMinute(services, minutes[index]);
                Assert.That(driver.IsSimulationPaused, Is.True);
                yield return Capture("lighting-" + names[index] + ".png");
                imageColors[index] = MeanImageColor();
                if (minutes[index] == 540)
                    Assert.That(lamps.All(lamp => !lamp.GetComponentInChildren<Light2D>(true).enabled), Is.True);
                if (minutes[index] == 120)
                {
                    illuminatedRoad = MeanRoadLuminance();
                    foreach (var lamp in lamps) lamp.Apply(0f);
                    yield return Capture("lighting-0200-deep-night-lamps-off.png");
                    unlitRoad = MeanRoadLuminance();
                }
            }

            float day = Luminance(imageColors[0]);
            float deepNight = Luminance(imageColors[4]);
            Debug.Log($"Lighting camera luminance: day={day:F4}, deep-night={deepNight:F4}; "
                + $"same-night road with lamps={illuminatedRoad:F4}, lamps off={unlitRoad:F4}.");
            Assert.That(day, Is.GreaterThan(deepNight * 1.25f), "The rendered town must visibly darken at night.");
            Assert.That(illuminatedRoad - unlitRoad, Is.GreaterThan(0.025f),
                "Night lamps must brighten the road beside a pole, not only their own glass sprite.");
            for (int first = 0; first < imageColors.Length; first++)
            for (int second = first + 1; second < imageColors.Length; second++)
                Assert.That(Vector3.Distance(imageColors[first], imageColors[second]), Is.GreaterThan(0.01f),
                    names[first] + " and " + names[second] + " must render different town colors.");

            AdvanceToMinute(services, 360);
            foreach (var lamp in lamps)
            {
                Assert.That(lamp.GetComponentInChildren<Light2D>(true).enabled, Is.False, lamp.name);
                Assert.That(lamp.GetComponentsInChildren<SpriteRenderer>(true)
                    .Single(sprite => sprite.name == "Glass Glow").enabled, Is.False, lamp.name);
            }

            AdvanceToMinute(services, 120);
            Assert.That(driver.IsSimulationPaused, Is.True);
            body.linearVelocity = Vector2.zero;
            body.position = new Vector2(-4f, 8.5f);
            player.transform.position = body.position;
            Physics2D.SyncTransforms();
            yield return Capture("lighting-0200-residential.png");

            body.linearVelocity = Vector2.zero;
            body.position = new Vector2(7f, 8.5f);
            player.transform.position = body.position;
            Physics2D.SyncTransforms();
            yield return Capture("lighting-0200-residential-east.png");
        }

        private static void AdvanceToMinute(CozyTownServices services, int minuteOfDay)
        {
            int minutes = (minuteOfDay - services.Time.Current.MinuteOfDay + 1440) % 1440;
            while (minutes >= 60)
            {
                int sleepMinutes = Mathf.Min(720, minutes / 60 * 60);
                Assert.That(services.Sleep.SleepForMinutes(sleepMinutes).IsSuccess, Is.True);
                minutes -= sleepMinutes;
            }
            if (minutes > 0)
                Assert.That(services.DaytimeClock.AdvanceElapsed(
                    minutes * WorldTimeProgress.EffectiveSecondsPerGameMinute).IsSuccess, Is.True);
            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(minuteOfDay));
        }

        private IEnumerator Capture(string fileName)
        {
            int renderedFrames = 0;
            void Observe(ScriptableRenderContext context, Camera camera)
            {
                if (camera == _camera) renderedFrames++;
            }
            RenderPipelineManager.endCameraRendering += Observe;
            try
            {
                for (int frame = 0; frame < 30 && renderedFrames < 2; frame++) yield return null;
                Assert.That(renderedFrames, Is.GreaterThanOrEqualTo(2), "The formal scene camera did not render.");
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= Observe;
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = _target;
                _readback.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                _readback.Apply();
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, fileName), _readback.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private Vector3 MeanImageColor()
        {
            Color32[] pixels = _readback.GetPixels32();
            double red = 0;
            double green = 0;
            double blue = 0;
            foreach (Color32 pixel in pixels)
            {
                red += pixel.r;
                green += pixel.g;
                blue += pixel.b;
            }
            double scale = 1d / (255d * pixels.Length);
            return new Vector3((float)(red * scale), (float)(green * scale), (float)(blue * scale));
        }

        private float MeanRoadLuminance()
        {
            // This road patch is beside the shop's pole and excludes its pole and glass sprites.
            Vector3 bottomLeft = _camera.WorldToViewportPoint(new Vector3(-4f, 0.3125f, 0f));
            Vector3 topRight = _camera.WorldToViewportPoint(new Vector3(-3.5f, 0.5625f, 0f));
            Assert.That(bottomLeft.x, Is.InRange(0f, 1f));
            Assert.That(bottomLeft.y, Is.InRange(0f, 1f));
            Assert.That(topRight.x, Is.InRange(0f, 1f));
            Assert.That(topRight.y, Is.InRange(0f, 1f));
            int x = Mathf.FloorToInt(bottomLeft.x * _readback.width);
            int y = Mathf.FloorToInt(bottomLeft.y * _readback.height);
            int width = Mathf.CeilToInt(topRight.x * _readback.width) - x;
            int height = Mathf.CeilToInt(topRight.y * _readback.height) - y;
            Color[] pixels = _readback.GetPixels(x, y, width, height);
            return pixels.Average(pixel => Luminance(new Vector3(pixel.r, pixel.g, pixel.b)));
        }

        private static float Luminance(Vector3 color)
        {
            return color.x * 0.2126f + color.y * 0.7152f + color.z * 0.0722f;
        }

        private static void AdvanceMinutes(DaytimeClockDriver driver, int minutes)
        {
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            for (int minute = 0; minute < minutes; minute++)
                driver.AdvanceFrame(WorldTimeProgress.EffectiveSecondsPerGameMinute);
            driver.SetApplicationFocus(false);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_camera != null) _camera.targetTexture = _previousTarget;
            if (_readback != null) Object.DestroyImmediate(_readback);
            if (_target != null)
            {
                _target.Release();
                Object.DestroyImmediate(_target);
            }
            if (_scene.IsValid() && _scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_scene);
        }
    }
}
#endif
