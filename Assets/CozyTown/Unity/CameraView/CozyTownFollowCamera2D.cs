using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CozyTown.Unity.CameraView
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CozyTownFollowCamera2D : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Rect worldBounds;

        private Camera _camera;
        private PixelPerfectCamera _pixelPerfect;

        public void Configure(Transform followTarget, Rect bounds)
        {
            if (followTarget == null)
            {
                throw new ArgumentNullException(nameof(followTarget));
            }

            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(bounds),
                    "Camera world bounds must have positive width and height.");
            }

            target = followTarget;
            worldBounds = bounds;
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _pixelPerfect = GetComponent<PixelPerfectCamera>();
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            var position = target.position;
            var halfHeight = _camera.orthographicSize;
            var halfWidth = halfHeight * _camera.aspect;
            if (_pixelPerfect != null && _pixelPerfect.isActiveAndEnabled &&
                _pixelPerfect.cropFrame == PixelPerfectCamera.CropFrame.Windowbox &&
                _pixelPerfect.gridSnapping == PixelPerfectCamera.GridSnapping.UpscaleRenderTexture)
            {
                // Windowboxing keeps the native viewport fixed while the output window is resized.
                halfWidth = _pixelPerfect.refResolutionX / (2f * _pixelPerfect.assetsPPU);
                halfHeight = _pixelPerfect.refResolutionY / (2f * _pixelPerfect.assetsPPU);
            }

            position.x = worldBounds.width <= halfWidth * 2f
                ? worldBounds.center.x
                : Mathf.Clamp(position.x, worldBounds.xMin + halfWidth, worldBounds.xMax - halfWidth);
            position.y = worldBounds.height <= halfHeight * 2f
                ? worldBounds.center.y
                : Mathf.Clamp(position.y, worldBounds.yMin + halfHeight, worldBounds.yMax - halfHeight);
            position.z = transform.position.z;
            transform.position = position;
        }
    }
}
