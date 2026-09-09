using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CozyTown.Unity.Lighting
{
    [DisallowMultipleComponent]
    public sealed class TownLamp2D : MonoBehaviour
    {
        [SerializeField] private Light2D _illumination;
        [SerializeField] private SpriteRenderer _glow;
        [SerializeField, Min(0f)] private float _peakIntensity = 1f;

        public void Configure(Light2D illumination, SpriteRenderer glow, float peakIntensity = 1f)
        {
            if (illumination == null) throw new ArgumentNullException(nameof(illumination));
            if (glow == null) throw new ArgumentNullException(nameof(glow));
            if (float.IsNaN(peakIntensity) || float.IsInfinity(peakIntensity) || peakIntensity < 0f)
                throw new ArgumentOutOfRangeException(nameof(peakIntensity));
            _illumination = illumination;
            _glow = glow;
            _peakIntensity = peakIntensity;
        }

        public void Apply(float strength)
        {
            if (float.IsNaN(strength) || float.IsInfinity(strength))
                throw new ArgumentOutOfRangeException(nameof(strength));
            float amount = Mathf.Clamp01(strength);
            _illumination.intensity = amount * _peakIntensity;
            _illumination.enabled = amount > 0f;
            Color glowColor = _glow.color;
            glowColor.a = amount;
            _glow.color = glowColor;
            _glow.enabled = amount > 0f;
        }
    }
}
