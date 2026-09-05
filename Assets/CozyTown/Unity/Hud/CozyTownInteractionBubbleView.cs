using System;
using System.Collections.Generic;
using CozyTown.Unity.Interaction;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownInteractionBubbleView : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor2D playerInteractor;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private RectTransform bubbleRect;
        [SerializeField] private Text keyText;

        private bool _isSubscribed;

        public bool IsVisible { get; private set; }

        public Transform CurrentAnchor { get; private set; }

        public void Configure(PlayerInteractor2D interactor, Camera targetWorldCamera)
        {
            if (interactor == null)
            {
                throw new ArgumentNullException(nameof(interactor));
            }

            if (targetWorldCamera == null)
            {
                throw new ArgumentNullException(nameof(targetWorldCamera));
            }

            Unsubscribe();
            playerInteractor = interactor;
            worldCamera = targetWorldCamera;
            TrySubscribe();
            Refresh();
        }

        public void ConfigureUi(RectTransform targetBubbleRect, Text targetKeyText)
        {
            bubbleRect = targetBubbleRect != null
                ? targetBubbleRect
                : throw new ArgumentNullException(nameof(targetBubbleRect));
            keyText = targetKeyText != null
                ? targetKeyText
                : throw new ArgumentNullException(nameof(targetKeyText));
            keyText.text = "E";
            Refresh();
        }

        public void Refresh()
        {
            Transform anchor = playerInteractor != null && playerInteractor.isActiveAndEnabled
                ? playerInteractor.CurrentPromptAnchor
                : null;
            ShowAnchor(anchor);
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginContextRendering += PrepareCameraProjection;
            TrySubscribe();
            Refresh();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= PrepareCameraProjection;
            RenderPipelineManager.beginCameraRendering -= UpdatePositionBeforeRendering;
            Unsubscribe();
            ShowAnchor(null);
        }

        private void Update()
        {
            UpdatePosition();
        }

        private void PrepareCameraProjection(ScriptableRenderContext context, List<Camera> cameras)
        {
            // Register after camera enable callbacks, including PixelPerfectCamera restarts,
            // so this frame's snapped projection is used before overlay UI is drawn.
            RenderPipelineManager.beginCameraRendering -= UpdatePositionBeforeRendering;
            if (isActiveAndEnabled && worldCamera != null && cameras.Contains(worldCamera))
            {
                RenderPipelineManager.beginCameraRendering += UpdatePositionBeforeRendering;
            }
        }

        private void UpdatePositionBeforeRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == worldCamera)
            {
                UpdatePosition();
            }
        }

        private void TrySubscribe()
        {
            if (_isSubscribed || !isActiveAndEnabled || playerInteractor == null)
            {
                return;
            }

            playerInteractor.CurrentPromptAnchorChanged += ShowAnchor;
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed)
            {
                return;
            }

            playerInteractor.CurrentPromptAnchorChanged -= ShowAnchor;
            _isSubscribed = false;
        }

        private void ShowAnchor(Transform anchor)
        {
            CurrentAnchor = anchor;
            IsVisible = anchor != null && worldCamera != null && bubbleRect != null;
            if (bubbleRect != null)
            {
                bubbleRect.gameObject.SetActive(IsVisible);
            }

            UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (!IsVisible || CurrentAnchor == null)
            {
                return;
            }

            bubbleRect.position = worldCamera.WorldToScreenPoint(CurrentAnchor.position);
        }
    }
}
