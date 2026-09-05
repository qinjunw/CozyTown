#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Unity.CameraView;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class TownCameraPlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        private Scene _scene;
        private GameObject _cameraObject;
        private GameObject _targetObject;
        private readonly List<RenderTexture> _renderTextures = new List<RenderTexture>();

        [UnityTest]
        public IEnumerator DevelopmentScene_CameraFollowsPlayerIntoResidentialStreetWithoutZoomingOut()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
            yield return null;
            yield return null;

            var roots = _scene.GetRootGameObjects();
            var camera = roots.Single(root => root.name == "Main Camera").GetComponent<Camera>();
            var player = roots.Single(root => root.name == "Player");
            player.GetComponent<PlayerMovement2D>().enabled = false;
            player.GetComponent<Rigidbody2D>().simulated = false;
            var originalSize = camera.orthographicSize;
            var originalZ = camera.transform.position.z;

            player.transform.position = new Vector3(0f, 8f, 0f);
            yield return null;
            yield return null;

            Assert.That(camera.transform.position.y, Is.GreaterThan(1f),
                "The residential street must move into view as the player travels north.");
            Assert.That(camera.orthographicSize, Is.EqualTo(originalSize).Within(0.001f));
            Assert.That(camera.transform.position.z, Is.EqualTo(originalZ));
        }

        [UnityTest]
        public IEnumerator FollowCamera_KeepsVisibleViewportInsideTownAtEveryEdgeAndAfterAspectChange()
        {
            _cameraObject = new GameObject("Follow Camera");
            _cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2f;
            camera.aspect = 1.5f;
            _targetObject = new GameObject("Camera Target");
            _cameraObject.AddComponent<CozyTownFollowCamera2D>().Configure(
                _targetObject.transform,
                new Rect(-16f, -6f, 32f, 22f));

            var targets = new[]
            {
                new Vector3(100f, 100f, 0f),
                new Vector3(-100f, 100f, 0f),
                new Vector3(-100f, -100f, 0f),
                new Vector3(100f, -100f, 0f)
            };
            var expectedCenters = new[]
            {
                new Vector3(13f, 14f, -10f),
                new Vector3(-13f, 14f, -10f),
                new Vector3(-13f, -4f, -10f),
                new Vector3(13f, -4f, -10f)
            };

            for (var corner = 0; corner < targets.Length; corner++)
            {
                _targetObject.transform.position = targets[corner];
                yield return null;
                yield return null;

                Assert.That(camera.transform.position, Is.EqualTo(expectedCenters[corner]));
                AssertViewportInsideTown(camera);
            }

            camera.aspect = 2.5f;
            _targetObject.transform.position = targets[0];
            yield return null;
            yield return null;

            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(11f, 14f, -10f)));
            AssertViewportInsideTown(camera);
        }

        [UnityTest]
        public IEnumerator FollowCamera_UsesNativePixelViewportWhileWindowAspectChanges()
        {
            _cameraObject = new GameObject("Pixel Follow Camera");
            _cameraObject.SetActive(false);
            _cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.625f;
            camera.aspect = 16f / 9f;
            var pixelPerfect = _cameraObject.AddComponent<PixelPerfectCamera>();
            pixelPerfect.assetsPPU = 16;
            pixelPerfect.refResolutionX = 320;
            pixelPerfect.refResolutionY = 180;
            pixelPerfect.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
            _targetObject = new GameObject("Camera Target");
            _targetObject.transform.position = new Vector3(100f, 100f, 0f);
            _cameraObject.AddComponent<CozyTownFollowCamera2D>().Configure(
                _targetObject.transform,
                new Rect(-16f, -6f, 32f, 22f));
            _cameraObject.SetActive(true);
            yield return null;
            yield return null;

            // The screen aspect can change before Pixel Perfect updates its final windowboxed viewport.
            foreach (var temporaryAspect in new[] { 4f / 3f, 21f / 9f, 16f / 9f })
            {
                camera.aspect = temporaryAspect;
                yield return null;
                yield return null;

                Assert.That(camera.transform.position,
                    Is.EqualTo(new Vector3(6f, 10.375f, -10f)),
                    "The 320 x 180 native viewport must remain inside the town during a window resize.");
                Assert.That(camera.orthographicSize, Is.EqualTo(5.625f).Within(0.001f));
            }
        }

        [UnityTest]
        public IEnumerator FollowCamera_CentersOnlyTheAxesWhoseViewportExceedsTheTown()
        {
            _cameraObject = new GameObject("Wide Follow Camera");
            _cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 4f;
            _targetObject = new GameObject("Camera Target");
            _targetObject.transform.position = new Vector3(100f, 100f, 0f);
            _cameraObject.AddComponent<CozyTownFollowCamera2D>().Configure(
                _targetObject.transform,
                new Rect(-16f, -6f, 32f, 22f));
            yield return null;
            yield return null;

            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0f, 11f, -10f)),
                "A view wider than the town must center horizontally while still following vertically.");

            camera.orthographicSize = 20f;
            yield return null;
            yield return null;

            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0f, 5f, -10f)),
                "A view larger than the town on both axes must remain centered on the town.");
        }

        [UnityTest]
        [Category("GraphicsRequired")]
        public IEnumerator FollowCamera_FirstRenderedViewportStaysInsideTownAfterOutputResize()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Requires a graphics-enabled Unity test run.");
            }

            _cameraObject = new GameObject("Rendered Pixel Follow Camera");
            _cameraObject.SetActive(false);
            _cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.625f;
            var pixelPerfect = _cameraObject.AddComponent<PixelPerfectCamera>();
            pixelPerfect.assetsPPU = 16;
            pixelPerfect.refResolutionX = 320;
            pixelPerfect.refResolutionY = 180;
            pixelPerfect.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
            _targetObject = new GameObject("Camera Target");
            _targetObject.transform.position = new Vector3(100f, 100f, 0f);
            _cameraObject.AddComponent<CozyTownFollowCamera2D>().Configure(
                _targetObject.transform,
                new Rect(-16f, -6f, 32f, 22f));

            var rendered = false;
            Vector3 renderedBottomLeft = default;
            Vector3 renderedTopRight = default;
            void ObserveFirstRenderedFrame(ScriptableRenderContext context, Camera renderedCamera)
            {
                if (renderedCamera != camera || rendered)
                {
                    return;
                }

                renderedBottomLeft = camera.ViewportToWorldPoint(new Vector3(0f, 0f, 10f));
                renderedTopRight = camera.ViewportToWorldPoint(new Vector3(1f, 1f, 10f));
                rendered = true;
            }

            RenderPipelineManager.endCameraRendering += ObserveFirstRenderedFrame;
            try
            {
                foreach (var size in new[]
                {
                    new Vector2Int(640, 360),
                    new Vector2Int(640, 480),
                    new Vector2Int(840, 360)
                })
                {
                    var texture = new RenderTexture(size.x, size.y, 24);
                    _renderTextures.Add(texture);
                    camera.targetTexture = texture;
                    rendered = false;
                    _cameraObject.SetActive(true);
                    for (var frame = 0; frame < 30 && !rendered; frame++)
                    {
                        yield return null;
                    }

                    Assert.That(rendered, Is.True, $"No frame rendered at output size {size}.");
                    Assert.That(renderedBottomLeft.x, Is.EqualTo(-4f).Within(0.001f));
                    Assert.That(renderedBottomLeft.y, Is.EqualTo(4.75f).Within(0.001f));
                    Assert.That(renderedTopRight.x, Is.EqualTo(16f).Within(0.001f));
                    Assert.That(renderedTopRight.y, Is.EqualTo(16f).Within(0.001f));
                    Assert.That(pixelPerfect.pixelRatio, Is.EqualTo(2));
                }
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= ObserveFirstRenderedFrame;
            }
        }

        [UnityTearDown]
        public IEnumerator UnloadScene()
        {
            if (_cameraObject != null)
            {
                Object.Destroy(_cameraObject);
            }

            if (_targetObject != null)
            {
                Object.Destroy(_targetObject);
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }

            yield return null;
            foreach (var texture in _renderTextures)
            {
                texture.Release();
                Object.Destroy(texture);
            }

            _renderTextures.Clear();
        }

        private static void AssertViewportInsideTown(Camera camera)
        {
            var bottomLeft = camera.ViewportToWorldPoint(new Vector3(0f, 0f, 10f));
            var topRight = camera.ViewportToWorldPoint(new Vector3(1f, 1f, 10f));
            Assert.That(bottomLeft.x, Is.GreaterThanOrEqualTo(-16.001f));
            Assert.That(bottomLeft.y, Is.GreaterThanOrEqualTo(-6.001f));
            Assert.That(topRight.x, Is.LessThanOrEqualTo(16.001f));
            Assert.That(topRight.y, Is.LessThanOrEqualTo(16.001f));
        }
    }
}
#endif
