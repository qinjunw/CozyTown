#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Time;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CozyTown.Tests.PlayMode
{
    public sealed class TownLifeSceneVisualEvidencePlayModeTests
    {
        private Scene _scene;
        private Camera _camera;
        private RenderTexture _target;
        private RenderTexture _previousTarget;
        private Texture2D _readback;

        [UnityTest]
        [Category("GraphicsRequired")]
        public IEnumerator MorningDepartureAndWork_RenderActualNpcSpritesThroughTheSceneCamera()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Requires a graphics-enabled Unity test run.");

            const string scenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                scenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(scenePath);
            GameObject[] roots = _scene.GetRootGameObjects();
            var driver = roots.Single(root => root.name == "CozyTown").GetComponent<DaytimeClockDriver>();
            driver.SetApplicationFocus(false);
            var player = roots.Single(root => root.name == "Player");
            var clockText = roots.Single(root => root.name == "Debug HUD")
                .GetComponentsInChildren<Text>(true).Single(text => text.name == "Clock Text");
            var residents = roots.Single(root => root.name == "World")
                .GetComponentsInChildren<NpcWorldResident2D>(true);
            CollectionAssert.AreEquivalent(new[] { DefaultMvpIds.Npcs.Shopkeeper, DefaultMvpIds.Npcs.Farmer,
                DefaultMvpIds.Npcs.Fisher, DefaultMvpIds.Npcs.Cook }, residents.Select(resident => resident.NpcId));
            _camera = roots.Single(root => root.name == "Main Camera").GetComponent<Camera>();
            _previousTarget = _camera.targetTexture;
            _target = new RenderTexture(960, 540, 24);
            _readback = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            _camera.targetTexture = _target;

            AdvanceMinutes(driver, 3);
            AssertSprites(residents);
            var mina = residents.Single(resident => resident.NpcId == DefaultMvpIds.Npcs.Shopkeeper);
            Assert.That(mina.Status, Is.EqualTo(TownRouteStatus.Travelling));
            yield return Capture(player, driver, new Vector2(-4, 8.5f), "town-life-day1-0603-departure.png");
            Assert.That(clockText.text, Is.EqualTo("Day 1  06:03"));
            AssertInCamera(mina);

            AdvanceMinutes(driver, 147);
            AssertSprites(residents);
            foreach (var resident in residents)
            {
                Assert.That(resident.IsHome, Is.False, resident.NpcId);
                Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Arrived), resident.NpcId);
                Assert.That(resident.TargetLocationId, Does.StartWith("work."), resident.NpcId);
            }
            yield return Capture(player, driver, new Vector2(2, -0.5f), "town-life-day1-0830-work.png");
            Assert.That(clockText.text, Is.EqualTo("Day 1  08:30"));
            foreach (var resident in residents) AssertInCamera(resident);
        }

        private static void AdvanceMinutes(DaytimeClockDriver driver, int minutes)
        {
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            for (int minute = 0; minute < minutes; minute++)
                driver.AdvanceFrame(WorldTimeProgress.EffectiveSecondsPerGameMinute);
            driver.SetApplicationFocus(false);
        }

        private static void AssertSprites(NpcWorldResident2D[] residents)
        {
            foreach (var resident in residents)
            {
                Assert.That(resident.GetComponents<CozyTownNpcSpriteAnimator>(), Has.Length.EqualTo(1), resident.NpcId);
                var renderer = resident.transform.Find("Visual").GetComponent<SpriteRenderer>();
                Assert.That(renderer.sprite, Is.Not.Null, resident.NpcId);
                Assert.That(renderer.enabled, Is.EqualTo(!resident.IsHome), resident.NpcId);
                string prefix = "npc_" + resident.NpcId.Substring("npc.".Length) + "_";
                string pose = resident.Status == TownRouteStatus.Travelling ? "walk_" : "idle_";
                Assert.That(renderer.sprite.name, Does.StartWith(prefix + pose), resident.NpcId);
            }
        }

        private IEnumerator Capture(GameObject player, DaytimeClockDriver driver, Vector2 position, string fileName)
        {
            Assert.That(driver.IsSimulationPaused, Is.True);
            var body = player.GetComponent<Rigidbody2D>();
            body.linearVelocity = Vector2.zero;
            body.position = position;
            player.transform.position = position;
            Physics2D.SyncTransforms();

            bool rendered = false;
            void Observe(ScriptableRenderContext context, Camera camera)
            {
                if (camera == _camera) rendered = true;
            }
            RenderPipelineManager.endCameraRendering += Observe;
            try
            {
                for (int frame = 0; frame < 30 && !rendered; frame++) yield return null;
                Assert.That(rendered, Is.True, "The scene camera did not render.");
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

        private void AssertInCamera(NpcWorldResident2D resident)
        {
            Vector3 viewport = _camera.WorldToViewportPoint(resident.Position + Vector2.up);
            Assert.That(viewport.z, Is.GreaterThan(0), resident.NpcId);
            Assert.That(viewport.x, Is.InRange(0f, 1f), resident.NpcId);
            Assert.That(viewport.y, Is.InRange(0f, 1f), resident.NpcId);
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
