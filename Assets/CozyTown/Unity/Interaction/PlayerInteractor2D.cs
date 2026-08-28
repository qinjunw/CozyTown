using System;
using CozyTown.Unity.Input;
using UnityEngine;

namespace CozyTown.Unity.Interaction
{
    public sealed class PlayerInteractor2D : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour inputSourceBehaviour;
        [SerializeField] private InteractionProbe2D interactionProbe;

        private IPlayerInputSource _inputSource;

        public void Configure(
            IPlayerInputSource inputSource,
            InteractionProbe2D probe)
        {
            _inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
            inputSourceBehaviour = inputSource as MonoBehaviour;
            interactionProbe = probe != null ? probe : throw new ArgumentNullException(nameof(probe));
        }

        private void Reset()
        {
            inputSourceBehaviour = GetComponent<InputSystemPlayerInputSource>();
            interactionProbe = GetComponent<InteractionProbe2D>();
        }

        private void Awake()
        {
            if (_inputSource == null && !TryResolveInputSource(out var error))
            {
                ReportConfigurationError(error);
                return;
            }

            if (interactionProbe == null)
            {
                ReportConfigurationError($"{nameof(interactionProbe)} is required.");
            }
        }

        private void Update()
        {
            if (!_inputSource.InteractPressedThisFrame)
            {
                return;
            }

            var context = new InteractionContext(gameObject);
            if (interactionProbe.TryFindClosest(context, out var interactable))
            {
                interactable.Interact(context);
            }
        }

        private bool TryResolveInputSource(out string error)
        {
            if (inputSourceBehaviour is IPlayerInputSource inputSource)
            {
                _inputSource = inputSource;
                error = null;
                return true;
            }

            error = $"{nameof(inputSourceBehaviour)} must implement {nameof(IPlayerInputSource)}.";
            return false;
        }

        private void ReportConfigurationError(string error)
        {
            Debug.LogError($"Player interaction could not initialize: {error}", this);
            enabled = false;
        }
    }
}
