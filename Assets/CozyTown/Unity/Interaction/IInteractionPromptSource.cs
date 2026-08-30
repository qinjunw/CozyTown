using UnityEngine;

namespace CozyTown.Unity.Interaction
{
    public interface IInteractionPromptSource
    {
        string PromptText { get; }

        Transform PromptAnchor { get; }
    }
}
