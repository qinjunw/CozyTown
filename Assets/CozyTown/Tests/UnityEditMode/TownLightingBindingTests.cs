using CozyTown.Runtime.Core;
using CozyTown.Unity.Lighting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class TownLightingBindingTests
    {
        private GameObject _root;
        private TownLightingProfile _profile;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_profile);
        }

        [Test]
        public void Bind_ProjectsTheCurrentWorldTimeIntoAmbientLighting()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.Sleep.SleepForMinutes(720).IsSuccess, Is.True);
            Assert.That(services.Sleep.SleepForMinutes(480).IsSuccess, Is.True);
            _root = new GameObject("Lighting test");
            _profile = ScriptableObject.CreateInstance<TownLightingProfile>();
            var ambient = _root.AddComponent<Light2D>();
            ambient.lightType = Light2D.LightType.Global;
            ambient.intensity = 1f;
            ambient.color = Color.white;
            var controller = _root.AddComponent<TownLightingController>();
            controller.Configure(_profile, ambient, new TownLamp2D[0]);

            controller.Bind(services.WorldTimeFlow);

            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(120));
            Assert.That(ambient.intensity, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(ambient.color.b, Is.GreaterThan(ambient.color.r));
        }

        [Test]
        public void ElapsedTime_GraduallyChangesTheWorldAndLampTogether()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.Sleep.SleepForMinutes(720).IsSuccess, Is.True);
            _root = new GameObject("Lighting test");
            _profile = ScriptableObject.CreateInstance<TownLightingProfile>();
            var ambient = _root.AddComponent<Light2D>();
            ambient.lightType = Light2D.LightType.Global;
            var lampObject = new GameObject("Lamp");
            lampObject.transform.SetParent(_root.transform);
            var illumination = lampObject.AddComponent<Light2D>();
            illumination.lightType = Light2D.LightType.Point;
            var glow = lampObject.AddComponent<SpriteRenderer>();
            var lamp = lampObject.AddComponent<TownLamp2D>();
            lamp.Configure(illumination, glow, 0.85f);
            var controller = _root.AddComponent<TownLightingController>();
            controller.Configure(_profile, ambient, new[] { lamp });
            controller.Bind(services.WorldTimeFlow);
            Color dusk = ambient.color;

            Assert.That(services.DaytimeClock.AdvanceElapsed(15).IsSuccess, Is.True);

            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(1110));
            Assert.That(ambient.color.r, Is.LessThan(dusk.r));
            Assert.That(illumination.intensity, Is.EqualTo(0.425f).Within(0.001f));
            Assert.That(glow.color.a, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(illumination.enabled, Is.True);
        }
    }
}
