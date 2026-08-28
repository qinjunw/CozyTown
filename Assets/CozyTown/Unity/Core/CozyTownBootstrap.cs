using System;
using CozyTown.Runtime.Core;
using CozyTown.Unity.Hud;
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
    }
}
