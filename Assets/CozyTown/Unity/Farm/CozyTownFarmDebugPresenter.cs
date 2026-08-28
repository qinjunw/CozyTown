using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Farm
{
    public sealed class CozyTownFarmDebugPresenter : CozyTownModalPresenterBase
    {
        private IFarmGameplayCoordinator _coordinator;
        [SerializeField] private CozyTownFarmDebugView _view;

        protected override TownInteractionKind ExpectedKind => TownInteractionKind.Farm;
        protected override bool HasDependencies => _coordinator != null && _view != null;

        public void Configure(TownInteractionPoint2D point, CozyTownFarmDebugView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            ConfigureInteraction(point);
        }

        public void Bind(IFarmGameplayCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            DependenciesChanged();
        }

        protected override void SubscribeView()
        {
            _view.PlantRequested += Plant;
            _view.WaterRequested += Water;
            _view.HarvestRequested += Harvest;
            _view.CloseRequested += CloseModal;
        }

        protected override void UnsubscribeView()
        {
            _view.PlantRequested -= Plant;
            _view.WaterRequested -= Water;
            _view.HarvestRequested -= Harvest;
            _view.CloseRequested -= CloseModal;
        }

        protected override void ShowInitialState() => _view.Show(_coordinator.GetCurrentState(), string.Empty);
        protected override void HideView() => _view?.Hide();
        private void Plant(string plot, string seed) => Run(() => _coordinator.Plant(plot, seed), "Plant");
        private void Water(string plot) => Run(() => _coordinator.Water(plot), "Water");
        private void Harvest(string plot) => Run(() => _coordinator.Harvest(plot), "Harvest");

        private void Run(Func<OperationResult> command, string action)
        {
            var result = command();
            _view.Show(
                _coordinator.GetCurrentState(),
                result.IsSuccess ? $"{action} succeeded." : $"{action} failed: {result.ErrorCode}");
        }
    }
}
