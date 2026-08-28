using System;
using CozyTown.Unity.Player;
using UnityEngine;

namespace CozyTown.Unity.Interaction
{
    public abstract class CozyTownModalPresenterBase : MonoBehaviour
    {
        [SerializeField] private TownInteractionPoint2D _interactionPoint;
        private PlayerModalInputGate2D _gate;
        private bool _subscribed;

        public bool IsOpen => _gate != null;

        protected abstract TownInteractionKind ExpectedKind { get; }
        protected abstract bool HasDependencies { get; }

        protected void ConfigureInteraction(TownInteractionPoint2D point)
        {
            _interactionPoint = point != null ? point : throw new ArgumentNullException(nameof(point));
            TrySubscribe();
        }

        protected void DependenciesChanged() => TrySubscribe();

        protected abstract void SubscribeView();
        protected abstract void UnsubscribeView();
        protected abstract void ShowInitialState();
        protected abstract void HideView();

        protected void CloseModal()
        {
            if (_gate != null)
            {
                _gate.AcquisitionRevoked -= HandleGateRevoked;
                _gate.Release(this);
                _gate = null;
            }

            HideView();
        }

        private void OnEnable() => TrySubscribe();

        private void OnDisable()
        {
            if (_subscribed)
            {
                _interactionPoint.Interacted -= HandleInteraction;
                UnsubscribeView();
                _subscribed = false;
            }

            CloseModal();
        }

        private void TrySubscribe()
        {
            if (!isActiveAndEnabled || _subscribed || _interactionPoint == null
                || _interactionPoint.Kind != ExpectedKind || !HasDependencies)
            {
                return;
            }

            _interactionPoint.Interacted += HandleInteraction;
            SubscribeView();
            _subscribed = true;
        }

        private void HandleInteraction(InteractionContext context)
        {
            if (IsOpen || context.Actor == null)
            {
                return;
            }

            var gate = context.Actor.GetComponent<PlayerModalInputGate2D>();
            if (gate == null || !gate.TryAcquire(this))
            {
                return;
            }

            _gate = gate;
            _gate.AcquisitionRevoked += HandleGateRevoked;
            ShowInitialState();
        }

        private void HandleGateRevoked()
        {
            if (_gate != null)
            {
                _gate.AcquisitionRevoked -= HandleGateRevoked;
                _gate = null;
            }

            HideView();
        }
    }
}
