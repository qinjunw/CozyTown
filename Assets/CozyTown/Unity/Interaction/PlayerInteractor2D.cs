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
        private IInteractable _currentTarget;

        public string CurrentPrompt { get; private set; } = string.Empty;

        public Transform CurrentPromptAnchor { get; private set; }

        public string LastInteractionFeedback { get; private set; } = string.Empty;

        public event Action<Transform> CurrentPromptAnchorChanged;

        public void Configure(
            IPlayerInputSource inputSource,
            InteractionProbe2D probe)
        {
            _inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
            inputSourceBehaviour = inputSource as MonoBehaviour;
            interactionProbe = probe != null ? probe : throw new ArgumentNullException(nameof(probe));
            ClearCurrentTarget();
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
            if (_inputSource == null || interactionProbe == null)
            {
                return;
            }

            var context = new InteractionContext(gameObject);
            ResolveCurrentTarget(context);

            var interactPressed = _inputSource.InteractPressedThisFrame;
            if (interactPressed && _currentTarget != null)
            {
                var target = _currentTarget;
                target.Interact(context);
                LastInteractionFeedback = BuildFeedback(target);
            }
        }

        private void OnDisable()
        {
            ClearCurrentTarget();
        }

        private void ResolveCurrentTarget(InteractionContext context)
        {
            if (!interactionProbe.TryFindClosest(context, out var target))
            {
                ClearCurrentTarget();
                return;
            }

            _currentTarget = target;
            var promptSource = target as IInteractionPromptSource;
            CurrentPrompt = promptSource?.PromptText ?? string.Empty;
            SetCurrentPromptAnchor(promptSource?.PromptAnchor);
        }

        private void ClearCurrentTarget()
        {
            _currentTarget = null;
            CurrentPrompt = string.Empty;
            SetCurrentPromptAnchor(null);
        }

        private void SetCurrentPromptAnchor(Transform anchor)
        {
            if (ReferenceEquals(CurrentPromptAnchor, anchor))
            {
                return;
            }

            CurrentPromptAnchor = anchor;
            CurrentPromptAnchorChanged?.Invoke(anchor);
        }

        private static string BuildFeedback(IInteractable target)
        {
            if (target is IInteractionPromptSource promptSource
                && !string.IsNullOrWhiteSpace(promptSource.PromptText))
            {
                return $"Interacted: {promptSource.PromptText}";
            }

            return "Interaction triggered";
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
