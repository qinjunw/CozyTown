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

        private ICharacterShopTradingCoordinator _coordinator;
        private ICozyTownShopDebugView _view;
        private ShopTradingViewState _currentState;
        private string _shopId;
        private string _characterId;
        private GameObject _actor;
        private PlayerModalInputGate2D _inputGate;
        private bool _isSubscribed;

        public bool IsOpen => _actor != null;

        public void Bind(
            ICharacterShopTradingCoordinator coordinator,
            string shopId,
            string characterId)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _shopId = !string.IsNullOrWhiteSpace(shopId)
                ? shopId
                : throw new ArgumentException("Shop ID is required.", nameof(shopId));
            _characterId = !string.IsNullOrWhiteSpace(characterId)
                ? characterId
                : throw new ArgumentException(
                    "Character ID is required.",
                    nameof(characterId));
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
                error = $"{nameof(ICharacterShopTradingCoordinator)} must be injected before Start.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_shopId)
                || string.IsNullOrWhiteSpace(_characterId))
            {
                error = "Stable shop and character IDs must be injected before Start.";
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
            _inputGate = _actor.GetComponent<PlayerModalInputGate2D>();
            if (_inputGate == null || !_inputGate.TryAcquire(this))
            {
                _actor = null;
                _inputGate = null;
                return;
            }

            _inputGate.AcquisitionRevoked += HandleInputGateRevoked;
            RefreshView(string.Empty);
        }

        private void HandleBuyRequested(string itemId)
        {
            if (!IsOpen)
            {
                return;
            }

            string displayName = ResolveDisplayName(itemId);
            OperationResult<ShopReceipt> result = _coordinator.Buy(
                _shopId,
                _characterId,
                itemId,
                TradeQuantity);
            string feedback = result.IsSuccess
                ? BuildSuccessFeedback("Bought", result.Value, displayName)
                : $"Buy failed: {result.ErrorCode}";
            RefreshView(feedback);
        }

        private void HandleSellRequested(string itemId)
        {
            if (!IsOpen)
            {
                return;
            }

            string displayName = ResolveDisplayName(itemId);
            OperationResult<ShopReceipt> result = _coordinator.Sell(
                _shopId,
                _characterId,
                itemId,
                TradeQuantity);
            string feedback = result.IsSuccess
                ? BuildSuccessFeedback("Sold", result.Value, displayName)
                : $"Sell failed: {result.ErrorCode}";
            RefreshView(feedback);
        }

        private void Close()
        {
            if (_actor != null)
            {
                _inputGate.AcquisitionRevoked -= HandleInputGateRevoked;
                _inputGate.Release(this);
            }

            _actor = null;
            _inputGate = null;
            _currentState = null;
            _view?.Hide();
        }

        private void HandleInputGateRevoked()
        {
            if (_inputGate != null)
            {
                _inputGate.AcquisitionRevoked -= HandleInputGateRevoked;
            }

            _actor = null;
            _inputGate = null;
            _currentState = null;
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
            string displayName)
        {
            return $"{verb} {receipt.Quantity} x {displayName} for {receipt.TotalPrice} coins.";
        }

        private void RefreshView(string feedback)
        {
            OperationResult<ShopTradingViewState> result = _coordinator.GetCurrentState(
                _shopId,
                _characterId);
            if (result.IsSuccess)
            {
                _currentState = result.Value;
                _view.Show(_currentState, feedback);
                return;
            }

            if (_currentState != null)
            {
                _view.Show(
                    _currentState,
                    $"State refresh failed: {result.ErrorCode}");
                return;
            }

            Debug.LogError(
                $"Shop state could not be loaded: {result.ErrorCode}",
                this);
            Close();
        }

        private string ResolveDisplayName(string itemId)
        {
            if (_currentState != null)
            {
                foreach (ShopTradingLineItem item in _currentState.PurchaseItems)
                {
                    if (string.Equals(item.ItemId, itemId, StringComparison.Ordinal))
                    {
                        return item.DisplayName;
                    }
                }

                foreach (ShopTradingLineItem item in _currentState.SaleItems)
                {
                    if (string.Equals(item.ItemId, itemId, StringComparison.Ordinal))
                    {
                        return item.DisplayName;
                    }
                }
            }

            return itemId;
        }

        private void ReportConfigurationError(string error)
        {
            Debug.LogError($"Shop debug presenter could not initialize: {error}", this);
            enabled = false;
        }
    }
}
