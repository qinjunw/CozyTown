using System;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Town;
using System.Text;
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
        private PlayerBodySnapshot _initialSnapshot;

        public Vector2 LastMoveDirection { get; private set; } = Vector2.down;

        public PlayerBodySnapshot CaptureSnapshot()
        {
            var body = _body != null ? _body : GetComponent<Rigidbody2D>();
            Vector2 position = body != null ? body.position : (Vector2)transform.position;
            return new PlayerBodySnapshot(new Position2DSnapshot(position.x, position.y),
                new Position2DSnapshot(LastMoveDirection.x, LastMoveDirection.y));
        }

        public PlayerBodySnapshot CaptureInitialSnapshot()
            => _initialSnapshot ?? (_initialSnapshot = CaptureSnapshot());

        public static void ValidateSnapshot(PlayerBodySnapshot snapshot)
        {
            bool Finite(Position2DSnapshot value) => value != null
                && !float.IsNaN(value.X) && !float.IsInfinity(value.X)
                && !float.IsNaN(value.Y) && !float.IsInfinity(value.Y);
            if (snapshot == null || !Finite(snapshot.Position) || !Finite(snapshot.Facing)
                || Mathf.Abs(new Vector2(snapshot.Facing.X, snapshot.Facing.Y).sqrMagnitude - 1f) > 0.0001f)
                throw new ArgumentException("Saved player requires a finite position and a unit facing direction.", nameof(snapshot));
        }

        public void RestoreSnapshot(PlayerBodySnapshot snapshot)
        {
            ValidateSnapshot(snapshot);
            CaptureInitialSnapshot();
            _body = _body != null ? _body : GetComponent<Rigidbody2D>();
            var position = new Vector2(snapshot.Position.X, snapshot.Position.Y);
            _body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            LastMoveDirection = new Vector2(snapshot.Facing.X, snapshot.Facing.Y);
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0;
        }

        public string CaptureConfiguration()
        {
            var initial = CaptureInitialSnapshot();
            ValidateSnapshot(initial);
            var result = new StringBuilder();
            TownMap2D.AppendConfiguration(result, "player-body-v1", speed,
                initial.Position.X, initial.Position.Y, initial.Facing.X, initial.Facing.Y);
            return result.ToString();
        }

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
            CaptureInitialSnapshot();
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
