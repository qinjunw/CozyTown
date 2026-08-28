using System;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerModalInputGate2D : MonoBehaviour
    {
        private Rigidbody2D _body;
        private PlayerMovement2D _movement;
        private PlayerInteractor2D _interactor;
        private object _owner;
        private bool _movementWasEnabled;
        private bool _interactorWasEnabled;

        public bool IsAcquired => _owner != null;

        public event Action AcquisitionRevoked;

        public bool TryAcquire(object owner)
        {
            if (owner == null || !isActiveAndEnabled)
            {
                return false;
            }

            if (_owner != null)
            {
                return ReferenceEquals(_owner, owner);
            }

            ResolveComponents();
            if (_movement == null || _interactor == null)
            {
                return false;
            }

            _owner = owner;
            _movementWasEnabled = _movement.enabled;
            _interactorWasEnabled = _interactor.enabled;

            _movement.enabled = false;
            _interactor.enabled = false;

            _body.linearVelocity = Vector2.zero;
            return true;
        }

        public bool Release(object owner)
        {
            if (_owner == null || !ReferenceEquals(_owner, owner))
            {
                return false;
            }

            RestoreActorControls();
            return true;
        }

        private void Awake()
        {
            ResolveComponents();
        }

        private void OnDisable()
        {
            var wasAcquired = IsAcquired;
            RestoreActorControls();
            if (wasAcquired)
            {
                AcquisitionRevoked?.Invoke();
            }
        }

        private void ResolveComponents()
        {
            _body = GetComponent<Rigidbody2D>();
            _movement = GetComponent<PlayerMovement2D>();
            _interactor = GetComponent<PlayerInteractor2D>();
        }

        private void RestoreActorControls()
        {
            if (_owner == null)
            {
                return;
            }

            if (_movement != null)
            {
                _movement.enabled = _movementWasEnabled;
            }

            if (_interactor != null)
            {
                _interactor.enabled = _interactorWasEnabled;
            }

            _owner = null;
        }
    }
}
