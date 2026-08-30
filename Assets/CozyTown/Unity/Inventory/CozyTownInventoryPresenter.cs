using System;
using CozyTown.Runtime.Inventory;
using CozyTown.Unity.Input;
using CozyTown.Unity.Player;
using UnityEngine;

namespace CozyTown.Unity.Inventory
{
    public sealed class CozyTownInventoryPresenter : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour inputSourceBehaviour;
        [SerializeField] private PlayerModalInputGate2D inputGate;
        [SerializeField] private CozyTownBackpackView backpackView;
        [SerializeField] private CozyTownHotbarView hotbarView;

        private IInventoryUiInputSource _inputSource;
        private IInventoryProjection _projection;
        private bool _subscribed;
        private bool _ownsGate;

        public bool IsBackpackOpen => _ownsGate;

        public int SelectedHotbarIndex { get; private set; }

        public void Bind(IInventoryProjection projection)
        {
            _projection = projection ?? throw new ArgumentNullException(nameof(projection));
            RefreshViews();
        }

        public void Configure(
            IInventoryUiInputSource inputSource,
            PlayerModalInputGate2D gate,
            CozyTownBackpackView backpack,
            CozyTownHotbarView hotbar)
        {
            Unsubscribe();
            _inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
            inputSourceBehaviour = inputSource as MonoBehaviour;
            inputGate = gate != null ? gate : throw new ArgumentNullException(nameof(gate));
            backpackView = backpack != null
                ? backpack
                : throw new ArgumentNullException(nameof(backpack));
            hotbarView = hotbar != null ? hotbar : throw new ArgumentNullException(nameof(hotbar));
            TrySubscribe();
            RefreshViews();
        }

        public void ProcessInput()
        {
            if (!HasDependencies)
            {
                return;
            }

            int selectedIndex = _inputSource.HotbarSelectionPressedThisFrame;
            if (selectedIndex >= 0 && selectedIndex < CozyTownHotbarView.SlotCount)
            {
                SelectedHotbarIndex = selectedIndex;
            }

            RefreshViews();
            if (!_inputSource.BackpackTogglePressedThisFrame)
            {
                return;
            }

            if (IsBackpackOpen)
            {
                CloseBackpack();
            }
            else
            {
                OpenBackpack();
            }
        }

        public void RefreshViews()
        {
            if (_projection == null || hotbarView == null)
            {
                return;
            }

            InventoryProjection current = _projection.CaptureProjection();
            hotbarView.Render(current, SelectedHotbarIndex);
            if (IsBackpackOpen && backpackView != null)
            {
                backpackView.Show(current);
            }
        }

        private bool HasDependencies => _inputSource != null
            && _projection != null
            && inputGate != null
            && backpackView != null
            && hotbarView != null;

        private void Awake()
        {
            if (_inputSource == null && inputSourceBehaviour is IInventoryUiInputSource inputSource)
            {
                _inputSource = inputSource;
            }
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            CloseBackpack();
        }

        private void Update()
        {
            ProcessInput();
        }

        private void LateUpdate()
        {
            RefreshViews();
        }

        private void TrySubscribe()
        {
            if (!isActiveAndEnabled || _subscribed || backpackView == null)
            {
                return;
            }

            backpackView.CloseRequested += CloseBackpack;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            backpackView.CloseRequested -= CloseBackpack;
            _subscribed = false;
        }

        private void OpenBackpack()
        {
            if (!HasDependencies || !inputGate.TryAcquire(this))
            {
                return;
            }

            _ownsGate = true;
            inputGate.AcquisitionRevoked += HandleGateRevoked;
            backpackView.Show(_projection.CaptureProjection());
        }

        private void CloseBackpack()
        {
            if (_ownsGate)
            {
                inputGate.AcquisitionRevoked -= HandleGateRevoked;
                inputGate.Release(this);
                _ownsGate = false;
            }

            backpackView?.Hide();
        }

        private void HandleGateRevoked()
        {
            if (inputGate != null)
            {
                inputGate.AcquisitionRevoked -= HandleGateRevoked;
            }

            _ownsGate = false;
            backpackView?.Hide();
        }
    }
}
