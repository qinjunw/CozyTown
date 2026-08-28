using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using UnityEngine;

namespace CozyTown.Unity.Shop
{
    public sealed class CozyTownShopDebugPresenter : MonoBehaviour
    {
        private const int TradeQuantity = 1;

        [SerializeField] private TownInteractionPoint2D shopPoint;
        [SerializeField] private MonoBehaviour viewBehaviour;

        private IShopTradingCoordinator _coordinator;
        private ICozyTownShopDebugView _view;
        private GameObject _actor;
        private PlayerMovement2D _movement;
        private PlayerInteractor2D _interactor;
        private Rigidbody2D _body;
        private bool _movementWasEnabled;
        private bool _interactorWasEnabled;
        private bool _isSubscribed;

        public bool IsOpen => _actor != null;

        public void Bind(IShopTradingCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            TrySubscribe();
        }

        public void Configure(
            TownInteractionPoint2D interactionPoint,
            ICozyTownShopDebugView view)
        {
            shopPoint = interactionPoint != null
                ? interactionPoint
                : throw new ArgumentNullException(nameof(interactionPoint));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            viewBehaviour = view as MonoBehaviour;
            TrySubscribe();
        }

        private void Start()
        {
            if (_isSubscribed)
            {
                return;
            }

            if (!TryValidateConfiguration(out var error))
            {
                ReportConfigurationError(error);
            }
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (_isSubscribed)
            {
                shopPoint.Interacted -= HandleShopInteraction;
                _view.BuyRequested -= HandleBuyRequested;
                _view.SellRequested -= HandleSellRequested;
                _view.CloseRequested -= Close;
                _isSubscribed = false;
            }

            Close();
        }

        private void TrySubscribe()
        {
            if (!isActiveAndEnabled
                || _isSubscribed
                || !TryValidateConfiguration(out _))
            {
                return;
            }

            shopPoint.Interacted += HandleShopInteraction;
            _view.BuyRequested += HandleBuyRequested;
            _view.SellRequested += HandleSellRequested;
            _view.CloseRequested += Close;
            _isSubscribed = true;
        }

        private bool TryValidateConfiguration(out string error)
        {
            if (_view == null && !TryResolveView(out error))
            {
                return false;
            }

            if (shopPoint == null)
            {
                error = $"{nameof(shopPoint)} is required.";
                return false;
            }

            if (shopPoint.Kind != TownInteractionKind.Shop)
            {
                error = $"{nameof(shopPoint)} must use {TownInteractionKind.Shop}.";
                return false;
            }

            if (_coordinator == null)
            {
                error = $"{nameof(IShopTradingCoordinator)} must be injected before Start.";
                return false;
            }

            error = null;
            return true;
        }

        private void HandleShopInteraction(InteractionContext context)
        {
            if (IsOpen || context.Actor == null)
            {
                return;
            }

            _actor = context.Actor;
            _movement = _actor.GetComponent<PlayerMovement2D>();
            _interactor = _actor.GetComponent<PlayerInteractor2D>();
            _body = _actor.GetComponent<Rigidbody2D>();
            _movementWasEnabled = _movement != null && _movement.enabled;
            _interactorWasEnabled = _interactor != null && _interactor.enabled;

            if (_movement != null)
            {
                _movement.enabled = false;
            }

            if (_interactor != null)
            {
                _interactor.enabled = false;
            }

            if (_body != null)
            {
                _body.linearVelocity = Vector2.zero;
            }

            _view.Show(_coordinator.GetCurrentState(), string.Empty);
        }

        private void HandleBuyRequested(string itemId)
        {
            if (!IsOpen)
            {
                return;
            }

            OperationResult<ShopReceipt> result = _coordinator.Buy(itemId, TradeQuantity);
            ShopViewState state = _coordinator.GetCurrentState();
            string feedback = result.IsSuccess
                ? BuildSuccessFeedback("Bought", result.Value, state)
                : $"Buy failed: {result.ErrorCode}";
            _view.Show(state, feedback);
        }

        private void HandleSellRequested(string itemId)
        {
            if (!IsOpen)
            {
                return;
            }

            OperationResult<ShopReceipt> result = _coordinator.Sell(itemId, TradeQuantity);
            ShopViewState state = _coordinator.GetCurrentState();
            string feedback = result.IsSuccess
                ? BuildSuccessFeedback("Sold", result.Value, state)
                : $"Sell failed: {result.ErrorCode}";
            _view.Show(state, feedback);
        }

        private void Close()
        {
            if (_actor != null)
            {
                if (_movement != null)
                {
                    _movement.enabled = _movementWasEnabled;
                }

                if (_interactor != null)
                {
                    _interactor.enabled = _interactorWasEnabled;
                }
            }

            _actor = null;
            _movement = null;
            _interactor = null;
            _body = null;
            _view?.Hide();
        }

        private bool TryResolveView(out string error)
        {
            if (viewBehaviour is ICozyTownShopDebugView view)
            {
                _view = view;
                error = null;
                return true;
            }

            error = $"{nameof(viewBehaviour)} must implement {nameof(ICozyTownShopDebugView)}.";
            return false;
        }

        private static string BuildSuccessFeedback(
            string verb,
            ShopReceipt receipt,
            ShopViewState state)
        {
            var displayName = receipt.ItemId;
            foreach (var item in state.Items)
            {
                if (string.Equals(item.ItemId, receipt.ItemId, StringComparison.Ordinal))
                {
                    displayName = item.DisplayName;
                    break;
                }
            }

            return $"{verb} {receipt.Quantity} x {displayName} for {receipt.TotalPrice} coins.";
        }

        private void ReportConfigurationError(string error)
        {
            Debug.LogError($"Shop debug presenter could not initialize: {error}", this);
            enabled = false;
        }
    }
}
