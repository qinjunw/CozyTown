using System;
using CozyTown.Runtime.Time;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CozyTown.Unity.Lighting
{
    [DisallowMultipleComponent]
    public sealed class TownLightingController : MonoBehaviour
    {
        [SerializeField] private TownLightingProfile _profile;
        [SerializeField] private Light2D _ambientLight;
        [SerializeField] private TownLamp2D[] _lamps = Array.Empty<TownLamp2D>();
        private IWorldTimeFlow _timeFlow;

        public void Configure(TownLightingProfile profile, Light2D ambientLight, TownLamp2D[] lamps)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (ambientLight == null) throw new ArgumentNullException(nameof(ambientLight));
            if (ambientLight.lightType != Light2D.LightType.Global)
                throw new ArgumentException("Ambient lighting requires a global 2D light.", nameof(ambientLight));
            if (lamps == null || Array.Exists(lamps, lamp => lamp == null))
                throw new ArgumentException("The lamp collection must not contain null references.", nameof(lamps));
            _profile = profile;
            _ambientLight = ambientLight;
            _lamps = (TownLamp2D[])lamps.Clone();
            if (_timeFlow != null) Apply(_timeFlow.Current);
        }

        public void Bind(IWorldTimeFlow timeFlow)
        {
            if (timeFlow == null) throw new ArgumentNullException(nameof(timeFlow));
            if (_profile == null || _ambientLight == null)
                throw new InvalidOperationException("Lighting requires a profile and global light before binding time.");
            if (_timeFlow != null) _timeFlow.PresentationChanged -= Apply;
            _timeFlow = timeFlow;
            Apply(_timeFlow.Current);
            _timeFlow.PresentationChanged += Apply;
        }

        // Pause belongs to the shared clock. Explicit sleep/load still updates a disabled view.
        private void OnDestroy()
        {
            if (_timeFlow != null) _timeFlow.PresentationChanged -= Apply;
        }

        private void Apply(WorldTimeProgress progress)
        {
            var sample = _profile.Evaluate(progress.Clock.MinuteOfDay + progress.FractionalMinute);
            _ambientLight.color = sample.AmbientColor;
            _ambientLight.intensity = sample.AmbientIntensity;
            foreach (var lamp in _lamps) lamp.Apply(sample.LampStrength);
        }
    }
}
