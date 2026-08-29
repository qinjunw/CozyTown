using System;
using UnityEngine;

namespace CozyTown.Unity.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement2D), typeof(Rigidbody2D))]
    public sealed class CozyTownPlayerSpriteAnimator : MonoBehaviour
    {
        private const int DirectionCount = 4;
        private const int WalkFramesPerDirection = 2;

        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private PlayerMovement2D movement;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Sprite[] idleSprites = new Sprite[DirectionCount];
        [SerializeField] private Sprite[] walkSprites = new Sprite[DirectionCount * WalkFramesPerDirection];
        [SerializeField, Min(0.01f)] private float framesPerSecond = 6f;

        private int _directionIndex;
        private int _walkFrame;
        private float _elapsed;

        public void Configure(
            SpriteRenderer renderer,
            PlayerMovement2D playerMovement,
            Rigidbody2D rigidbody,
            Sprite[] directionalIdleSprites,
            Sprite[] directionalWalkSprites,
            float animationFramesPerSecond = 6f)
        {
            spriteRenderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            movement = playerMovement ?? throw new ArgumentNullException(nameof(playerMovement));
            body = rigidbody ?? throw new ArgumentNullException(nameof(rigidbody));
            idleSprites = CopyAndValidate(
                directionalIdleSprites,
                DirectionCount,
                nameof(directionalIdleSprites));
            walkSprites = CopyAndValidate(
                directionalWalkSprites,
                DirectionCount * WalkFramesPerDirection,
                nameof(directionalWalkSprites));
            framesPerSecond = Mathf.Max(0.01f, animationFramesPerSecond);
            _directionIndex = DirectionIndex(movement.LastMoveDirection);
            _walkFrame = 0;
            _elapsed = 0f;
            Refresh(0f);
        }

        public void Refresh(float deltaTime)
        {
            if (!HasValidConfiguration())
            {
                return;
            }

            var velocity = body.linearVelocity;
            bool isWalking = velocity.sqrMagnitude > 0.0001f;
            int nextDirection = DirectionIndex(isWalking ? velocity : movement.LastMoveDirection);
            if (nextDirection != _directionIndex)
            {
                _directionIndex = nextDirection;
                _walkFrame = 0;
                _elapsed = 0f;
            }

            if (!isWalking)
            {
                _walkFrame = 0;
                _elapsed = 0f;
                spriteRenderer.sprite = idleSprites[_directionIndex];
                return;
            }

            _elapsed += Mathf.Max(0f, deltaTime);
            float frameDuration = 1f / framesPerSecond;
            while (_elapsed >= frameDuration)
            {
                _elapsed -= frameDuration;
                _walkFrame = (_walkFrame + 1) % WalkFramesPerDirection;
            }

            spriteRenderer.sprite = walkSprites[(_directionIndex * WalkFramesPerDirection) + _walkFrame];
        }

        private void Reset()
        {
            movement = GetComponent<PlayerMovement2D>();
            body = GetComponent<Rigidbody2D>();
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        private void Awake()
        {
            movement ??= GetComponent<PlayerMovement2D>();
            body ??= GetComponent<Rigidbody2D>();
            spriteRenderer ??= GetComponentInChildren<SpriteRenderer>(true);
            if (!HasValidConfiguration())
            {
                Debug.LogError("Player sprite animator requires four idle and eight walk Sprites.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            Refresh(Time.deltaTime);
        }

        private bool HasValidConfiguration()
        {
            return spriteRenderer != null
                && movement != null
                && body != null
                && HasSprites(idleSprites, DirectionCount)
                && HasSprites(walkSprites, DirectionCount * WalkFramesPerDirection);
        }

        private static int DirectionIndex(Vector2 direction)
        {
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
            {
                return direction.x < 0f ? 1 : 2;
            }

            return direction.y > 0f ? 3 : 0;
        }

        private static Sprite[] CopyAndValidate(Sprite[] source, int expectedLength, string parameterName)
        {
            if (!HasSprites(source, expectedLength))
            {
                throw new ArgumentException(
                    $"Expected {expectedLength} non-null Sprites.",
                    parameterName);
            }

            return (Sprite[])source.Clone();
        }

        private static bool HasSprites(Sprite[] sprites, int expectedLength)
        {
            if (sprites == null || sprites.Length != expectedLength)
            {
                return false;
            }

            foreach (var sprite in sprites)
            {
                if (sprite == null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
