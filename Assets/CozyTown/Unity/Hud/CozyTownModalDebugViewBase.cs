using System;
using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public abstract class CozyTownModalDebugViewBase : MonoBehaviour
    {
        private int _fontSize;

        public event Action CloseRequested;
        public bool IsVisible { get; private set; }
        public string Feedback { get; private set; } = string.Empty;
        protected GUIStyle LabelStyle { get; private set; }
        protected GUIStyle ButtonStyle { get; private set; }
        protected GUIStyle BoxStyle { get; private set; }

        public void Hide() => IsVisible = false;

        public void RequestClose()
        {
            if (IsVisible)
            {
                CloseRequested?.Invoke();
            }
        }

        protected void ShowBase(string feedback)
        {
            Feedback = feedback ?? string.Empty;
            IsVisible = true;
        }

        protected bool BeginPanel(string title)
        {
            if (!IsVisible)
            {
                return false;
            }

            EnsureStyles();
            var width = Mathf.Min(900f, Screen.width - 32f);
            var height = Mathf.Min(720f, Screen.height - 64f);
            GUILayout.BeginArea(new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height), BoxStyle);
            GUILayout.Label(title, LabelStyle);
            if (!string.IsNullOrWhiteSpace(Feedback))
            {
                GUILayout.Label(Feedback, LabelStyle);
            }
            return true;
        }

        protected void EndPanel()
        {
            if (GUILayout.Button("Close", ButtonStyle, GUILayout.Height(_fontSize + 18f)))
            {
                RequestClose();
            }
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            var size = CozyTownDebugGuiStyles.CalculateFontSize(Screen.height);
            if (LabelStyle != null && size == _fontSize)
            {
                return;
            }
            _fontSize = size;
            LabelStyle = CozyTownDebugGuiStyles.CreateLabelStyle(size);
            BoxStyle = CozyTownDebugGuiStyles.CreateBoxStyle();
            ButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold };
        }
    }
}
