using System;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Time;
using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownHudPresenter : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour viewBehaviour;

        private ITimeService _time;
        private IWallet _wallet;
        private ICozyTownHudView _view;

        public void Bind(ITimeService time, IWallet wallet)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        public void ConfigureView(ICozyTownHudView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            viewBehaviour = view as MonoBehaviour;
        }

        private void Awake()
        {
            if (_view == null && !TryResolveView(out var error))
            {
                ReportConfigurationError(error);
                return;
            }

            if (_time == null || _wallet == null)
            {
                ReportConfigurationError("ITimeService and IWallet must be injected before Awake.");
            }
        }

        private void LateUpdate()
        {
            var clock = _time.Current;
            _view.Render(new CozyTownHudState(
                clock.Day,
                clock.MinuteOfDay,
                _wallet.Balance));
        }

        private bool TryResolveView(out string error)
        {
            if (viewBehaviour is ICozyTownHudView view)
            {
                _view = view;
                error = null;
                return true;
            }

            error = $"{nameof(viewBehaviour)} must implement {nameof(ICozyTownHudView)}.";
            return false;
        }

        private void ReportConfigurationError(string error)
        {
            Debug.LogError($"HUD presenter could not initialize: {error}", this);
            enabled = false;
        }
    }
}
