using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CozyTown.Unity.Input
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class InputSystemPlayerInputSource : MonoBehaviour,
        IPlayerInputSource,
        IInventoryUiInputSource
    {
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string interactActionName = "Interact";
        [SerializeField] private string backpackActionName = "Backpack";
        [SerializeField] private string[] hotbarActionNames =
            { "Hotbar1", "Hotbar2", "Hotbar3", "Hotbar4", "Hotbar5" };

        private InputAction _moveAction;
        private InputAction _interactAction;
        private InputAction _backpackAction;
        private InputAction[] _hotbarActions = Array.Empty<InputAction>();

        public Vector2 Movement => isActiveAndEnabled
            ? _moveAction?.ReadValue<Vector2>() ?? Vector2.zero
            : Vector2.zero;

        // The input asset currently applies a Hold interaction. Reading the raw press edge here
        // intentionally produces one interaction attempt per physical press instead of waiting for Hold.
        public bool InteractPressedThisFrame => isActiveAndEnabled
            && (_interactAction?.WasPressedThisFrame() ?? false);

        public bool BackpackTogglePressedThisFrame => isActiveAndEnabled
            && (_backpackAction?.WasPressedThisFrame() ?? false);

        public int HotbarSelectionPressedThisFrame
        {
            get
            {
                if (!isActiveAndEnabled)
                {
                    return -1;
                }

                for (var index = 0; index < _hotbarActions.Length; index++)
                {
                    if (_hotbarActions[index].WasPressedThisFrame())
                    {
                        return index;
                    }
                }

                return -1;
            }
        }

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

            _backpackAction = playerInput.actions.FindAction(backpackActionName, false);
            if (_backpackAction == null)
            {
                error = $"Input action '{backpackActionName}' was not found.";
                return false;
            }

            if (hotbarActionNames == null || hotbarActionNames.Length != 5)
            {
                error = "Exactly five hotbar input action names are required.";
                return false;
            }

            _hotbarActions = new InputAction[hotbarActionNames.Length];
            for (var index = 0; index < hotbarActionNames.Length; index++)
            {
                _hotbarActions[index] = playerInput.actions.FindAction(
                    hotbarActionNames[index],
                    false);
                if (_hotbarActions[index] == null)
                {
                    error = $"Input action '{hotbarActionNames[index]}' was not found.";
                    return false;
                }
            }

            error = null;
            return true;
        }
    }
}
