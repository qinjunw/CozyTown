using UnityEngine;

namespace CozyTown.Unity.Input
{
    public interface IPlayerInputSource
    {
        Vector2 Movement { get; }

        bool InteractPressedThisFrame { get; }
    }
}
