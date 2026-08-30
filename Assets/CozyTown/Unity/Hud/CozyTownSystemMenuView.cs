using System;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownSystemMenuView : MonoBehaviour
    {
        [SerializeField] private Button gearButton;
        [SerializeField] private GameObject panel;
        [SerializeField] private GameObject mainPage;
        [SerializeField] private GameObject settingsPage;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Button quitButton;

        private bool _listenersBound;

        public event Action GearRequested;
        public event Action SettingsRequested;
        public event Action BackRequested;
        public event Action QuitRequested;
        public event Action Deactivated;

        public bool IsVisible { get; private set; }
        public bool IsSettingsVisible { get; private set; }

        public void ConfigureUi(
            Button targetGearButton,
            GameObject targetPanel,
            GameObject targetMainPage,
            GameObject targetSettingsPage,
            Button targetSettingsButton,
            Button targetBackButton,
            Button targetQuitButton)
        {
            RemoveButtonListeners();
            gearButton = targetGearButton != null
                ? targetGearButton
                : throw new ArgumentNullException(nameof(targetGearButton));
            panel = targetPanel != null
                ? targetPanel
                : throw new ArgumentNullException(nameof(targetPanel));
            mainPage = targetMainPage != null
                ? targetMainPage
                : throw new ArgumentNullException(nameof(targetMainPage));
            settingsPage = targetSettingsPage != null
                ? targetSettingsPage
                : throw new ArgumentNullException(nameof(targetSettingsPage));
            settingsButton = targetSettingsButton != null
                ? targetSettingsButton
                : throw new ArgumentNullException(nameof(targetSettingsButton));
            backButton = targetBackButton != null
                ? targetBackButton
                : throw new ArgumentNullException(nameof(targetBackButton));
            quitButton = targetQuitButton != null
                ? targetQuitButton
                : throw new ArgumentNullException(nameof(targetQuitButton));

            BindButtonListeners();
            RefreshUi();
        }

        public void ShowMain()
        {
            IsVisible = true;
            IsSettingsVisible = false;
            RefreshUi();
        }

        public void ShowSettings()
        {
            IsVisible = true;
            IsSettingsVisible = true;
            RefreshUi();
        }

        public void Hide()
        {
            IsVisible = false;
            IsSettingsVisible = false;
            RefreshUi();
        }

        private void OnEnable()
        {
            BindButtonListeners();
            RefreshUi();
        }

        private void OnDisable()
        {
            RemoveButtonListeners();
            IsVisible = false;
            IsSettingsVisible = false;
            if (panel != null)
            {
                panel.SetActive(false);
            }

            Deactivated?.Invoke();
        }

        private void RequestGear() => GearRequested?.Invoke();

        private void RequestSettings()
        {
            if (IsVisible && !IsSettingsVisible)
            {
                SettingsRequested?.Invoke();
            }
        }

        private void RequestBack()
        {
            if (IsVisible && IsSettingsVisible)
            {
                BackRequested?.Invoke();
            }
        }

        private void RequestQuit()
        {
            if (IsVisible && !IsSettingsVisible)
            {
                QuitRequested?.Invoke();
            }
        }

        private void RefreshUi()
        {
            panel?.SetActive(IsVisible);
            mainPage?.SetActive(IsVisible && !IsSettingsVisible);
            settingsPage?.SetActive(IsVisible && IsSettingsVisible);
        }

        private void BindButtonListeners()
        {
            if (_listenersBound || gearButton == null || settingsButton == null
                || backButton == null || quitButton == null)
            {
                return;
            }

            gearButton.onClick.AddListener(RequestGear);
            settingsButton.onClick.AddListener(RequestSettings);
            backButton.onClick.AddListener(RequestBack);
            quitButton.onClick.AddListener(RequestQuit);
            _listenersBound = true;
        }

        private void RemoveButtonListeners()
        {
            if (!_listenersBound)
            {
                return;
            }

            gearButton?.onClick.RemoveListener(RequestGear);
            settingsButton?.onClick.RemoveListener(RequestSettings);
            backButton?.onClick.RemoveListener(RequestBack);
            quitButton?.onClick.RemoveListener(RequestQuit);
            _listenersBound = false;
        }
    }
}
