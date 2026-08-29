using System;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using UnityEngine;

namespace CozyTown.Unity.Save
{
    public sealed class CozyTownSaveDebugPresenter : MonoBehaviour
    {
        [SerializeField] private CozyTownSaveDebugView view;

        private IGameSaveCoordinator _coordinator;
        private bool _isSubscribed;

        public void Configure(CozyTownSaveDebugView saveView)
        {
            view = saveView != null
                ? saveView
                : throw new ArgumentNullException(nameof(saveView));
            TrySubscribe();
        }

        public void Bind(IGameSaveCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            TrySubscribe();
        }

        private void Start()
        {
            if (_isSubscribed)
            {
                return;
            }

            if (!HasDependencies())
            {
                Debug.LogError(
                    "Save debug presenter requires a view and IGameSaveCoordinator.",
                    this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            view?.Hide();
        }

        private void TrySubscribe()
        {
            if (!isActiveAndEnabled || _isSubscribed || !HasDependencies())
            {
                return;
            }

            view.SaveRequested += Save;
            view.LoadRequested += Load;
            _isSubscribed = true;
            view.Show(_coordinator.HasSave, string.Empty);
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed)
            {
                return;
            }

            view.SaveRequested -= Save;
            view.LoadRequested -= Load;
            _isSubscribed = false;
        }

        private bool HasDependencies()
        {
            return view != null && _coordinator != null;
        }

        private void Save()
        {
            OperationResult result = _coordinator.Save();
            view.Show(
                _coordinator.HasSave,
                result.IsSuccess ? "Game saved." : $"Save failed: {result.ErrorCode}");
        }

        private void Load()
        {
            if (!_coordinator.HasSave)
            {
                view.Show(false, "Load unavailable: save.slot_missing");
                return;
            }

            OperationResult result = _coordinator.Load();
            view.Show(
                _coordinator.HasSave,
                result.IsSuccess ? "Game loaded." : $"Load failed: {result.ErrorCode}");
        }
    }
}
