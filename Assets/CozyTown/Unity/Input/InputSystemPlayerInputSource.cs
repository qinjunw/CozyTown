using UnityEngine;
using UnityEngine.InputSystem;

namespace CozyTown.Unity.Input
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class InputSystemPlayerInputSource : MonoBehaviour, IPlayerInputSource
    {
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string interactActionName = "Interact";

        private InputAction _moveAction;
        private InputAction _interactAction;

        public Vector2 Movement => isActiveAndEnabled
            ? _moveAction?.ReadValue<Vector2>() ?? Vector2.zero
            : Vector2.zero;

        // The input asset currently applies a Hold interaction. Reading the raw press edge here
        // intentionally produces one interaction attempt per physical press instead of waiting for Hold.
        public bool InteractPressedThisFrame => isActiveAndEnabled
            && (_interactAction?.WasPressedThisFrame() ?? false);

        private void Reset()
        {
            playerInput = GetComponent<PlayerInput>();
        }

        private void Awake()
        {
            playerInput ??= GetComponent<PlayerInput>();
            if (!TryResolveActions(out var error))
            {
                Debug.LogError($"Player input could not initialize: {error}", this);
                enabled = false;
            }
        }

        private bool TryResolveActions(out string error)
        {
            if (playerInput == null || playerInput.actions == null)
            {
                error = "PlayerInput and its InputActionAsset are required.";
                return false;
            }

            _moveAction = playerInput.actions.FindAction(moveActionName, false);
            if (_moveAction == null)
            {
                error = $"Input action '{moveActionName}' was not found.";
                return false;
            }

            _interactAction = playerInput.actions.FindAction(interactActionName, false);
            if (_interactAction == null)
            {
                error = $"Input action '{interactActionName}' was not found.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
