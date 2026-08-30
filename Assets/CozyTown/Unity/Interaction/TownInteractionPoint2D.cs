using System;
using UnityEngine;

namespace CozyTown.Unity.Interaction
{
    public sealed class TownInteractionPoint2D : MonoBehaviour, IInteractable, IInteractionPromptSource
    {
        [SerializeField] private TownInteractionKind kind;
        [SerializeField] private string promptText = "Interact";
        [SerializeField] private Transform promptAnchor;

        public TownInteractionKind Kind => kind;

        public string PromptText => promptText ?? string.Empty;

        public Transform PromptAnchor => promptAnchor != null ? promptAnchor : transform;

        public int InteractionCount { get; private set; }

        public event Action<InteractionContext> Interacted;

        public void Configure(TownInteractionKind interactionKind, string prompt)
        {
            if (!Enum.IsDefined(typeof(TownInteractionKind), interactionKind))
            {
                throw new ArgumentOutOfRangeException(nameof(interactionKind));
            }

            kind = interactionKind;
            promptText = prompt ?? string.Empty;
        }

        public void ConfigurePromptAnchor(Transform anchor)
        {
            promptAnchor = anchor;
        }

        public bool CanInteract(InteractionContext context)
        {
            return context.Actor != null && isActiveAndEnabled;
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context))
            {
                return;
            }

            InteractionCount++;
            Interacted?.Invoke(context);
        }
    }
}
