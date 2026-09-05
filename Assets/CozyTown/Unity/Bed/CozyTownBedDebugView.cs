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
        [SerializeField] private Text sleepHoursText;
        [SerializeField] private Button decreaseSleepButton;
        [SerializeField] private Button increaseSleepButton;

        private bool _listenersAttached;

        public event Action SleepRequested;

        public int SelectedSleepHours { get; private set; } = 8;

        public void ConfigureUi(
            GameObject configuredPanel,
            Text configuredFeedbackText,
            Button configuredCloseButton,
            Button configuredSleepButton,
            Text configuredSleepHoursText,
            Button configuredDecreaseSleepButton,
            Button configuredIncreaseSleepButton)
        {
            if (configuredSleepHoursText == null)
            {
                throw new ArgumentNullException(nameof(configuredSleepHoursText));
            }
            if (configuredDecreaseSleepButton == null)
            {
                throw new ArgumentNullException(nameof(configuredDecreaseSleepButton));
            }
            if (configuredIncreaseSleepButton == null)
            {
                throw new ArgumentNullException(nameof(configuredIncreaseSleepButton));
            }

            ConfigureUi(
                configuredPanel,
                configuredFeedbackText,
                configuredCloseButton,
                configuredSleepButton);
            DetachListeners();
            sleepHoursText = configuredSleepHoursText;
            decreaseSleepButton = configuredDecreaseSleepButton;
            increaseSleepButton = configuredIncreaseSleepButton;
            AttachListeners();
            RefreshUi();
        }

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
            if (!IsVisible)
            {
                SelectedSleepHours = 8;
            }
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

        public void RequestDecreaseSleepHours()
        {
            if (IsVisible && SelectedSleepHours > 1)
            {
                SelectedSleepHours--;
                RefreshUi();
            }
        }

        public void RequestIncreaseSleepHours()
        {
            if (IsVisible && SelectedSleepHours < 12)
            {
                SelectedSleepHours++;
                RefreshUi();
            }
        }

        private void OnEnable()
        {
            AttachListeners();
            RefreshUi();
        }

        private void OnDisable()
        {
            RequestClose();
            DetachListeners();
            Hide();
        }

        private void RefreshUi()
        {
            if (panel == null || feedbackText == null)
            {
                return;
            }

            panel.SetActive(IsVisible);
            feedbackText.text = Feedback;
            if (sleepHoursText != null)
            {
                sleepHoursText.text = SelectedSleepHours == 1
                    ? "1 hour"
                    : $"{SelectedSleepHours} hours";
            }
            if (decreaseSleepButton != null)
            {
                decreaseSleepButton.interactable = IsVisible && SelectedSleepHours > 1;
            }
            if (increaseSleepButton != null)
            {
                increaseSleepButton.interactable = IsVisible && SelectedSleepHours < 12;
            }
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
            if (decreaseSleepButton != null)
            {
                decreaseSleepButton.onClick.AddListener(RequestDecreaseSleepHours);
            }
            if (increaseSleepButton != null)
            {
                increaseSleepButton.onClick.AddListener(RequestIncreaseSleepHours);
            }
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

            if (decreaseSleepButton != null)
            {
                decreaseSleepButton.onClick.RemoveListener(RequestDecreaseSleepHours);
            }
            if (increaseSleepButton != null)
            {
                increaseSleepButton.onClick.RemoveListener(RequestIncreaseSleepHours);
            }

            _listenersAttached = false;
        }
    }
}
