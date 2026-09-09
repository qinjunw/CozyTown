using System;
using UnityEngine;

namespace CozyTown.Unity.Lighting
{
    [CreateAssetMenu(fileName = "TownLightingProfile", menuName = "CozyTown/Town Lighting Profile")]
    public sealed class TownLightingProfile : ScriptableObject
    {
        [SerializeField] private TownLightingKeyframe[] _keyframes =
        {
            new TownLightingKeyframe("Deep night", 120, new Color(0.54f, 0.62f, 0.9f), 0.35f),
            new TownLightingKeyframe("Dawn", 330, new Color(1f, 0.79f, 0.74f), 0.65f),
            new TownLightingKeyframe("Day", 540, new Color(1f, 0.98f, 0.94f), 1f),
            new TownLightingKeyframe("Afternoon", 900, new Color(1f, 0.91f, 0.75f), 0.95f),
            new TownLightingKeyframe("Dusk", 1080, new Color(1f, 0.65f, 0.48f), 0.67f),
            new TownLightingKeyframe("Night", 1200, new Color(0.65f, 0.67f, 1f), 0.48f)
        };
        [SerializeField, Range(0, 1439)] private int _lampFadeInStartMinute = 1080;
        [SerializeField, Range(0, 1439)] private int _lampFadeInEndMinute = 1140;
        [SerializeField, Range(0, 1439)] private int _lampFadeOutStartMinute = 300;
        [SerializeField, Range(0, 1439)] private int _lampFadeOutEndMinute = 360;

        public TownLightingSample Evaluate(double minuteOfDay)
        {
            if (double.IsNaN(minuteOfDay) || double.IsInfinity(minuteOfDay))
            {
                throw new ArgumentOutOfRangeException(nameof(minuteOfDay), "Time must be finite.");
            }

            ValidateConfiguration();
            double minute = (minuteOfDay % 1440 + 1440) % 1440;
            for (int index = 0; index < _keyframes.Length; index++)
            {
                TownLightingKeyframe current = _keyframes[index];
                TownLightingKeyframe next = _keyframes[(index + 1) % _keyframes.Length];
                double start = current.MinuteOfDay;
                double end = next.MinuteOfDay;
                double sampleMinute = minute;
                if (end <= start)
                {
                    end += 1440;
                    if (sampleMinute < start)
                    {
                        sampleMinute += 1440;
                    }
                }

                if (sampleMinute < start || sampleMinute > end)
                {
                    continue;
                }

                float blend = Mathf.SmoothStep(0f, 1f, (float)((sampleMinute - start) / (end - start)));
                return new TownLightingSample(
                    Color.Lerp(current.Color, next.Color, blend),
                    Mathf.Lerp(current.Intensity, next.Intensity, blend),
                    EvaluateLampStrength(minute));
            }

            throw new InvalidOperationException("Lighting keyframes must cover the daily cycle.");
        }

        private float EvaluateLampStrength(double minute)
        {
            if (minute < _lampFadeOutStartMinute || minute >= _lampFadeInEndMinute)
            {
                return 1f;
            }

            if (minute < _lampFadeOutEndMinute)
            {
                float progress = (float)((minute - _lampFadeOutStartMinute)
                    / (_lampFadeOutEndMinute - _lampFadeOutStartMinute));
                return 1f - Mathf.SmoothStep(0f, 1f, progress);
            }

            if (minute < _lampFadeInStartMinute)
            {
                return 0f;
            }

            float fadeInProgress = (float)((minute - _lampFadeInStartMinute)
                / (_lampFadeInEndMinute - _lampFadeInStartMinute));
            return Mathf.SmoothStep(0f, 1f, fadeInProgress);
        }

        private void ValidateConfiguration()
        {
            if (_keyframes == null || _keyframes.Length < 2)
            {
                throw new InvalidOperationException("Lighting requires at least two keyframes.");
            }

            int previousMinute = -1;
            foreach (TownLightingKeyframe keyframe in _keyframes)
            {
                if (keyframe == null || keyframe.MinuteOfDay <= previousMinute || keyframe.MinuteOfDay >= 1440)
                {
                    throw new InvalidOperationException("Lighting keyframes must have increasing times from 00:00 through 23:59.");
                }

                if (!IsFinite(keyframe.Intensity) || keyframe.Intensity < 0f
                    || !IsFinite(keyframe.Color.r) || !IsFinite(keyframe.Color.g)
                    || !IsFinite(keyframe.Color.b) || !IsFinite(keyframe.Color.a))
                {
                    throw new InvalidOperationException("Lighting colors must be finite and intensities must be finite and non-negative.");
                }

                previousMinute = keyframe.MinuteOfDay;
            }

            if (_lampFadeOutStartMinute < 0 || _lampFadeOutStartMinute >= _lampFadeOutEndMinute
                || _lampFadeOutEndMinute > _lampFadeInStartMinute
                || _lampFadeInStartMinute >= _lampFadeInEndMinute || _lampFadeInEndMinute >= 1440)
            {
                throw new InvalidOperationException("Lamp fade windows must finish at dawn before they start again at dusk.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public sealed class TownLightingKeyframe
    {
        [SerializeField] private string _name;
        [SerializeField, Range(0, 1439)] private int _minuteOfDay;
        [SerializeField] private Color _color;
        [SerializeField, Min(0f)] private float _intensity;

        public string Name => _name;
        public int MinuteOfDay => _minuteOfDay;
        public Color Color => _color;
        public float Intensity => _intensity;

        public TownLightingKeyframe(string name, int minuteOfDay, Color color, float intensity)
        {
            _name = name;
            _minuteOfDay = minuteOfDay;
            _color = color;
            _intensity = intensity;
        }
    }

    public readonly struct TownLightingSample
    {
        public Color AmbientColor { get; }
        public float AmbientIntensity { get; }
        public float LampStrength { get; }

        public TownLightingSample(Color ambientColor, float ambientIntensity, float lampStrength)
        {
            AmbientColor = ambientColor;
            AmbientIntensity = ambientIntensity;
            LampStrength = lampStrength;
        }
    }
}
