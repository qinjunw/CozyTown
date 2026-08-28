using System;
using CozyTown.Runtime.Core;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Shop;
using UnityEngine;

namespace CozyTown.Unity.Core
{
    [DefaultExecutionOrder(-1000)]
    public sealed class CozyTownBootstrap : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Optional MonoBehaviour implementing ICozyTownServicesFactory. The default MVP configuration is used when omitted.")]
        private MonoBehaviour servicesFactoryBehaviour;
        [SerializeField] private CozyTownHudPresenter[] hudPresenters = Array.Empty<CozyTownHudPresenter>();
        [SerializeField] private CozyTownShopDebugPresenter[] shopPresenters = Array.Empty<CozyTownShopDebugPresenter>();
        [SerializeField]
        private CozyTownFarmDebugPresenter[] _farmPresenters =
            Array.Empty<CozyTownFarmDebugPresenter>();
        [SerializeField]
        private CozyTownBedDebugPresenter[] _bedPresenters =
            Array.Empty<CozyTownBedDebugPresenter>();
        [SerializeField]
        private CozyTownCoopDebugPresenter[] _coopPresenters =
            Array.Empty<CozyTownCoopDebugPresenter>();
        [SerializeField]
        private CozyTownPondDebugPresenter[] _pondPresenters =
            Array.Empty<CozyTownPondDebugPresenter>();
        [SerializeField]
        private CozyTownKitchenDebugPresenter[] _kitchenPresenters =
            Array.Empty<CozyTownKitchenDebugPresenter>();

        private ICozyTownServicesFactory _factoryOverride;
        private CozyTownServices _services;

        public bool IsInitialized => _services != null;

        public void SetFactory(ICozyTownServicesFactory factory)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException("Services are already initialized.");
            }

            _factoryOverride = factory ?? throw new ArgumentNullException(nameof(factory));
            servicesFactoryBehaviour = factory as MonoBehaviour;
        }

        public void Initialize(CozyTownServices services)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException("Services are already initialized.");
            }

            _services = services ?? throw new ArgumentNullException(nameof(services));
            BindHudPresenters();
            BindShopPresenters();
            BindGameplayPresenters();
        }

        public void RegisterHudPresenter(CozyTownHudPresenter presenter)
        {
            if (presenter == null)
            {
                throw new ArgumentNullException(nameof(presenter));
            }

            if (Array.IndexOf(hudPresenters, presenter) < 0)
            {
                Array.Resize(ref hudPresenters, hudPresenters.Length + 1);
                hudPresenters[hudPresenters.Length - 1] = presenter;
            }

            if (IsInitialized)
            {
                BindHudPresenter(presenter);
            }
        }

        public void RegisterShopPresenter(CozyTownShopDebugPresenter presenter)
        {
            if (presenter == null)
            {
                throw new ArgumentNullException(nameof(presenter));
            }

            if (Array.IndexOf(shopPresenters, presenter) < 0)
            {
                Array.Resize(ref shopPresenters, shopPresenters.Length + 1);
                shopPresenters[shopPresenters.Length - 1] = presenter;
            }

            if (IsInitialized)
            {
                BindShopPresenter(presenter);
            }
        }

        public void RegisterFarmPresenter(CozyTownFarmDebugPresenter presenter)
        {
            Register(ref _farmPresenters, presenter);
            if (IsInitialized)
            {
                presenter.Bind(_services.FarmGameplay);
            }
        }

        public void RegisterBedPresenter(CozyTownBedDebugPresenter presenter)
        {
            Register(ref _bedPresenters, presenter);
            if (IsInitialized)
            {
                presenter.Bind(_services.DayTransition);
            }
        }

        public void RegisterCoopPresenter(CozyTownCoopDebugPresenter presenter)
        {
            Register(ref _coopPresenters, presenter);
            if (IsInitialized)
            {
                presenter.Bind(_services.LivestockGameplay);
            }
        }

        public void RegisterPondPresenter(CozyTownPondDebugPresenter presenter)
        {
            Register(ref _pondPresenters, presenter);
            if (IsInitialized)
            {
                presenter.Bind(_services.FishingGameplay);
            }
        }

        public void RegisterKitchenPresenter(CozyTownKitchenDebugPresenter presenter)
        {
            Register(ref _kitchenPresenters, presenter);
            if (IsInitialized)
            {
                presenter.Bind(_services.CookingGameplay);
            }
        }

        private void Awake()
        {
            if (IsInitialized)
            {
                return;
            }

            try
            {
                var factory = ResolveFactory();
                Initialize(factory != null
                    ? factory.Create()
                    : CozyTownCompositionRoot.CreateDefault());
            }
            catch (Exception exception)
            {
                Debug.LogError($"CozyTown bootstrap could not initialize: {exception.Message}", this);
                enabled = false;
            }
        }

        private ICozyTownServicesFactory ResolveFactory()
        {
            if (_factoryOverride != null)
            {
                return _factoryOverride;
            }

            if (servicesFactoryBehaviour == null)
            {
                return null;
            }

            if (servicesFactoryBehaviour is ICozyTownServicesFactory factory)
            {
                return factory;
            }

            throw new InvalidOperationException(
                $"{servicesFactoryBehaviour.name} must implement {nameof(ICozyTownServicesFactory)}.");
        }

        private void BindHudPresenters()
        {
            foreach (var presenter in hudPresenters)
            {
                if (presenter != null)
                {
                    BindHudPresenter(presenter);
                }
            }
        }

        private void BindHudPresenter(CozyTownHudPresenter presenter)
        {
            presenter.Bind(_services.Time, _services.Wallet);
        }

        private void BindShopPresenters()
        {
            foreach (var presenter in shopPresenters)
            {
                if (presenter != null)
                {
                    BindShopPresenter(presenter);
                }
            }
        }

        private void BindShopPresenter(CozyTownShopDebugPresenter presenter)
        {
            presenter.Bind(_services.ShopTrading);
        }

        private void BindGameplayPresenters()
        {
            foreach (var presenter in _farmPresenters)
            {
                if (presenter != null)
                {
                    presenter.Bind(_services.FarmGameplay);
                }
            }
            foreach (var presenter in _bedPresenters)
            {
                if (presenter != null)
                {
                    presenter.Bind(_services.DayTransition);
                }
            }
            foreach (var presenter in _coopPresenters)
            {
                if (presenter != null)
                {
                    presenter.Bind(_services.LivestockGameplay);
                }
            }
            foreach (var presenter in _pondPresenters)
            {
                if (presenter != null)
                {
                    presenter.Bind(_services.FishingGameplay);
                }
            }
            foreach (var presenter in _kitchenPresenters)
            {
                if (presenter != null)
                {
                    presenter.Bind(_services.CookingGameplay);
                }
            }
        }

        private static void Register<T>(ref T[] presenters, T presenter) where T : MonoBehaviour
        {
            if (presenter == null)
            {
                throw new ArgumentNullException(nameof(presenter));
            }
            if (Array.IndexOf(presenters, presenter) >= 0)
            {
                return;
            }
            Array.Resize(ref presenters, presenters.Length + 1);
            presenters[presenters.Length - 1] = presenter;
        }
    }
}
