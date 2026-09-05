using System;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    [DisallowMultipleComponent]
    public sealed class CozyTownNpcSpriteAnimator : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite[] idleSprites = new Sprite[4];
        [SerializeField] private Sprite[] walkSprites = new Sprite[8];
        private int _directionIndex;
        private double _walkPhase;

        public void Configure(
            SpriteRenderer renderer,
            Sprite[] directionalIdleSprites,
            Sprite[] directionalWalkSprites)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            Sprite[] idle = CopySprites(directionalIdleSprites, 4, nameof(directionalIdleSprites));
            Sprite[] walk = CopySprites(directionalWalkSprites, 8, nameof(directionalWalkSprites));
            spriteRenderer = renderer;
            idleSprites = idle;
            walkSprites = walk;
            _directionIndex = 0;
            _walkPhase = 0;
            spriteRenderer.sprite = idleSprites[0];
        }

        public void Apply(Vector2 direction, bool isWalking, double acceptedSeconds, bool rebuild = false)
        {
            if (acceptedSeconds < 0 || double.IsNaN(acceptedSeconds) || double.IsInfinity(acceptedSeconds))
                throw new ArgumentOutOfRangeException(nameof(acceptedSeconds));
            int directionIndex = Mathf.Abs(direction.x) > Mathf.Abs(direction.y)
                ? (direction.x < 0 ? 1 : 2)
                : (direction.y > 0 ? 3 : 0);
            if (rebuild || !isWalking || directionIndex != _directionIndex)
            {
                _walkPhase = 0;
            }
            _directionIndex = directionIndex;
            if (rebuild || !isWalking)
            {
                spriteRenderer.sprite = idleSprites[directionIndex];
                return;
            }
            _walkPhase = (_walkPhase + acceptedSeconds * 6) % 2;
            spriteRenderer.sprite = walkSprites[directionIndex * 2 + (int)_walkPhase];
        }

        private static Sprite[] CopySprites(Sprite[] source, int length, string parameterName)
        {
            if (source == null || source.Length != length)
                throw new ArgumentException($"Expected {length} non-null Sprites.", parameterName);
            foreach (Sprite sprite in source)
            {
                if (sprite == null)
                    throw new ArgumentException($"Expected {length} non-null Sprites.", parameterName);
            }
            return (Sprite[])source.Clone();
        }
    }
}
