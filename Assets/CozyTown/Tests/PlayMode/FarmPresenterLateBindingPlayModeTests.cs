using System.Collections;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Unity.Core;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class FarmPresenterLateBindingPlayModeTests
    {
        private GameObject _actor;
        private GameObject _bootstrapObject;
        private GameObject _pointObject;
        private GameObject _presenterObject;

        [UnityTest]
        public IEnumerator RegisterAfterStart_BindsAndInvokesCommandOnce()
        {
            var coordinator = new StubFarmGameplayCoordinator();
            var point = CreatePoint();
            var presenter = CreatePresenter(point, out var view);

            yield return null;
            Assert.That(presenter.enabled, Is.True);

            _bootstrapObject = new GameObject("Bootstrap");
            _bootstrapObject.SetActive(false);
            var bootstrap = _bootstrapObject.AddComponent<CozyTownBootstrap>();
            bootstrap.SetFactory(new TestServicesFactory(CreateServices(coordinator)));
            _bootstrapObject.SetActive(true);
            Assert.That(bootstrap.IsInitialized, Is.True);
            bootstrap.RegisterFarmPresenter(presenter);

            CreateActor();
            point.Interact(new InteractionContext(_actor));
            yield return null;
            Assert.That(presenter.IsOpen, Is.True);
            Assert.That(view.IsVisible, Is.True);

            view.RequestPlant("plot.01", "seed.potato");
            Assert.That(coordinator.PlantCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator MissingConfiguration_RemainsSafeAndEnabled()
        {
            _presenterObject = new GameObject("Unconfigured Farm Presenter");
            var presenter = _presenterObject.AddComponent<CozyTownFarmDebugPresenter>();

            yield return null;

            Assert.That(presenter.enabled, Is.True);
            Assert.That(presenter.IsOpen, Is.False);
        }

        [TearDown]
        public void TearDown()
        {
            Destroy(_presenterObject);
            Destroy(_pointObject);
            Destroy(_actor);
            Destroy(_bootstrapObject);
        }

        private TownInteractionPoint2D CreatePoint()
        {
            _pointObject = new GameObject("Farm Point");
            var point = _pointObject.AddComponent<TownInteractionPoint2D>();
            point.Configure(TownInteractionKind.Farm, "Open farm");
            return point;
        }

        private CozyTownFarmDebugPresenter CreatePresenter(
            TownInteractionPoint2D point,
            out CozyTownFarmDebugView view)
        {
            _presenterObject = new GameObject("Farm UI");
            view = _presenterObject.AddComponent<CozyTownFarmDebugView>();
            var presenter = _presenterObject.AddComponent<CozyTownFarmDebugPresenter>();
            presenter.Configure(point, view);
            return presenter;
        }

        private void CreateActor()
        {
            _actor = new GameObject("Actor");
            _actor.SetActive(false);
            _actor.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var input = _actor.AddComponent<PlayModePlayerInputSource>();
            var movement = _actor.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);
            var probe = _actor.AddComponent<InteractionProbe2D>();
            var interactor = _actor.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);
            _actor.AddComponent<PlayerModalInputGate2D>();
            _actor.SetActive(true);
        }

        private static CozyTownServices CreateServices(IFarmGameplayCoordinator farmGameplay)
        {
            var defaults = CozyTownCompositionRoot.CreateDefault();
            return new CozyTownServices(
                defaults.DayTransition,
                defaults.Time,
                defaults.Inventory,
                defaults.Wallet,
                defaults.ShopTrading,
                defaults.Farm,
                farmGameplay,
                defaults.Livestock,
                defaults.LivestockGameplay,
                defaults.Fishing,
                defaults.FishingGameplay,
                defaults.Cooking,
                defaults.CookingGameplay,
                defaults.NpcDialogue,
                defaults.NpcDialogueGameplay,
                defaults.SaveStorage,
                defaults.GameSave,
                defaults.EconomyState,
                defaults.WorldSeed,
                defaults.DaytimeClock,
                defaults.WorldTime,
                defaults.Sleep);
        }

        private static void Destroy(Object target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }

        private sealed class TestServicesFactory : ICozyTownServicesFactory
        {
            private readonly CozyTownServices _services;

            public TestServicesFactory(CozyTownServices services) => _services = services;

            public CozyTownServices Create() => _services;
        }

        private sealed class StubFarmGameplayCoordinator : IFarmGameplayCoordinator
        {
            private readonly FarmViewState _state = new FarmViewState(null, null);

            public int PlantCalls { get; private set; }

            public FarmViewState GetCurrentState() => _state;

            public OperationResult Plant(string plotId, string seedItemId)
            {
                PlantCalls++;
                return OperationResult.Success();
            }

            public OperationResult Water(string plotId) => OperationResult.Success();

            public OperationResult Harvest(string plotId) => OperationResult.Success();
        }
    }
}
