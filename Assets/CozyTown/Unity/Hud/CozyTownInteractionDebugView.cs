using System;
using CozyTown.Unity.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownInteractionDebugView : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor2D playerInteractor;
        [SerializeField] private GameObject panel;
        [SerializeField] private Text promptText;
        [SerializeField] private Text feedbackText;

        public void Configure(PlayerInteractor2D interactor)
        {
            playerInteractor = interactor != null
                ? interactor
                : throw new ArgumentNullException(nameof(interactor));
            Refresh();
        }

        public void ConfigureUi(
            GameObject targetPanel,
            Text targetPromptText,
            Text targetFeedbackText)
        {
            panel = targetPanel != null
                ? targetPanel
                : throw new ArgumentNullException(nameof(targetPanel));
            promptText = targetPromptText != null
                ? targetPromptText
                : throw new ArgumentNullException(nameof(targetPromptText));
            feedbackText = targetFeedbackText != null
                ? targetFeedbackText
                : throw new ArgumentNullException(nameof(targetFeedbackText));

            Refresh();
        }

        private void Awake()
        {
            if (playerInteractor == null)
            {
                Debug.LogError("Interaction debug view requires a PlayerInteractor2D.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (panel == null || promptText == null || feedbackText == null)
            {
                return;
            }

            bool hasInteractor = playerInteractor != null;
            panel.SetActive(hasInteractor);
            if (!hasInteractor)
            {
                return;
            }

            string prompt = playerInteractor.CurrentPrompt;
            string feedback = playerInteractor.LastInteractionFeedback;
            promptText.text = string.IsNullOrWhiteSpace(prompt)
                ? "Interaction: move near a colored point"
                : $"Interaction: {prompt}";
            feedbackText.text = string.IsNullOrWhiteSpace(feedback)
                ? string.Empty
                : $"Last: {feedback}";
        }
    }
}
