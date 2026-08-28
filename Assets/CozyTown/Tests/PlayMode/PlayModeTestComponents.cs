using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class PlayModePlayerInputSource : MonoBehaviour, IPlayerInputSource
    {
        private bool _interactPending;

        public Vector2 Movement { get; set; }

        public bool InteractPressedThisFrame
        {
            get
            {
                var pressed = _interactPending;
                _interactPending = false;
                return pressed;
            }
        }

        public void PressInteract()
        {
            _interactPending = true;
        }
    }

    public sealed class CountingInteractable : MonoBehaviour, IInteractable
    {
        public int InteractionCount { get; private set; }

        public bool CanInteract(InteractionContext context)
        {
            return context.Actor != null && isActiveAndEnabled;
        }

        public void Interact(InteractionContext context)
        {
            if (CanInteract(context))
            {
                InteractionCount++;
            }
        }
    }
}
