using System;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Save
{
    public sealed class CozyTownSaveDebugView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text feedbackText;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;

        public event Action SaveRequested;

        public event Action LoadRequested;

        public bool IsVisible { get; private set; }

        public bool HasSave { get; private set; }

        public string Feedback { get; private set; } = string.Empty;

        public void ConfigureUi(
            GameObject targetPanel,
            Text targetFeedbackText,
            Button targetSaveButton,
            Button targetLoadButton)
        {
            if (targetPanel == null)
            {
                throw new ArgumentNullException(nameof(targetPanel));
            }

            if (targetFeedbackText == null)
            {
                throw new ArgumentNullException(nameof(targetFeedbackText));
            }

            if (targetSaveButton == null)
            {
                throw new ArgumentNullException(nameof(targetSaveButton));
            }

            if (targetLoadButton == null)
            {
                throw new ArgumentNullException(nameof(targetLoadButton));
            }

            RemoveButtonListeners();

            panel = targetPanel;
            feedbackText = targetFeedbackText;
            saveButton = targetSaveButton;
            loadButton = targetLoadButton;

            BindButtonListeners();
            RefreshUi();
        }

        public void Show(bool hasSave, string feedback)
        {
            HasSave = hasSave;
            Feedback = feedback ?? string.Empty;
            IsVisible = true;
            RefreshUi();
        }

        public void Hide()
        {
            IsVisible = false;
            RefreshUi();
        }

        public void RequestSave()
        {
            if (IsVisible)
            {
                SaveRequested?.Invoke();
            }
        }

        public void RequestLoad()
        {
            if (IsVisible && HasSave)
            {
                LoadRequested?.Invoke();
            }
        }

        private void OnEnable()
        {
            BindButtonListeners();
            RefreshUi();
        }

        private void OnDisable()
        {
            RemoveButtonListeners();
        }

        private void RefreshUi()
        {
            if (panel != null)
            {
                panel.SetActive(IsVisible);
            }

            if (feedbackText != null)
            {
                feedbackText.text = Feedback;
            }

            if (saveButton != null)
            {
                saveButton.interactable = IsVisible;
            }

            if (loadButton != null)
            {
                loadButton.interactable = IsVisible && HasSave;
            }
        }

        private void RemoveButtonListeners()
        {
            if (saveButton != null)
            {
                saveButton.onClick.RemoveListener(RequestSave);
            }

            if (loadButton != null)
            {
                loadButton.onClick.RemoveListener(RequestLoad);
            }
        }

        private void BindButtonListeners()
        {
            if (saveButton != null)
            {
                saveButton.onClick.RemoveListener(RequestSave);
                saveButton.onClick.AddListener(RequestSave);
            }

            if (loadButton != null)
            {
                loadButton.onClick.RemoveListener(RequestLoad);
                loadButton.onClick.AddListener(RequestLoad);
            }
        }
    }
}
