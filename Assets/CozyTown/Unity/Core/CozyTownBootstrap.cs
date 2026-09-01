using System;
using System.IO;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Content;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Inventory;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Save;
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
        [SerializeField]
        [Tooltip("Validated economy and production content used by the default service graph.")]
        private CozyTownMvpContentAsset _contentAsset;
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
        [SerializeField]
        private CozyTownNpcDebugPresenter[] _npcPresenters =
            Array.Empty<CozyTownNpcDebugPresenter>();
        [SerializeField]
        private CozyTownSaveDebugPresenter[] _savePresenters =
            Array.Empty<CozyTownSaveDebugPresenter>();
        [SerializeField]
        private CozyTownInventoryPresenter[] _inventoryPresenters =
            Array.Empty<CozyTownInventoryPresenter>();
        [SerializeField]
        [Tooltip("Optional HTTP(S) proxy endpoint. Leave empty to use fixed NPC dialogue.")]
        private string _aiProxyEndpoint = string.Empty;
        [SerializeField]
        [Min(0.1f)]
        private float _aiProxyTimeoutSeconds = 8f;

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

        public void ConfigureContentAsset(CozyTownMvpContentAsset contentAsset)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException("Services are already initialized.");
            }

            _contentAsset = contentAsset
                ?? throw new ArgumentNullException(nameof(contentAsset));
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

        public void RegisterNpcPresenter(CozyTownNpcDebugPresenter presenter)
        {
            Register(ref _npcPresenters, presenter);
            if (IsInitialized)
            {
                presenter.Bind(_services.NpcDialogueGameplay);
            }
        }

        public void ConfigureNpcPresenters(params CozyTownNpcDebugPresenter[] presenters)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "NPC presenters cannot be replaced after services are initialized.");
            }
            if (presenters == null)
            {
                throw new ArgumentNullException(nameof(presenters));
            }

            var configured = new CozyTownNpcDebugPresenter[presenters.Length];
            for (var index = 0; index < presenters.Length; index++)
            {
                var presenter = presenters[index]
                    ?? throw new ArgumentException(
                        "NPC presenters must not contain null entries.",
                        nameof(presenters));
                if (Array.IndexOf(configured, presenter, 0, index) >= 0)
                {
                    throw new ArgumentException(
                        "NPC presenters must not contain duplicate entries.",
                        nameof(presenters));
                }

                configured[index] = presenter;
            }

            _npcPresenters = configured;
        }

        public void RegisterSavePresenter(CozyTownSaveDebugPresenter presenter)
        {
            Register(ref _savePresenters, presenter);
            if (IsInitialized)
            {
                presenter.Bind(_services.GameSave);
            }
        }

        public void RegisterInventoryPresenter(CozyTownInventoryPresenter presenter)
        {
            Register(ref _inventoryPresenters, presenter);
            if (IsInitialized)
            {
                presenter.Bind(_services.InventoryProjection);
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
                    : CreateDefaultUnityServices());
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

        private CozyTownServices CreateDefaultUnityServices()
        {
            if (_contentAsset == null)
            {
                throw new InvalidOperationException(
                    "The default content asset has not been assigned.");
            }

            OperationResult<CozyTownConfiguration> content = _contentAsset.Load();
            if (!content.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"The default content asset is invalid: {content.ErrorCode}");
            }

            CozyTownConfiguration configuration = content.Value;
            INpcDialogueGenerator fallback = new ConfiguredFallbackDialogueGenerator(
                configuration.Npcs,
                configuration.FallbackDialogue);
            INpcDialogueGenerator dialogue = fallback;
            AiProxyRuntimeConfiguration aiProxy =
                AiProxyRuntimeConfiguration.FromEnvironment(
                    _aiProxyEndpoint,
                    _aiProxyTimeoutSeconds);
            if (!string.IsNullOrWhiteSpace(aiProxy.Endpoint))
            {
                dialogue = new AiNpcDialogueGenerator(
                    new ProxyNpcDialogueClient(aiProxy.Endpoint),
                    fallback,
                    TimeSpan.FromSeconds(aiProxy.TimeoutSeconds));
            }

            ISaveStorage saveStorage;
            if (Application.isBatchMode)
            {
                saveStorage = new InMemorySaveStorage();
            }
            else
            {
                string savePath = Path.Combine(
                    Application.persistentDataPath,
                    "CozyTown",
                    "main.json");
                saveStorage = new JsonFileSaveStorage(savePath);
            }

            return CozyTownCompositionRoot.Create(
                configuration,
                dialogue,
                saveStorage);
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
            presenter.Bind(
                _services.ShopTrading,
                DefaultMvpIds.Shops.TownGeneral,
                DefaultMvpIds.Characters.Player);
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
            foreach (var presenter in _npcPresenters)
            {
                if (presenter != null)
                {
                    presenter.Bind(_services.NpcDialogueGameplay);
                }
            }
            foreach (var presenter in _savePresenters)
            {
                if (presenter != null)
                {
                    presenter.Bind(_services.GameSave);
                }
            }
            foreach (var presenter in _inventoryPresenters)
            {
                if (presenter != null)
                {
                    presenter.Bind(_services.InventoryProjection);
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
