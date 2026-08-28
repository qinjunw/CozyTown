using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Pond
{
    public sealed class CozyTownPondDebugPresenter : CozyTownModalPresenterBase
    {
        private IFishingGameplayCoordinator _coordinator;
        private IFishingRollSource _rollSource = new UnityRandomFishingRollSource();
        [SerializeField] private CozyTownPondDebugView _view;

        protected override TownInteractionKind ExpectedKind => TownInteractionKind.Pond;
        protected override bool HasDependencies => _coordinator != null && _rollSource != null && _view != null;

        public void Configure(TownInteractionPoint2D point, CozyTownPondDebugView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            ConfigureInteraction(point);
        }

        public void Bind(IFishingGameplayCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            DependenciesChanged();
        }

        public void SetRollSource(IFishingRollSource rollSource)
        {
            _rollSource = rollSource ?? throw new ArgumentNullException(nameof(rollSource));
            DependenciesChanged();
        }

        protected override void SubscribeView()
        {
            _view.CatchRequested += Catch;
            _view.CloseRequested += CloseModal;
        }

        protected override void UnsubscribeView()
        {
            _view.CatchRequested -= Catch;
            _view.CloseRequested -= CloseModal;
        }

        protected override void ShowInitialState() => _view.Show(_coordinator.GetCurrentState(), string.Empty);
        protected override void HideView() => _view?.Hide();

        private void Catch()
        {
            var result = _coordinator.Catch(_rollSource.NextRoll());
            _view.Show(
                _coordinator.GetCurrentState(),
                result.IsSuccess ? $"Caught {result.Value.ItemId}." : $"Catch failed: {result.ErrorCode}");
        }
    }
}
