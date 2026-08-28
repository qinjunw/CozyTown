using System.Collections;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Unity.Core;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using CozyTown.Unity.Shop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class ShopDebugPresenterPlayModeTests
    {
        private GameObject _actor;
        private GameObject _pointObject;
        private GameObject _shopUi;
        private GameObject _bootstrapObject;

        [UnityTest]
        public IEnumerator InteractionOpensPanel_BuySucceeds_SellFails_AndCloseRestoresActor()
        {
            var input = CreateActor();
            var point = CreateShopPoint();
            var initialState = State(balance: 300, potatoSeedOwned: 0);
            var coordinator = new StubShopTradingCoordinator(initialState)
            {
                BuyResult = OperationResult<ShopReceipt>.Success(
                    new ShopReceipt("seed.potato", 1, 20, true)),
                StateAfterBuy = State(balance: 280, potatoSeedOwned: 1),
                SellResult = OperationResult<ShopReceipt>.Failure(
                    "inventory.insufficient_quantity")
            };
            var presenter = CreatePresenter(point, coordinator, out var view);
            var body = _actor.GetComponent<Rigidbody2D>();
            var movement = _actor.GetComponent<PlayerMovement2D>();
            var interactor = _actor.GetComponent<PlayerInteractor2D>();
            body.linearVelocity = new Vector2(2f, -1f);

            point.Interact(new InteractionContext(_actor));
            yield return null;

            Assert.That(presenter.IsOpen, Is.True);
            Assert.That(view.IsVisible, Is.True);
            Assert.That(movement.enabled, Is.False);
            Assert.That(interactor.enabled, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));

            view.RequestBuy("seed.potato");
            Assert.That(coordinator.BuyCalls, Is.EqualTo(1));
            Assert.That(coordinator.LastQuantity, Is.EqualTo(1));
            Assert.That(view.State.Balance, Is.EqualTo(280));
            Assert.That(view.State.Items[0].OwnedQuantity, Is.EqualTo(1));
            Assert.That(view.Feedback, Is.EqualTo("Bought 1 x Potato Seed for 20 coins."));

            view.RequestSell("crop.potato");
            Assert.That(coordinator.SellCalls, Is.EqualTo(1));
            Assert.That(coordinator.LastQuantity, Is.EqualTo(1));
            Assert.That(view.Feedback, Is.EqualTo(
                "Sell failed: inventory.insufficient_quantity"));

            view.RequestClose();
            Assert.That(presenter.IsOpen, Is.False);
            Assert.That(view.IsVisible, Is.False);
            Assert.That(movement.enabled, Is.True);
            Assert.That(interactor.enabled, Is.True);
            Assert.That(input.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator DisableWhileOpen_RestoresActorAndStopsResponding()
        {
            CreateActor();
            var point = CreateShopPoint();
            var coordinator = new StubShopTradingCoordinator(State(300, 0));
            var presenter = CreatePresenter(point, coordinator, out var view);
            var movement = _actor.GetComponent<PlayerMovement2D>();
            var interactor = _actor.GetComponent<PlayerInteractor2D>();

            point.Interact(new InteractionContext(_actor));
            yield return null;
            Assert.That(view.IsVisible, Is.True);

            presenter.enabled = false;
            Assert.That(view.IsVisible, Is.False);
            Assert.That(movement.enabled, Is.True);
            Assert.That(interactor.enabled, Is.True);

            point.Interact(new InteractionContext(_actor));
            Assert.That(view.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator DisablingInputGate_ClosesShopAndRestoresActor()
        {
            CreateActor();
            var point = CreateShopPoint();
            var presenter = CreatePresenter(
                point,
                new StubShopTradingCoordinator(State(300, 0)),
                out var view);
            var gate = _actor.GetComponent<PlayerModalInputGate2D>();
            var movement = _actor.GetComponent<PlayerMovement2D>();
            var interactor = _actor.GetComponent<PlayerInteractor2D>();

            point.Interact(new InteractionContext(_actor));
            yield return null;
            Assert.That(presenter.IsOpen, Is.True);

            gate.enabled = false;

            Assert.That(presenter.IsOpen, Is.False);
            Assert.That(view.IsVisible, Is.False);
            Assert.That(movement.enabled, Is.True);
            Assert.That(interactor.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator Close_PreservesOriginallyDisabledActorComponents()
        {
            CreateActor();
            var movement = _actor.GetComponent<PlayerMovement2D>();
            var interactor = _actor.GetComponent<PlayerInteractor2D>();
            movement.enabled = false;
            interactor.enabled = false;
            var point = CreateShopPoint();
            var presenter = CreatePresenter(
                point,
                new StubShopTradingCoordinator(State(300, 0)),
                out var view);

            point.Interact(new InteractionContext(_actor));
            yield return null;
            Assert.That(presenter.IsOpen, Is.True);

            view.RequestClose();
            Assert.That(movement.enabled, Is.False);
            Assert.That(interactor.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator DisableThenEnable_SubscriptionRemainsUnique()
        {
            CreateActor();
            var point = CreateShopPoint();
            var coordinator = new StubShopTradingCoordinator(State(300, 0))
            {
                BuyResult = OperationResult<ShopReceipt>.Failure("test.failure")
            };
            var presenter = CreatePresenter(point, coordinator, out var view);

            presenter.enabled = false;
            presenter.enabled = true;
            point.Interact(new InteractionContext(_actor));
            yield return null;
            view.RequestBuy("seed.potato");

            Assert.That(coordinator.BuyCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PlayerInteractionEdge_OpensShopWithoutTrading()
        {
            var input = CreateActor();
            var point = CreateShopPoint();
            _pointObject.AddComponent<CircleCollider2D>();
            _pointObject.transform.position = new Vector2(0.25f, 0f);
            var coordinator = new StubShopTradingCoordinator(State(300, 0));
            var presenter = CreatePresenter(point, coordinator, out var view);
            Physics2D.SyncTransforms();
            yield return null;

            input.PressInteract();
            yield return null;

            Assert.That(presenter.IsOpen, Is.True);
            Assert.That(view.IsVisible, Is.True);
            Assert.That(coordinator.BuyCalls, Is.Zero);
            Assert.That(coordinator.SellCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ActivePresenter_CanConfigureAndRegisterAfterBootstrapInitialization()
        {
            CreateActor();
            var point = CreateShopPoint();
            _shopUi = new GameObject("Shop UI");
            var view = _shopUi.AddComponent<CozyTownShopDebugView>();
            var presenter = _shopUi.AddComponent<CozyTownShopDebugPresenter>();
            presenter.Configure(point, view);

            _bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = _bootstrapObject.AddComponent<CozyTownBootstrap>();
            Assert.That(bootstrap.IsInitialized, Is.True);
            bootstrap.RegisterShopPresenter(presenter);

            point.Interact(new InteractionContext(_actor));
            yield return null;

            Assert.That(presenter.enabled, Is.True);
            Assert.That(presenter.IsOpen, Is.True);
            Assert.That(view.IsVisible, Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            if (_bootstrapObject != null)
            {
                Object.DestroyImmediate(_bootstrapObject);
            }

            if (_shopUi != null)
            {
                Object.DestroyImmediate(_shopUi);
            }

            if (_pointObject != null)
            {
                Object.DestroyImmediate(_pointObject);
            }

            if (_actor != null)
            {
                Object.DestroyImmediate(_actor);
            }
        }

        private PlayModePlayerInputSource CreateActor()
        {
            _actor = new GameObject("Actor");
            _actor.SetActive(false);
            var body = _actor.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            var input = _actor.AddComponent<PlayModePlayerInputSource>();
            var movement = _actor.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);
            var probe = _actor.AddComponent<InteractionProbe2D>();
            var interactor = _actor.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);
            _actor.AddComponent<PlayerModalInputGate2D>();
            _actor.SetActive(true);
            return input;
        }

        private TownInteractionPoint2D CreateShopPoint()
        {
            _pointObject = new GameObject("Shop point");
            var point = _pointObject.AddComponent<TownInteractionPoint2D>();
            point.Configure(TownInteractionKind.Shop, "Open shop");
            return point;
        }

        private CozyTownShopDebugPresenter CreatePresenter(
            TownInteractionPoint2D point,
            IShopTradingCoordinator coordinator,
            out CozyTownShopDebugView view)
        {
            _shopUi = new GameObject("Shop UI");
            _shopUi.SetActive(false);
            view = _shopUi.AddComponent<CozyTownShopDebugView>();
            var presenter = _shopUi.AddComponent<CozyTownShopDebugPresenter>();
            presenter.Configure(point, view);
            presenter.Bind(coordinator);
            _shopUi.SetActive(true);
            return presenter;
        }

        private static ShopViewState State(int balance, int potatoSeedOwned)
        {
            return new ShopViewState(
                balance,
                new[]
                {
                    new ShopLineItem(
                        "seed.potato",
                        "Potato Seed",
                        20,
                        0,
                        potatoSeedOwned),
                    new ShopLineItem(
                        "crop.potato",
                        "Potato",
                        0,
                        20,
                        0)
                });
        }

        private sealed class StubShopTradingCoordinator : IShopTradingCoordinator
        {
            private ShopViewState _state;

            public StubShopTradingCoordinator(ShopViewState state)
            {
                _state = state;
            }

            public OperationResult<ShopReceipt> BuyResult { get; set; }

            public OperationResult<ShopReceipt> SellResult { get; set; }

            public ShopViewState StateAfterBuy { get; set; }

            public ShopViewState StateAfterSell { get; set; }

            public int BuyCalls { get; private set; }

            public int SellCalls { get; private set; }

            public int LastQuantity { get; private set; }

            public ShopViewState GetCurrentState()
            {
                return _state;
            }

            public OperationResult<ShopReceipt> Buy(string itemId, int quantity)
            {
                BuyCalls++;
                LastQuantity = quantity;
                if (BuyResult.IsSuccess && StateAfterBuy != null)
                {
                    _state = StateAfterBuy;
                }

                return BuyResult;
            }

            public OperationResult<ShopReceipt> Sell(string itemId, int quantity)
            {
                SellCalls++;
                LastQuantity = quantity;
                if (SellResult.IsSuccess && StateAfterSell != null)
                {
                    _state = StateAfterSell;
                }

                return SellResult;
            }
        }
    }
}
