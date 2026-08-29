using System;
using CozyTown.Unity.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Bed
{
    public sealed class CozyTownBedDebugView : CozyTownModalDebugViewBase
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text feedbackText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button sleepButton;

        private bool _listenersAttached;

        public event Action SleepRequested;

        public void ConfigureUi(
            GameObject configuredPanel,
            Text configuredFeedbackText,
            Button configuredCloseButton,
            Button configuredSleepButton)
        {
            DetachListeners();
            panel = configuredPanel != null
                ? configuredPanel
                : throw new ArgumentNullException(nameof(configuredPanel));
            feedbackText = configuredFeedbackText != null
                ? configuredFeedbackText
                : throw new ArgumentNullException(nameof(configuredFeedbackText));
            closeButton = configuredCloseButton != null
                ? configuredCloseButton
                : throw new ArgumentNullException(nameof(configuredCloseButton));
            sleepButton = configuredSleepButton != null
                ? configuredSleepButton
                : throw new ArgumentNullException(nameof(configuredSleepButton));

            panel.SetActive(IsVisible);
            AttachListeners();
            RefreshUi();
        }

        public void Show(string feedback)
        {
            ShowBase(feedback);
            RefreshUi();
        }

        public new void Hide()
        {
            base.Hide();
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        public void RequestSleep()
        {
            if (IsVisible)
            {
                SleepRequested?.Invoke();
            }
        }

        private void OnEnable()
        {
            AttachListeners();
            RefreshUi();
        }

        private void OnDisable()
        {
            DetachListeners();
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        private void RefreshUi()
        {
            if (panel == null || feedbackText == null)
            {
                return;
            }

            panel.SetActive(IsVisible);
            feedbackText.text = Feedback;
        }

        private void AttachListeners()
        {
            if (_listenersAttached
                || closeButton == null
                || sleepButton == null
                || !isActiveAndEnabled)
            {
                return;
            }

            closeButton.onClick.AddListener(RequestClose);
            sleepButton.onClick.AddListener(RequestSleep);
            _listenersAttached = true;
        }

        private void DetachListeners()
        {
            if (!_listenersAttached)
            {
                return;
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(RequestClose);
            }

            if (sleepButton != null)
            {
                sleepButton.onClick.RemoveListener(RequestSleep);
            }

            _listenersAttached = false;
        }
    }
}
