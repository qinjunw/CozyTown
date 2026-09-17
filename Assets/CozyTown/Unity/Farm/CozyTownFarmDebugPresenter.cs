using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Farm
{
    public sealed class CozyTownFarmDebugPresenter : CozyTownModalPresenterBase
    {
        private IFarmGameplayCoordinator _coordinator;
        private IWorldTimeFlow _timeFlow;
        private int _lastSettlementDay;
        private long _lastRebuildVersion;
        [SerializeField] private CozyTownFarmDebugView _view;
        [SerializeField] private CozyTownFarmWorldView _worldView;

        protected override TownInteractionKind ExpectedKind => TownInteractionKind.Farm;
        protected override bool HasDependencies => _coordinator != null && _view != null;

        public void Configure(TownInteractionPoint2D point, CozyTownFarmDebugView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            ConfigureInteraction(point);
        }

        public void ConfigureWorldView(CozyTownFarmWorldView worldView)
        {
            _worldView = worldView ?? throw new ArgumentNullException(nameof(worldView));
            if (_coordinator != null)
            {
                _worldView.Show(_coordinator.GetCurrentState());
            }
        }

        public void Bind(IFarmGameplayCoordinator coordinator, IWorldTimeFlow timeFlow = null)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            if (_timeFlow != null)
            {
                _timeFlow.PresentationChanged -= RefreshWorldAfterSettlement;
            }
            _timeFlow = timeFlow;
            if (_timeFlow != null)
            {
                _lastSettlementDay = SettlementDay(_timeFlow.Current);
                _lastRebuildVersion = _timeFlow.Current.RebuildVersion;
                _timeFlow.PresentationChanged += RefreshWorldAfterSettlement;
            }
            _worldView?.Show(_coordinator.GetCurrentState());
            DependenciesChanged();
        }

        // World presentation follows the bound session even while the modal presenter is disabled.
        private void OnDestroy()
        {
            if (_timeFlow != null)
            {
                _timeFlow.PresentationChanged -= RefreshWorldAfterSettlement;
            }
        }

        private void RefreshWorldAfterSettlement(WorldTimeProgress progress)
        {
            int settlementDay = SettlementDay(progress);
            if (settlementDay == _lastSettlementDay && progress.RebuildVersion == _lastRebuildVersion)
            {
                return;
            }
            _lastSettlementDay = settlementDay;
            _lastRebuildVersion = progress.RebuildVersion;
            _worldView?.Show(_coordinator.GetCurrentState());
        }

        private static int SettlementDay(WorldTimeProgress progress) =>
            progress.Clock.Day - (progress.Clock.MinuteOfDay < 5 * 60 ? 1 : 0);

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

        protected override void ShowInitialState() => Present(_coordinator.GetCurrentState(), string.Empty);
        protected override void HideView() => _view?.Hide();
        private void Plant(string plot, string seed) => Run(() => _coordinator.Plant(plot, seed), "Plant");
        private void Water(string plot) => Run(() => _coordinator.Water(plot), "Water");
        private void Harvest(string plot) => Run(() => _coordinator.Harvest(plot), "Harvest");

        private void Run(Func<OperationResult> command, string action)
        {
            var result = command();
            Present(
                _coordinator.GetCurrentState(),
                result.IsSuccess ? $"{action} succeeded." : CozyTownGameplayFeedback.Failure(action, result.ErrorCode, this));
        }

        private void Present(FarmViewState state, string feedback)
        {
            _worldView?.Show(state);
            _view.Show(state, feedback);
        }
    }
}
