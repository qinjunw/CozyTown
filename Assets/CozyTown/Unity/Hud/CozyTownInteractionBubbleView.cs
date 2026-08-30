using System;
using CozyTown.Unity.Interaction;
using UnityEngine;
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
            TrySubscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ShowAnchor(null);
        }

        private void Update()
        {
            UpdatePosition();
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
