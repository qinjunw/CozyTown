using System.Collections;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Core;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Lighting;
using CozyTown.Unity.Player;
using CozyTown.Unity.Time;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class TownLightingLifecyclePlayModeTests
    {
        private GameObject _root;
        private TownLightingProfile _profile;
        private TownLightingController _controller;
        private Light2D _ambient;
        private Light2D _illumination;

        [TestCase(false)]
        [TestCase(true)]
        public void Bootstrap_RegistersLightingBeforeOrAfterInitialization(bool registerLate)
        {
            CreateLighting();
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.Sleep.SleepForMinutes(720).IsSuccess, Is.True);
            Assert.That(services.Sleep.SleepForMinutes(480).IsSuccess, Is.True);
            var bootstrapObject = new GameObject("Bootstrap");
            bootstrapObject.transform.SetParent(_root.transform);
            bootstrapObject.SetActive(false);
            var bootstrap = bootstrapObject.AddComponent<CozyTownBootstrap>();
            if (!registerLate) bootstrap.RegisterTownLighting(_controller);
            bootstrap.Initialize(services);
            if (registerLate) bootstrap.RegisterTownLighting(_controller);
            bootstrapObject.SetActive(true);

            Assert.That(_ambient.intensity, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(_illumination.enabled, Is.True);
            Assert.That(services.Sleep.SleepForMinutes(240).IsSuccess, Is.True);
            Assert.That(_ambient.intensity, Is.GreaterThan(0.65f));
            Assert.That(_illumination.enabled, Is.False);
        }

        [Test]
        public void SleepAndLoad_RestoreNightLightingEvenWhenTheControllerIsDisabled()
        {
            CreateLighting();
            var services = CozyTownCompositionRoot.CreateDefault();
            _controller.Bind(services.WorldTimeFlow);
            Assert.That(services.Sleep.SleepForMinutes(720).IsSuccess, Is.True);
            Assert.That(services.Sleep.SleepForMinutes(360).IsSuccess, Is.True);
            Assert.That(_illumination.enabled, Is.True);
            Color midnightColor = _ambient.color;
            float midnightIntensity = _ambient.intensity;
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);

            _controller.enabled = false;
            Assert.That(services.Sleep.SleepForMinutes(360).IsSuccess, Is.True);
            Assert.That(_illumination.enabled, Is.False);
            Assert.That(services.GameSave.Load().IsSuccess, Is.True);

            Assert.That(services.Time.Current.MinuteOfDay, Is.Zero);
            Assert.That(_ambient.color, Is.EqualTo(midnightColor));
            Assert.That(_ambient.intensity, Is.EqualTo(midnightIntensity));
            Assert.That(_illumination.intensity, Is.EqualTo(0.85f));
            Assert.That(_illumination.enabled, Is.True);
        }

        [Test]
        public void FailedLoad_PreservesTheCurrentLighting()
        {
            CreateLighting();
            var services = CozyTownCompositionRoot.Create(
                DefaultMvpContent.CreateConfiguration(), npcDialogue: null, saveStorage: new ReadFailingStorage());
            _controller.Bind(services.WorldTimeFlow);
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            Assert.That(services.Sleep.SleepForMinutes(720).IsSuccess, Is.True);
            Assert.That(services.DaytimeClock.AdvanceElapsed(15).IsSuccess, Is.True);
            Color color = _ambient.color;
            float intensity = _illumination.intensity;

            var result = services.GameSave.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.read_failed"));
            Assert.That(_ambient.color, Is.EqualTo(color));
            Assert.That(_illumination.intensity, Is.EqualTo(intensity));
            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(1110));
        }

        [UnityTest]
        public IEnumerator RebindingAndDestruction_StopListeningToThePreviousSession()
        {
            CreateLighting();
            var first = CozyTownCompositionRoot.CreateDefault();
            var second = CozyTownCompositionRoot.CreateDefault();
            _controller.Bind(first.WorldTimeFlow);
            _controller.Bind(second.WorldTimeFlow);
            Color morning = _ambient.color;

            Assert.That(first.Sleep.SleepForMinutes(720).IsSuccess, Is.True);
            Assert.That(first.DaytimeClock.AdvanceElapsed(15).IsSuccess, Is.True);
            Assert.That(_ambient.color, Is.EqualTo(morning));
            Assert.That(_illumination.enabled, Is.False);

            Object.Destroy(_controller);
            yield return null;
            Assert.That(second.Sleep.SleepForMinutes(720).IsSuccess, Is.True);
            Assert.That(second.DaytimeClock.AdvanceElapsed(15).IsSuccess, Is.True);
            Assert.That(_ambient.color, Is.EqualTo(morning));
            Assert.That(_illumination.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator ModalAndFocusPause_StopLightingWhileFractionalTimeRemainsSmooth()
        {
            CreateLighting();
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.Sleep.SleepForMinutes(720).IsSuccess, Is.True);
            Assert.That(services.DaytimeClock.AdvanceElapsed(15).IsSuccess, Is.True);
            _controller.Bind(services.WorldTimeFlow);
            var actor = new GameObject("Player");
            actor.transform.SetParent(_root.transform);
            actor.SetActive(false);
            actor.AddComponent<Rigidbody2D>().gravityScale = 0;
            var input = actor.AddComponent<PlayModePlayerInputSource>();
            actor.AddComponent<PlayerMovement2D>().SetInputSource(input);
            var probe = actor.AddComponent<InteractionProbe2D>();
            actor.AddComponent<PlayerInteractor2D>().Configure(input, probe);
            var gate = actor.AddComponent<PlayerModalInputGate2D>();
            actor.SetActive(true);
            var driver = _root.AddComponent<DaytimeClockDriver>();
            driver.ConfigureInputGate(gate);
            driver.Bind(services.DaytimeClock);
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            float before = _illumination.intensity;

            driver.AdvanceFrame(0.25);

            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(1110));
            Assert.That(_illumination.intensity, Is.GreaterThan(before));
            Color pausedColor = _ambient.color;
            float pausedIntensity = _illumination.intensity;
            var owner = new object();
            Assert.That(gate.TryAcquire(owner), Is.True);
            driver.AdvanceFrame(60);
            yield return null;
            Assert.That(_ambient.color, Is.EqualTo(pausedColor));
            Assert.That(_illumination.intensity, Is.EqualTo(pausedIntensity));
            Assert.That(gate.Release(owner), Is.True);
            driver.SetApplicationFocus(false);
            driver.AdvanceFrame(60);
            yield return null;
            Assert.That(_ambient.color, Is.EqualTo(pausedColor));
            Assert.That(_illumination.intensity, Is.EqualTo(pausedIntensity));
        }

        private void CreateLighting()
        {
            _root = new GameObject("Lighting lifecycle");
            _profile = ScriptableObject.CreateInstance<TownLightingProfile>();
            _ambient = _root.AddComponent<Light2D>();
            _ambient.lightType = Light2D.LightType.Global;
            var lampObject = new GameObject("Lamp");
            lampObject.transform.SetParent(_root.transform);
            _illumination = lampObject.AddComponent<Light2D>();
            _illumination.lightType = Light2D.LightType.Point;
            var glow = lampObject.AddComponent<SpriteRenderer>();
            var lamp = lampObject.AddComponent<TownLamp2D>();
            lamp.Configure(_illumination, glow, 0.85f);
            _controller = _root.AddComponent<TownLightingController>();
            _controller.Configure(_profile, _ambient, new[] { lamp });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_profile);
        }

        private sealed class ReadFailingStorage : ISaveStorage
        {
            private readonly InMemorySaveStorage _storage = new InMemorySaveStorage();
            public bool Exists(string slotId) => _storage.Exists(slotId);
            public OperationResult Save(string slotId, GameSaveSnapshot snapshot) => _storage.Save(slotId, snapshot);
            public OperationResult<GameSaveSnapshot> Load(string slotId) =>
                OperationResult<GameSaveSnapshot>.Failure("save.read_failed");
        }
    }
}
