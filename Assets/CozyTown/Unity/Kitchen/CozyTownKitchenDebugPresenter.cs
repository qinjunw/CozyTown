using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Kitchen
{
    public sealed class CozyTownKitchenDebugPresenter : CozyTownModalPresenterBase
    {
        private ICookingGameplayCoordinator _coordinator;
        [SerializeField] private CozyTownKitchenDebugView _view;

        protected override TownInteractionKind ExpectedKind => TownInteractionKind.Kitchen;
        protected override bool HasDependencies => _coordinator != null && _view != null;

        public void Configure(TownInteractionPoint2D point, CozyTownKitchenDebugView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            ConfigureInteraction(point);
        }

        public void Bind(ICookingGameplayCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            DependenciesChanged();
        }

        protected override void SubscribeView()
        {
            _view.CookRequested += Cook;
            _view.CloseRequested += CloseModal;
        }

        protected override void UnsubscribeView()
        {
            _view.CookRequested -= Cook;
            _view.CloseRequested -= CloseModal;
        }

        protected override void ShowInitialState() => _view.Show(_coordinator.GetCurrentState(), string.Empty);
        protected override void HideView() => _view?.Hide();

        private void Cook(string recipeId)
        {
            var result = _coordinator.Cook(recipeId);
            _view.Show(
                _coordinator.GetCurrentState(),
                result.IsSuccess
                    ? $"Cooked {result.Value.OutputItemId} x{result.Value.OutputQuantity}."
                    : $"Cook failed: {result.ErrorCode}");
        }
    }
}
