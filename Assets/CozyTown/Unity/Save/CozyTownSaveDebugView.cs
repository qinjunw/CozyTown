using System;
using CozyTown.Unity.Hud;
using UnityEngine;

namespace CozyTown.Unity.Save
{
    public sealed class CozyTownSaveDebugView : MonoBehaviour
    {
        private GUIStyle _boxStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;
        private int _fontSize;

        public event Action SaveRequested;

        public event Action LoadRequested;

        public bool IsVisible { get; private set; }

        public bool HasSave { get; private set; }

        public string Feedback { get; private set; } = string.Empty;

        public void Show(bool hasSave, string feedback)
        {
            HasSave = hasSave;
            Feedback = feedback ?? string.Empty;
            IsVisible = true;
        }

        public void Hide()
        {
            IsVisible = false;
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

        private void OnGUI()
        {
            if (!IsVisible)
            {
                return;
            }

            EnsureStyles();
            float width = Mathf.Min(440f, Screen.width - 32f);
            float height = _fontSize * 4f + 92f;
            GUILayout.BeginArea(
                new Rect(Screen.width - width - 16f, 16f, width, height),
                _boxStyle);
            GUILayout.Label("Save", _labelStyle);
            if (!string.IsNullOrWhiteSpace(Feedback))
            {
                GUILayout.Label(Feedback, _labelStyle);
            }

            if (GUILayout.Button("Save game", _buttonStyle, GUILayout.Height(_fontSize + 18f)))
            {
                RequestSave();
            }

            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && HasSave;
            if (GUILayout.Button("Load game", _buttonStyle, GUILayout.Height(_fontSize + 18f)))
            {
                RequestLoad();
            }

            GUI.enabled = wasEnabled;
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            int size = CozyTownDebugGuiStyles.CalculateFontSize(Screen.height);
            if (_labelStyle != null && size == _fontSize)
            {
                return;
            }

            _fontSize = size;
            _labelStyle = CozyTownDebugGuiStyles.CreateLabelStyle(size);
            _boxStyle = CozyTownDebugGuiStyles.CreateBoxStyle();
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = size,
                fontStyle = FontStyle.Bold
            };
        }
    }
}
