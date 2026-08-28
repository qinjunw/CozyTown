using System;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownInteractionDebugView : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor2D playerInteractor;

        public void Configure(PlayerInteractor2D interactor)
        {
            playerInteractor = interactor != null
                ? interactor
                : throw new ArgumentNullException(nameof(interactor));
        }

        private void Awake()
        {
            if (playerInteractor == null)
            {
                Debug.LogError("Interaction debug view requires a PlayerInteractor2D.", this);
                enabled = false;
            }
        }

        private void OnGUI()
        {
            var prompt = playerInteractor.CurrentPrompt;
            var feedback = playerInteractor.LastInteractionFeedback;
            var height = string.IsNullOrWhiteSpace(feedback) ? 58f : 82f;

            GUILayout.BeginArea(
                new Rect(12f, Screen.height - height - 12f, 420f, height),
                GUI.skin.box);
            GUILayout.Label(string.IsNullOrWhiteSpace(prompt)
                ? "Interaction: move near a colored point"
                : $"Interaction: {prompt}");
            if (!string.IsNullOrWhiteSpace(feedback))
            {
                GUILayout.Label($"Last: {feedback}");
            }

            GUILayout.EndArea();
        }
    }
}
