using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Interaction;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    public sealed class CozyTownNpcDebugPresenter : CozyTownModalPresenterBase
    {
        [SerializeField] private CozyTownNpcDebugView _view;
        [SerializeField] private string _defaultNpcId = string.Empty;

        private INpcDialogueCoordinator _coordinator;
        private CancellationTokenSource _dialogueCancellation;
        private int _requestVersion;
        private bool _ownsView;

        public string NpcId => _defaultNpcId ?? string.Empty;

        protected override TownInteractionKind ExpectedKind => TownInteractionKind.Npc;

        protected override bool HasDependencies =>
            _coordinator != null
            && _view != null
            && !string.IsNullOrWhiteSpace(_defaultNpcId);

        public void Configure(
            TownInteractionPoint2D point,
            CozyTownNpcDebugView view,
            string defaultNpcId)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            if (string.IsNullOrWhiteSpace(defaultNpcId))
            {
                throw new ArgumentException("Default NPC ID must not be empty.", nameof(defaultNpcId));
            }

            _defaultNpcId = defaultNpcId;
            ConfigureInteraction(point);
        }

        public void Bind(INpcDialogueCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            DependenciesChanged();
        }

        protected override void SubscribeView()
        {
            _view.TalkRequested += TalkAgain;
            _view.NpcRequested += RequestDialogue;
            _view.CloseRequested += HandleCloseRequested;
        }

        protected override void UnsubscribeView()
        {
            _view.TalkRequested -= TalkAgain;
            _view.NpcRequested -= RequestDialogue;
            _view.CloseRequested -= HandleCloseRequested;
        }

        protected override void ShowInitialState()
        {
            _ownsView = true;
            RequestDialogue(_defaultNpcId);
        }

        protected override void HideView()
        {
            CancelPendingDialogue();
            if (!_ownsView)
            {
                return;
            }

            _ownsView = false;
            _view?.Hide();
        }

        private void TalkAgain()
        {
            if (IsOpen)
            {
                RequestDialogue(_defaultNpcId);
            }
        }

        private void HandleCloseRequested()
        {
            if (IsOpen)
            {
                CloseModal();
            }
        }

        private async void RequestDialogue(string npcId)
        {
            if (!IsOpen
                || !string.Equals(npcId, _defaultNpcId, StringComparison.Ordinal))
            {
                return;
            }

            CancelPendingDialogue();
            int requestVersion = ++_requestVersion;
            var requestCancellation = new CancellationTokenSource();
            _dialogueCancellation = requestCancellation;
            CancellationToken cancellationToken = requestCancellation.Token;
            IReadOnlyList<NpcDialogueOption> npcProjection = ProjectNpc(npcId);
            _view.ShowLoading(npcProjection, npcId);

            try
            {
                NpcDialogueViewState state = await _coordinator.GenerateAsync(
                    npcId,
                    cancellationToken);
                if (CanApply(requestVersion, cancellationToken))
                {
                    _view.Show(state, npcProjection);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                if (CanApply(requestVersion, cancellationToken))
                {
                    _view.ShowFailure(npcProjection, npcId);
                }
            }
            finally
            {
                if (ReferenceEquals(_dialogueCancellation, requestCancellation))
                {
                    _dialogueCancellation = null;
                }

                requestCancellation.Dispose();
            }
        }

        private IReadOnlyList<NpcDialogueOption> ProjectNpc(string npcId)
        {
            foreach (NpcDialogueOption npc in _coordinator.Npcs)
            {
                if (string.Equals(npc.NpcId, npcId, StringComparison.Ordinal))
                {
                    return new[] { npc };
                }
            }

            return Array.Empty<NpcDialogueOption>();
        }

        private bool CanApply(int requestVersion, CancellationToken cancellationToken)
        {
            return requestVersion == _requestVersion
                && !cancellationToken.IsCancellationRequested
                && IsOpen
                && _view != null
                && _view.IsVisible;
        }

        private void CancelPendingDialogue()
        {
            _requestVersion++;
            if (_dialogueCancellation == null)
            {
                return;
            }

            _dialogueCancellation.Cancel();
            _dialogueCancellation = null;
        }
    }
}
