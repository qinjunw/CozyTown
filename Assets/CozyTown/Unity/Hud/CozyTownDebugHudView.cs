using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownDebugHudView : MonoBehaviour, ICozyTownHudView
    {
        private CozyTownHudState _state;
        private bool _hasState;

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

            GUILayout.BeginArea(new Rect(12f, 12f, 220f, 80f), GUI.skin.box);
            GUILayout.Label($"Day {_state.Day}  {_state.Hour:00}:{_state.Minute:00}");
            GUILayout.Label($"Coins: {_state.Balance}");
            GUILayout.EndArea();
        }
    }
}
