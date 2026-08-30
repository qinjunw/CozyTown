using System;
using CozyTown.Unity.Player;
using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownSystemMenuController : MonoBehaviour
    {
        [SerializeField] private PlayerModalInputGate2D inputGate;
        [SerializeField] private CozyTownSystemMenuView view;

        private IApplicationQuitter _quitter;
        private bool _subscribed;
        private bool _ownsGate;

        public bool IsOpen => _ownsGate && view != null && view.IsVisible;

        public void Configure(
            PlayerModalInputGate2D targetInputGate,
            CozyTownSystemMenuView targetView,
            IApplicationQuitter applicationQuitter = null)
        {
            Unsubscribe();
            inputGate = targetInputGate != null
                ? targetInputGate
                : throw new ArgumentNullException(nameof(targetInputGate));
            view = targetView != null
                ? targetView
                : throw new ArgumentNullException(nameof(targetView));
            _quitter = applicationQuitter ?? new UnityApplicationQuitter();
            Subscribe();
            view.Hide();
        }

        private void Awake()
        {
            _quitter ??= new UnityApplicationQuitter();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            Unsubscribe();
            Close();
        }

        private void Subscribe()
        {
            if (_subscribed || inputGate == null || view == null)
            {
                return;
            }

            view.GearRequested += Toggle;
            view.SettingsRequested += ShowSettings;
            view.BackRequested += ShowMain;
            view.QuitRequested += Quit;
            inputGate.AcquisitionRevoked += HandleGateRevoked;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            view.GearRequested -= Toggle;
            view.SettingsRequested -= ShowSettings;
            view.BackRequested -= ShowMain;
            view.QuitRequested -= Quit;
            inputGate.AcquisitionRevoked -= HandleGateRevoked;
            _subscribed = false;
        }

        private void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            if (!inputGate.TryAcquire(this))
            {
                return;
            }

            _ownsGate = true;
            view.ShowMain();
        }

        private void ShowSettings()
        {
            if (IsOpen)
            {
                view.ShowSettings();
            }
        }

        private void ShowMain()
        {
            if (IsOpen)
            {
                view.ShowMain();
            }
        }

        private void Quit()
        {
            if (IsOpen)
            {
                _quitter.Quit();
            }
        }

        private void Close()
        {
            view?.Hide();
            if (_ownsGate && inputGate != null)
            {
                inputGate.Release(this);
            }

            _ownsGate = false;
        }

        private void HandleGateRevoked()
        {
            _ownsGate = false;
            view?.Hide();
        }
    }
}
