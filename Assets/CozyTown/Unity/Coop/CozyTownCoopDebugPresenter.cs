using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Coop
{
    public sealed class CozyTownCoopDebugPresenter : CozyTownModalPresenterBase
    {
        private ILivestockGameplayCoordinator _coordinator;
        private IWorldTimeFlow _timeFlow;
        private int _lastSettlementDay;
        private long _lastRebuildVersion;
        [SerializeField] private CozyTownCoopDebugView _view;
        [SerializeField] private CozyTownCoopWorldView _worldView;

        protected override TownInteractionKind ExpectedKind => TownInteractionKind.Coop;
        protected override bool HasDependencies => _coordinator != null && _view != null;

        public void Configure(TownInteractionPoint2D point, CozyTownCoopDebugView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            ConfigureInteraction(point);
        }

        public void ConfigureWorldView(CozyTownCoopWorldView worldView)
        {
            _worldView = worldView ?? throw new ArgumentNullException(nameof(worldView));
            if (_coordinator != null)
            {
                _worldView.Show(_coordinator.GetCurrentState());
            }
        }

        public void Bind(ILivestockGameplayCoordinator coordinator, IWorldTimeFlow timeFlow = null)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            if (_timeFlow != null)
            {
                _timeFlow.Changed -= RefreshWorldAfterSettlement;
            }
            _timeFlow = timeFlow;
            if (_timeFlow != null)
            {
                _lastSettlementDay = SettlementDay(_timeFlow.Current);
                _lastRebuildVersion = _timeFlow.Current.RebuildVersion;
                _timeFlow.Changed += RefreshWorldAfterSettlement;
            }
            _worldView?.Show(_coordinator.GetCurrentState());
            DependenciesChanged();
        }

        // World presentation follows the bound session even while the modal presenter is disabled.
        private void OnDestroy()
        {
            if (_timeFlow != null)
            {
                _timeFlow.Changed -= RefreshWorldAfterSettlement;
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
            _view.FeedRequested += Feed;
            _view.CollectRequested += Collect;
            _view.CloseRequested += CloseModal;
        }

        protected override void UnsubscribeView()
        {
            _view.FeedRequested -= Feed;
            _view.CollectRequested -= Collect;
            _view.CloseRequested -= CloseModal;
        }

        protected override void ShowInitialState() => Present(_coordinator.GetCurrentState(), string.Empty);
        protected override void HideView() => _view?.Hide();
        private void Feed(string id) => Run(() => _coordinator.Feed(id), "Feed");
        private void Collect(string id) => Run(() => _coordinator.CollectProduct(id), "Collect");

        private void Run(Func<OperationResult> command, string action)
        {
            var result = command();
            Present(
                _coordinator.GetCurrentState(),
                result.IsSuccess ? $"{action} succeeded." : CozyTownGameplayFeedback.Failure(action, result.ErrorCode, this));
        }

        private void Present(LivestockViewState state, string feedback)
        {
            _worldView?.Show(state);
            _view.Show(state, feedback);
        }
    }
}
