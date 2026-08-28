using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Bed
{
    public sealed class CozyTownBedDebugPresenter : CozyTownModalPresenterBase
    {
        private IDayTransitionCoordinator _coordinator;
        [SerializeField] private CozyTownBedDebugView _view;

        protected override TownInteractionKind ExpectedKind => TownInteractionKind.Bed;

        protected override bool HasDependencies => _coordinator != null && _view != null;

        public void Configure(TownInteractionPoint2D point, CozyTownBedDebugView target)
        {
            _view = target ?? throw new ArgumentNullException(nameof(target));
            ConfigureInteraction(point);
        }

        public void Bind(IDayTransitionCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            DependenciesChanged();
        }

        protected override void SubscribeView()
        {
            _view.SleepRequested += Sleep;
            _view.CloseRequested += CloseModal;
        }

        protected override void UnsubscribeView()
        {
            _view.SleepRequested -= Sleep;
            _view.CloseRequested -= CloseModal;
        }

        protected override void ShowInitialState() => _view.Show(string.Empty);

        protected override void HideView() => _view?.Hide();

        private void Sleep()
        {
            var result = _coordinator.SleepToNextDay();
            _view.Show(result.IsSuccess
                ? $"Slept to day {result.Value.Day}."
                : $"Sleep failed: {result.ErrorCode}");
        }
    }
}
