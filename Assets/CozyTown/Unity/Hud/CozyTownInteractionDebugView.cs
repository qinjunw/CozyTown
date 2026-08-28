using System;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownInteractionDebugView : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor2D playerInteractor;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private int _fontSize;

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
            EnsureStyles();
            var prompt = playerInteractor.CurrentPrompt;
            var feedback = playerInteractor.LastInteractionFeedback;
            var lineCount = string.IsNullOrWhiteSpace(feedback) ? 1 : 2;
            var lineHeight = _fontSize + 12f;
            var height = lineHeight * lineCount + 24f;
            var width = Mathf.Min(
                Mathf.Max(520f, Screen.width * 0.48f),
                Screen.width - 32f);

            GUILayout.BeginArea(
                new Rect(16f, Screen.height - height - 16f, width, height),
                _boxStyle);
            GUILayout.Label(string.IsNullOrWhiteSpace(prompt)
                ? "Interaction: move near a colored point"
                : $"Interaction: {prompt}",
                _labelStyle,
                GUILayout.Height(lineHeight));
            if (!string.IsNullOrWhiteSpace(feedback))
            {
                GUILayout.Label(
                    $"Last: {feedback}",
                    _labelStyle,
                    GUILayout.Height(lineHeight));
            }

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            var fontSize = CozyTownDebugGuiStyles.CalculateFontSize(Screen.height);
            if (_labelStyle != null && _fontSize == fontSize)
            {
                return;
            }

            _fontSize = fontSize;
            _labelStyle = CozyTownDebugGuiStyles.CreateLabelStyle(fontSize);
            _boxStyle = CozyTownDebugGuiStyles.CreateBoxStyle();
        }
    }
}
