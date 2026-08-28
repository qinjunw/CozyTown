using UnityEngine;

namespace CozyTown.Unity.Interaction
{
    public sealed class InteractionProbe2D : MonoBehaviour
    {
        [SerializeField] private Transform probeOrigin;
        [SerializeField, Min(0f)] private float radius = 0.75f;
        [SerializeField] private LayerMask interactionLayers = ~0;

        public bool TryFindClosest(
            InteractionContext context,
            out IInteractable interactable)
        {
            interactable = null;
            if (context.Actor == null)
            {
                return false;
            }

            var origin = probeOrigin != null
                ? (Vector2)probeOrigin.position
                : (Vector2)transform.position;
            var colliders = Physics2D.OverlapCircleAll(origin, radius, interactionLayers);
            var closestDistance = float.PositiveInfinity;
            var closestId = ulong.MaxValue;

            foreach (var collider in colliders)
            {
                var candidate = FindInteractable(collider, out var candidateBehaviour);
                if (candidate == null || !candidateBehaviour.isActiveAndEnabled)
                {
                    continue;
                }

                if (candidateBehaviour.transform.IsChildOf(context.Actor.transform))
                {
                    continue;
                }

                if (!candidate.CanInteract(context))
                {
                    continue;
                }

                var closestPoint = collider.ClosestPoint(origin);
                var distance = (closestPoint - origin).sqrMagnitude;
                var candidateId = EntityId.ToULong(candidateBehaviour.GetEntityId());
                var isCloser = distance < closestDistance;
                var winsTie = Mathf.Approximately(distance, closestDistance)
                    && candidateId < closestId;
                if (!isCloser && !winsTie)
                {
                    continue;
                }

                closestDistance = distance;
                closestId = candidateId;
                interactable = candidate;
            }

            return interactable != null;
        }

        private static IInteractable FindInteractable(
            Component collider,
            out MonoBehaviour interactableBehaviour)
        {
            var behaviours = collider.GetComponentsInParent<MonoBehaviour>(true);
            foreach (var behaviour in behaviours)
            {
                if (behaviour is IInteractable interactable)
                {
                    interactableBehaviour = behaviour;
                    return interactable;
                }
            }

            interactableBehaviour = null;
            return null;
        }

        private void OnDrawGizmosSelected()
        {
            var origin = probeOrigin != null ? probeOrigin.position : transform.position;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(origin, radius);
        }
    }
}
