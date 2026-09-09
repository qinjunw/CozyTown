using System;
using CozyTown.Unity.Lighting;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class TownLightingProfileTests
    {
        private TownLightingProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<TownLightingProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_profile);
        }

        [Test]
        public void Evaluate_DaytimeIsBrighterAndWarmerThanDeepNight()
        {
            TownLightingSample day = _profile.Evaluate(540);
            TownLightingSample deepNight = _profile.Evaluate(120);

            Assert.That(day.AmbientIntensity, Is.GreaterThan(deepNight.AmbientIntensity + 0.4f));
            Assert.That(day.AmbientColor.r, Is.GreaterThan(day.AmbientColor.b));
            Assert.That(deepNight.AmbientColor.b, Is.GreaterThan(deepNight.AmbientColor.r));
        }

        [TestCase(0)]
        [TestCase(120)]
        [TestCase(330)]
        [TestCase(540)]
        [TestCase(900)]
        [TestCase(1080)]
        [TestCase(1200)]
        public void Evaluate_AroundEveryColorKeyframeAndMidnight_HasNoVisibleJump(int minute)
        {
            TownLightingSample before = _profile.Evaluate(minute - 0.01);
            TownLightingSample after = _profile.Evaluate(minute + 0.01);

            Assert.That(Mathf.Abs(after.AmbientIntensity - before.AmbientIntensity), Is.LessThan(0.001f));
            Assert.That(ColorDistance(before.AmbientColor, after.AmbientColor), Is.LessThan(0.001f));
        }

        [Test]
        public void Evaluate_DawnStartsAndFinishesChangingGradually()
        {
            float dawn = _profile.Evaluate(330).AmbientIntensity;
            float afterDawn = _profile.Evaluate(331).AmbientIntensity;
            float beforeDay = _profile.Evaluate(539).AmbientIntensity;
            float day = _profile.Evaluate(540).AmbientIntensity;

            Assert.That(afterDawn - dawn, Is.InRange(0.000001f, 0.0001f));
            Assert.That(day - beforeDay, Is.InRange(0.000001f, 0.0001f));
            Assert.That(_profile.Evaluate(435).AmbientIntensity, Is.InRange(0.8f, 0.85f));
        }

        [Test]
        public void Evaluate_FractionalMinutes_ContinueChangingBetweenWholeMinutes()
        {
            TownLightingSample first = _profile.Evaluate(400.25);
            TownLightingSample second = _profile.Evaluate(400.75);

            Assert.That(second.AmbientIntensity, Is.GreaterThan(first.AmbientIntensity));
            Assert.That(ColorDistance(first.AmbientColor, second.AmbientColor), Is.GreaterThan(0.0001f));
        }

        [TestCase(-1319.75)]
        [TestCase(120.25)]
        [TestCase(1560.25)]
        public void Evaluate_EquivalentTimesAcrossDays_HaveTheSameColorAndBrightness(double minute)
        {
            TownLightingSample expected = _profile.Evaluate(120.25);
            TownLightingSample actual = _profile.Evaluate(minute);

            Assert.That(actual.AmbientColor, Is.EqualTo(expected.AmbientColor));
            Assert.That(actual.AmbientIntensity, Is.EqualTo(expected.AmbientIntensity));
            Assert.That(actual.LampStrength, Is.EqualTo(expected.LampStrength));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Evaluate_NonFiniteTime_RejectsTheInput(double minute)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _profile.Evaluate(minute));
        }

        [Test]
        public void Evaluate_LampsFadeInBeforeNightAndOutAfterDawn()
        {
            Assert.That(_profile.Evaluate(1080).LampStrength, Is.EqualTo(0f));
            Assert.That(_profile.Evaluate(1110).LampStrength, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(_profile.Evaluate(1140).LampStrength, Is.EqualTo(1f));
            Assert.That(_profile.Evaluate(0).LampStrength, Is.EqualTo(1f));
            Assert.That(_profile.Evaluate(300).LampStrength, Is.EqualTo(1f));
            Assert.That(_profile.Evaluate(330).LampStrength, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(_profile.Evaluate(360).LampStrength, Is.EqualTo(0f));
            Assert.That(_profile.Evaluate(720).LampStrength, Is.EqualTo(0f));
        }

        [TestCase(300)]
        [TestCase(360)]
        [TestCase(1080)]
        [TestCase(1140)]
        public void Evaluate_LampFadeBoundaries_StartAndFinishGradually(int minute)
        {
            float before = _profile.Evaluate(minute - 1).LampStrength;
            float after = _profile.Evaluate(minute + 1).LampStrength;

            Assert.That(Mathf.Abs(after - before), Is.InRange(0.00001f, 0.001f));
        }

        private static float ColorDistance(Color first, Color second)
        {
            return Vector3.Distance(new Vector3(first.r, first.g, first.b), new Vector3(second.r, second.g, second.b));
        }
    }
}
