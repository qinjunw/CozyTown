using System;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownDebugHudView : MonoBehaviour, ICozyTownHudView
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text clockText;
        [SerializeField] private Text coinText;

        private CozyTownHudState _state;
        private bool _hasState;

        public void ConfigureUi(
            GameObject targetPanel,
            Text targetClockText,
            Text targetCoinText)
        {
            panel = targetPanel != null
                ? targetPanel
                : throw new ArgumentNullException(nameof(targetPanel));
            clockText = targetClockText != null
                ? targetClockText
                : throw new ArgumentNullException(nameof(targetClockText));
            coinText = targetCoinText != null
                ? targetCoinText
                : throw new ArgumentNullException(nameof(targetCoinText));

            RefreshUi();
        }

        public void Render(CozyTownHudState state)
        {
            _state = state;
            _hasState = true;
            RefreshUi();
        }

        private void RefreshUi()
        {
            if (panel == null || clockText == null || coinText == null)
            {
                return;
            }

            panel.SetActive(_hasState);
            if (!_hasState)
            {
                return;
            }

            clockText.text = $"Day {_state.Day}  {_state.Hour:00}:{_state.Minute:00}";
            coinText.text = $"Coins: {_state.Balance}";
        }
    }
}
