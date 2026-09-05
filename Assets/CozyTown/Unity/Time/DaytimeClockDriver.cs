using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Player;
using UnityEngine;

namespace CozyTown.Unity.Time
{
    public sealed class DaytimeClockDriver : MonoBehaviour
    {
        [SerializeField] private PlayerModalInputGate2D _inputGate;

        private IDaytimeClock _clock;
        private bool _discardNextFrame = true;
        private bool _subscribed;
        private bool _hasApplicationFocus = true;

        public bool IsSimulationPaused => !isActiveAndEnabled || _clock == null
            || _inputGate == null || _inputGate.IsAcquired || !_hasApplicationFocus;

        public void Bind(IDaytimeClock clock)
        {
            if (ReferenceEquals(_clock, clock) && clock != null)
            {
                return;
            }

            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _discardNextFrame = true;
        }

        public void ConfigureInputGate(PlayerModalInputGate2D inputGate)
        {
            if (inputGate == null)
            {
                throw new ArgumentNullException(nameof(inputGate));
            }

            if (_inputGate == inputGate)
            {
                return;
            }

            Unsubscribe();
            _inputGate = inputGate;
            _discardNextFrame = true;
            Subscribe();
        }

        public void AdvanceFrame(double frameElapsedSeconds)
        {
            if (IsSimulationPaused)
            {
                return;
            }

            if (_discardNextFrame)
            {
                _discardNextFrame = false;
                return;
            }

            _clock.AdvanceElapsed(frameElapsedSeconds);
        }

        public void SetApplicationFocus(bool hasFocus)
        {
            if (_hasApplicationFocus == hasFocus)
            {
                return;
            }

            _hasApplicationFocus = hasFocus;
            _discardNextFrame = true;
        }

        private void OnEnable()
        {
            _hasApplicationFocus = Application.isFocused;
            _discardNextFrame = true;
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        private void OnApplicationFocus(bool hasFocus) => SetApplicationFocus(hasFocus);

        private void LateUpdate()
        {
            // Update can open a modal before this raw frame sample is filtered by AdvanceFrame.
            // Only accepted elapsed time reaches the runtime clock; pause uses the gate and focus,
            // so the sample uses unscaled time rather than changing Unity's global time scale.
            AdvanceFrame(UnityEngine.Time.unscaledDeltaTime);
        }

        private void Subscribe()
        {
            if (_subscribed || !isActiveAndEnabled || _inputGate == null)
            {
                return;
            }

            _inputGate.AcquisitionChanged += HandleAcquisitionChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            if (_inputGate != null)
            {
                _inputGate.AcquisitionChanged -= HandleAcquisitionChanged;
            }

            _subscribed = false;
        }

        private void HandleAcquisitionChanged(bool isAcquired)
        {
            _discardNextFrame = true;
        }
    }
}
