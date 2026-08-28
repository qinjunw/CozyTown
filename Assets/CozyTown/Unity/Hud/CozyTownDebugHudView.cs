using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownDebugHudView : MonoBehaviour, ICozyTownHudView
    {
        private CozyTownHudState _state;
        private bool _hasState;
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private int _fontSize;

        public void Render(CozyTownHudState state)
        {
            _state = state;
            _hasState = true;
        }

        private void OnGUI()
        {
            if (!_hasState)
            {
                return;
            }

            EnsureStyles();
            var lineHeight = _fontSize + 10f;
            var width = Mathf.Min(360f, Screen.width - 32f);
            var height = lineHeight * 2f + 24f;

            GUILayout.BeginArea(new Rect(16f, 16f, width, height), _boxStyle);
            GUILayout.Label(
                $"Day {_state.Day}  {_state.Hour:00}:{_state.Minute:00}",
                _labelStyle,
                GUILayout.Height(lineHeight));
            GUILayout.Label(
                $"Coins: {_state.Balance}",
                _labelStyle,
                GUILayout.Height(lineHeight));
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
