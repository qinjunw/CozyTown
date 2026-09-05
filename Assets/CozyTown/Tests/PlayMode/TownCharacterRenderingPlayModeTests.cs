#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Time;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class TownCharacterRenderingPlayModeTests
    {
        private Scene _scene;
        private GameObject _fixture;
        private Texture2D _source;
        private Sprite _sprite;
        private Texture2D _readback;
        private RenderTexture _target;

        [UnityTest]
        [Category("GraphicsRequired")]
        public IEnumerator OverlappingNpcAndPlayer_RenderTheLowerFeetInFrontInBothDirections()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Requires a graphics-enabled Unity test run.");
            }

            const string scenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                scenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(scenePath);
            var roots = _scene.GetRootGameObjects();
            roots.Single(root => root.name == "CozyTown")
                .GetComponent<DaytimeClockDriver>().SetApplicationFocus(false);
            var playerSource = roots.Single(root => root.name == "Player")
                .transform.Find("Visual").GetComponent<SpriteRenderer>();
            var npcSource = roots.Single(root => root.name == "World")
                .GetComponentsInChildren<CozyTownNpcDebugPresenter>(true)[0]
                .transform.Find("Visual").GetComponent<SpriteRenderer>();

            _fixture = new GameObject("Character sorting render fixture");
            _source = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _source.SetPixel(0, 0, Color.white);
            _source.Apply();
            _sprite = Sprite.Create(_source, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0f), 0.5f);
            var player = CreateCharacter("Player", playerSource, Color.red);
            var npc = CreateCharacter("NPC", npcSource, Color.green);
            var cameraObject = new GameObject("Sorting camera");
            cameraObject.transform.SetParent(_fixture.transform);
            cameraObject.transform.position = new Vector3(0f, 1f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2f;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            _target = new RenderTexture(128, 128, 24);
            camera.targetTexture = _target;
            _readback = new Texture2D(128, 128, TextureFormat.RGBA32, false);

            player.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            npc.transform.localPosition = Vector3.zero;
            yield return WaitForCameraFrame(camera);
            AssertColor(ReadCenter(), Color.green, "NPC feet below the player must draw in front.");

            player.transform.localPosition = Vector3.zero;
            npc.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            yield return WaitForCameraFrame(camera);
            AssertColor(ReadCenter(), Color.red, "Player feet below the NPC must draw in front.");
        }

        private SpriteRenderer CreateCharacter(string name, SpriteRenderer source, Color color)
        {
            var character = new GameObject(name);
            character.layer = 31;
            character.transform.SetParent(_fixture.transform, false);
            var renderer = character.AddComponent<SpriteRenderer>();
            renderer.sprite = _sprite;
            renderer.spriteSortPoint = source.spriteSortPoint;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;
            renderer.color = color;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");
            Assert.That(renderer.sharedMaterial, Is.Not.Null);
            return renderer;
        }

        private static IEnumerator WaitForCameraFrame(Camera camera)
        {
            var rendered = false;
            void Observe(ScriptableRenderContext context, Camera renderedCamera)
            {
                if (renderedCamera == camera) rendered = true;
            }
            RenderPipelineManager.endCameraRendering += Observe;
            try
            {
                for (var frame = 0; frame < 30 && !rendered; frame++) yield return null;
                Assert.That(rendered, Is.True, "The sorting camera did not render.");
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= Observe;
            }
        }

        private Color ReadCenter()
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = _target;
                _readback.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                _readback.Apply();
                return _readback.GetPixel(64, 64);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static void AssertColor(Color actual, Color expected, string message)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.02f), message);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.02f), message);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.02f), message);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_fixture != null) Object.DestroyImmediate(_fixture);
            if (_sprite != null) Object.DestroyImmediate(_sprite);
            if (_source != null) Object.DestroyImmediate(_source);
            if (_readback != null) Object.DestroyImmediate(_readback);
            if (_target != null)
            {
                _target.Release();
                Object.DestroyImmediate(_target);
            }
            if (_scene.IsValid() && _scene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(_scene);
                if (unload != null) yield return unload;
            }
        }
    }
}
#endif
