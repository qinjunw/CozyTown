using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Bed
{
    public sealed class CozyTownBedDebugPresenter : CozyTownModalPresenterBase
    {
        private ISleepCoordinator _coordinator;
        [SerializeField] private CozyTownBedDebugView _view;

        protected override TownInteractionKind ExpectedKind => TownInteractionKind.Bed;

        protected override bool HasDependencies => _coordinator != null && _view != null;

        protected override bool CanOpenModal => base.CanOpenModal && _view.isActiveAndEnabled;

        public void Configure(TownInteractionPoint2D point, CozyTownBedDebugView target)
        {
            _view = target ?? throw new ArgumentNullException(nameof(target));
            ConfigureInteraction(point);
        }

        public void Bind(ISleepCoordinator coordinator)
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
            var result = _coordinator.SleepForMinutes(_view.SelectedSleepHours * 60);
            _view.Show(result.IsSuccess
                ? $"Slept to Day {result.Value.Day} {result.Value.MinuteOfDay / 60:00}:{result.Value.MinuteOfDay % 60:00}."
                : $"Sleep failed: {result.ErrorCode}");
        }
    }
}
