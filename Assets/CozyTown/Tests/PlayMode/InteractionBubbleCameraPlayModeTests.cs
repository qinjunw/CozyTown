using System.Collections;
using System.Collections.Generic;
using CozyTown.Unity.CameraView;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Interaction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CozyTown.Tests.PlayMode
{
    public sealed class InteractionBubbleCameraPlayModeTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private RenderTexture _targetTexture;

        [UnityTest]
        [Category("GraphicsRequired")]
        public IEnumerator InteractionBubble_StaysOnItsAnchorInTheFirstRenderedFrameAfterCameraMovement()
        {
            return VerifyFirstRenderedMovementFrame(false, false);
        }

        [UnityTest]
        [Category("GraphicsRequired")]
        public IEnumerator InteractionBubble_UsesTheCurrentPixelProjectionAfterPixelPerfectCameraRestarts()
        {
            return VerifyFirstRenderedMovementFrame(true, false);
        }

        [UnityTest]
        [Category("GraphicsRequired")]
        public IEnumerator InteractionBubble_HidesAndResumesCurrentCameraProjectionAfterViewRestarts()
        {
            return VerifyFirstRenderedMovementFrame(false, true);
        }

        [UnityTest]
        public IEnumerator InteractionBubble_HidesWhenResidentHeadAnchorIsUnavailable()
        {
            return VerifyFirstRenderedMovementFrame(false, false, true);
        }

        private IEnumerator VerifyFirstRenderedMovementFrame(bool restartPixelPerfect, bool restartView,
            bool hideAnchor = false)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Requires a graphics-enabled Unity test run.");
            }

            var actor = CreateObject("Actor", false);
            var input = actor.AddComponent<PlayModePlayerInputSource>();
            var probe = actor.AddComponent<InteractionProbe2D>();
            var interactor = actor.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);

            var targetObject = CreateObject("NPC Target");
            targetObject.transform.position = new Vector2(0.3f, 0f);
            var collider = targetObject.AddComponent<CircleCollider2D>();
            collider.radius = 0.04f;
            collider.isTrigger = true;
            var target = targetObject.AddComponent<TownInteractionPoint2D>();
            target.Configure(TownInteractionKind.Npc, "Talk");
            var anchor = CreateObject("Prompt Anchor").transform;
            anchor.SetParent(targetObject.transform, false);
            anchor.localPosition = new Vector3(0f, 0.8f, 0f);
            target.ConfigurePromptAnchor(anchor);

            var cameraObject = CreateObject("Following World Camera", false);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var worldCamera = cameraObject.AddComponent<Camera>();
            worldCamera.orthographic = true;
            worldCamera.orthographicSize = 5.625f;
            _targetTexture = new RenderTexture(640, 360, 24);
            worldCamera.targetTexture = _targetTexture;
            var pixelPerfect = cameraObject.AddComponent<PixelPerfectCamera>();
            pixelPerfect.assetsPPU = 16;
            pixelPerfect.refResolutionX = 320;
            pixelPerfect.refResolutionY = 180;
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
            pixelPerfect.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            cameraObject.AddComponent<CozyTownFollowCamera2D>()
                .Configure(actor.transform, new Rect(-16f, -6f, 32f, 22f));

            var uiRoot = CreateUiObject("Interaction Bubble UI", false);
            uiRoot.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var bubbleObject = CreateUiObject("Bubble");
            bubbleObject.transform.SetParent(uiRoot.transform, false);
            var bubbleRect = bubbleObject.GetComponent<RectTransform>();
            var keyObject = CreateUiObject("Key Text");
            keyObject.transform.SetParent(bubbleObject.transform, false);
            var view = uiRoot.AddComponent<CozyTownInteractionBubbleView>();
            view.Configure(interactor, worldCamera);
            view.ConfigureUi(bubbleRect, keyObject.AddComponent<Text>());

            actor.SetActive(true);
            cameraObject.SetActive(true);
            uiRoot.SetActive(true);
            Physics2D.SyncTransforms();
            yield return null;
            yield return null;
            Assert.That(interactor.CurrentPromptAnchor, Is.SameAs(anchor));
            Assert.That(view.IsVisible, Is.True);

            if (hideAnchor)
            {
                anchor.gameObject.SetActive(false);
                view.Refresh();
                Assert.That(view.IsVisible, Is.False);
                Assert.That(bubbleObject.activeSelf, Is.False);
                yield break;
            }

            if (restartPixelPerfect)
            {
                pixelPerfect.enabled = false;
                pixelPerfect.enabled = true;
            }

            if (restartView)
            {
                view.enabled = false;
                Assert.That(view.IsVisible, Is.False);
                Assert.That(bubbleObject.activeSelf, Is.False);
                view.enabled = true;
                Assert.That(view.IsVisible, Is.True);
                Assert.That(view.CurrentAnchor, Is.SameAs(anchor));
            }

            var rendered = false;
            Vector2 expectedScreenPosition = default;
            Vector2 actualScreenPosition = default;
            Vector3 renderedCameraPosition = default;
            void ObserveRenderedFrame(ScriptableRenderContext context, Camera renderedCamera)
            {
                if (renderedCamera != worldCamera || rendered)
                {
                    return;
                }

                expectedScreenPosition = worldCamera.WorldToScreenPoint(anchor.position);
                actualScreenPosition = bubbleRect.position;
                renderedCameraPosition = worldCamera.transform.position;
                rendered = true;
            }

            RenderPipelineManager.endCameraRendering += ObserveRenderedFrame;
            try
            {
                float nextActorX = restartPixelPerfect || restartView ? 0.53f : 0.5f;
                actor.transform.position = new Vector3(nextActorX, 0f, 0f);
                Physics2D.SyncTransforms();
                for (var frame = 0; frame < 30 && !rendered; frame++)
                {
                    yield return null;
                }

                Assert.That(rendered, Is.True, "The world camera did not render the movement frame.");
                Assert.That(renderedCameraPosition.x, Is.EqualTo(nextActorX).Within(0.001f));
                Assert.That(interactor.CurrentPromptAnchor, Is.SameAs(anchor));
                Assert.That(
                    Vector2.Distance(actualScreenPosition, expectedScreenPosition),
                    Is.LessThan(0.01f),
                    "The E bubble uses an earlier camera projection than its visible world anchor.");
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= ObserveRenderedFrame;
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in _objects)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            _objects.Clear();
            if (_targetTexture != null)
            {
                _targetTexture.Release();
                Object.DestroyImmediate(_targetTexture);
                _targetTexture = null;
            }
        }

        private GameObject CreateObject(string name, bool active = true)
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(active);
            _objects.Add(gameObject);
            return gameObject;
        }

        private GameObject CreateUiObject(string name, bool active = true)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.SetActive(active);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
