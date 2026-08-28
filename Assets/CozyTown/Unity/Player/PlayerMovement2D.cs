using System;
using CozyTown.Unity.Input;
using UnityEngine;

namespace CozyTown.Unity.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMovement2D : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour inputSourceBehaviour;
        [SerializeField, Min(0f)] private float speed = 4f;

        private Rigidbody2D _body;
        private IPlayerInputSource _inputSource;

        public Vector2 LastMoveDirection { get; private set; } = Vector2.down;

        public float Speed
        {
            get => speed;
            set => speed = Mathf.Max(0f, value);
        }

        public void SetInputSource(IPlayerInputSource inputSource)
        {
            _inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
            inputSourceBehaviour = inputSource as MonoBehaviour;
        }

        public static Vector2 CalculateVelocity(Vector2 input, float movementSpeed)
        {
            return Vector2.ClampMagnitude(input, 1f) * Mathf.Max(0f, movementSpeed);
        }

        private void Reset()
        {
            _body = GetComponent<Rigidbody2D>();
            inputSourceBehaviour = GetComponent<InputSystemPlayerInputSource>();
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            if (_inputSource == null && !TryResolveInputSource(out var error))
            {
                Debug.LogError($"Player movement could not initialize: {error}", this);
                enabled = false;
            }
        }

        private void FixedUpdate()
        {
            if (_inputSource == null)
            {
                _body.linearVelocity = Vector2.zero;
                return;
            }

            var input = Vector2.ClampMagnitude(_inputSource.Movement, 1f);
            if (input.sqrMagnitude > 0.0001f)
            {
                LastMoveDirection = input.normalized;
            }

            _body.linearVelocity = CalculateVelocity(input, speed);
        }

        private void OnDisable()
        {
            if (_body != null)
            {
                _body.linearVelocity = Vector2.zero;
            }
        }

        private bool TryResolveInputSource(out string error)
        {
            if (inputSourceBehaviour is IPlayerInputSource inputSource)
            {
                _inputSource = inputSource;
                error = null;
                return true;
            }

            error = $"{nameof(inputSourceBehaviour)} must implement {nameof(IPlayerInputSource)}.";
            return false;
        }
    }
}
