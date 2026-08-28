using System;
using UnityEngine;

namespace CozyTown.Unity.Interaction
{
    public readonly struct InteractionContext
    {
        public InteractionContext(GameObject actor)
        {
            Actor = actor != null ? actor : throw new ArgumentNullException(nameof(actor));
        }

        public GameObject Actor { get; }
    }
}
