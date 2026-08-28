using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Coop
{
    public sealed class CozyTownCoopDebugPresenter : CozyTownModalPresenterBase
    {
        private ILivestockGameplayCoordinator _coordinator;
        [SerializeField] private CozyTownCoopDebugView _view;

        protected override TownInteractionKind ExpectedKind => TownInteractionKind.Coop;
        protected override bool HasDependencies => _coordinator != null && _view != null;

        public void Configure(TownInteractionPoint2D point, CozyTownCoopDebugView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            ConfigureInteraction(point);
        }

        public void Bind(ILivestockGameplayCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            DependenciesChanged();
        }

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

        protected override void ShowInitialState() => _view.Show(_coordinator.GetCurrentState(), string.Empty);
        protected override void HideView() => _view?.Hide();
        private void Feed(string id) => Run(() => _coordinator.Feed(id), "Feed");
        private void Collect(string id) => Run(() => _coordinator.CollectProduct(id), "Collect");

        private void Run(Func<OperationResult> command, string action)
        {
            var result = command();
            _view.Show(
                _coordinator.GetCurrentState(),
                result.IsSuccess ? $"{action} succeeded." : $"{action} failed: {result.ErrorCode}");
        }
    }
}
