using System.Collections.Generic;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcLife;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Player;
using CozyTown.Unity.Time;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class NpcSpriteAnimatorPlayModeTests
    {
        private readonly List<Object> _resources = new List<Object>();
        private GameObject _root;
        private GameObject _world;
        private SpriteRenderer _renderer;
        private CozyTownNpcSpriteAnimator _animator;
        private Sprite[] _idle;
        private Sprite[] _walk;

        [TestCase(0, -1, 0)]
        [TestCase(-1, 0, 1)]
        [TestCase(1, 0, 2)]
        [TestCase(0, 1, 3)]
        public void ApplyStoppedDirection_ShowsTheMatchingIdleSprite(float x, float y, int idleIndex)
        {
            CreateFixture();

            _animator.Apply(new Vector2(x, y), false, 0);

            Assert.That(_renderer.sprite, Is.SameAs(_idle[idleIndex]));
        }

        [Test]
        public void Walking_UsesTwoFramesAtSixFramesPerAcceptedSecondAndFreezesAtZero()
        {
            CreateFixture();

            _animator.Apply(Vector2.down, true, 0);
            Assert.That(_renderer.sprite, Is.SameAs(_walk[0]));

            _animator.Apply(Vector2.down, true, 1d / 6d);
            Assert.That(_renderer.sprite, Is.SameAs(_walk[1]));

            _animator.Apply(Vector2.down, true, 0);
            Assert.That(_renderer.sprite, Is.SameAs(_walk[1]));

            _animator.Apply(Vector2.down, true, 1d / 6d);
            Assert.That(_renderer.sprite, Is.SameAs(_walk[0]));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StopOrRebuild_ShowsIdleAndRestartsTheWalkCycle(bool rebuild)
        {
            CreateFixture();
            _animator.Apply(Vector2.right, true, 1d / 6d);
            Assert.That(_renderer.sprite, Is.SameAs(_walk[5]));

            _animator.Apply(Vector2.right, rebuild, 0, rebuild);
            Assert.That(_renderer.sprite, Is.SameAs(_idle[2]));

            _animator.Apply(Vector2.right, true, 0);
            Assert.That(_renderer.sprite, Is.SameAs(_walk[4]));
        }

        [Test]
        public void Resident_AcceptedTimeDrivesFramesWhilePauseFailureSleepAndLoadKeepTheirSemantics()
        {
            CreateResidentFixture(out var services, out var resident, out var driver, out var gate);
            driver.AdvanceFrame(0);
            driver.AdvanceFrame(0.25);
            Assert.That(resident.Position.x, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(_renderer.sprite, Is.SameAs(_walk[5]));

            var owner = new object();
            Assert.That(gate.TryAcquire(owner), Is.True);
            driver.AdvanceFrame(300);
            driver.SetApplicationFocus(false);
            Assert.That(gate.Release(owner), Is.True);
            driver.AdvanceFrame(300);
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(300);
            Assert.That(resident.Position.x, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(_renderer.sprite, Is.SameAs(_walk[5]));

            var farm = services.Farm.CaptureSnapshot();
            Assert.That(services.Farm.AdvanceDay(2).IsSuccess, Is.True);
            Assert.That(services.DaytimeClock.AdvanceElapsed(1).IsSuccess, Is.False);
            Assert.That(services.Sleep.SleepForMinutes(60).IsSuccess, Is.False);
            Assert.That(services.GameSave.Load().IsSuccess, Is.False);
            Assert.That(resident.Position.x, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(_renderer.sprite, Is.SameAs(_walk[5]));
            Assert.That(services.Farm.Restore(farm).IsSuccess, Is.True);

            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            Assert.That(services.Sleep.SleepForMinutes(60).IsSuccess, Is.True);
            Assert.That(resident.Position, Is.EqualTo(new Vector2(20, 0)));
            Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(_renderer.sprite, Is.SameAs(_idle[2]));

            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(resident.Position, Is.EqualTo(Vector2.zero));
            Assert.That(_renderer.sprite, Is.SameAs(_idle[0]));
        }

        private void CreateResidentFixture(out CozyTownServices services, out NpcWorldResident2D resident,
            out DaytimeClockDriver driver, out PlayerModalInputGate2D gate)
        {
            CreateFixture();
            _world = new GameObject("NPC Animation Town");
            _root.transform.SetParent(_world.transform, false);
            var map = _world.AddComponent<TownMap2D>();
            map.Configure(new[] { new TownHome("home.mina", "npc.mina", "outside", "entry") },
                new[] { new TownLocation("outside", Vector2.zero),
                    new TownLocation("entry", new Vector2(0, 0.5f)),
                    new TownLocation("work", new Vector2(20, 0)),
                    new TownLocation("rest", new Vector2(20, 10)) },
                new[] { new TownRoad("entry", "outside"), new TownRoad("outside", "work"),
                    new TownRoad("work", "rest") });
            resident = _root.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, new NpcDailySchedule("npc.mina", "home.mina", "outside", "entry",
                "work", "rest", "work", 360, 480, 720, 780, 1020, 1080), _renderer);
            services = CozyTownCompositionRoot.CreateDefault();
            var controller = _world.AddComponent<CozyTownTownLifeController>();
            controller.Configure(resident);
            controller.Bind(services.WorldTimeFlow);

            var player = new GameObject("Player");
            player.SetActive(false);
            player.transform.SetParent(_world.transform, false);
            player.AddComponent<Rigidbody2D>().gravityScale = 0;
            var input = player.AddComponent<PlayModePlayerInputSource>();
            player.AddComponent<PlayerMovement2D>().SetInputSource(input);
            var probe = player.AddComponent<InteractionProbe2D>();
            player.AddComponent<PlayerInteractor2D>().Configure(input, probe);
            gate = player.AddComponent<PlayerModalInputGate2D>();
            player.SetActive(true);
            driver = _world.AddComponent<DaytimeClockDriver>();
            driver.Bind(services.DaytimeClock);
            driver.ConfigureInputGate(gate);
            driver.SetApplicationFocus(true);
        }

        private void CreateFixture()
        {
            _root = new GameObject("NPC Animation Fixture");
            _renderer = _root.AddComponent<SpriteRenderer>();
            _animator = _root.AddComponent<CozyTownNpcSpriteAnimator>();
            _idle = new Sprite[4];
            _walk = new Sprite[8];
            for (int index = 0; index < _idle.Length; index++)
            {
                _idle[index] = CreateFrame("Idle " + index, index);
            }
            for (int index = 0; index < _walk.Length; index++)
            {
                _walk[index] = CreateFrame("Walk " + index, index + 4);
            }
            _renderer.sprite = _idle[0];
            _animator.Configure(_renderer, _idle, _walk);
        }

        private Sprite CreateFrame(string name, int colorIndex)
        {
            var texture = new Texture2D(24, 32) { filterMode = FilterMode.Point };
            var pixels = new Color[24 * 32];
            var color = new Color((colorIndex + 1) / 12f, 0.5f, 1f);
            for (int pixel = 0; pixel < pixels.Length; pixel++)
            {
                pixels[pixel] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 24, 32), new Vector2(0.5f, 0), 16);
            sprite.name = name;
            _resources.Add(sprite);
            _resources.Add(texture);
            return sprite;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null) Object.DestroyImmediate(_world);
            else if (_root != null) Object.DestroyImmediate(_root);
            foreach (Object resource in _resources) Object.DestroyImmediate(resource);
            _resources.Clear();
        }
    }
}
