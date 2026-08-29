using System;
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
            _view.CloseRequested += CloseModal;
        }

        protected override void UnsubscribeView()
        {
            _view.TalkRequested -= TalkAgain;
            _view.NpcRequested -= RequestDialogue;
            _view.CloseRequested -= CloseModal;
        }

        protected override void ShowInitialState() => RequestDialogue(_defaultNpcId);

        protected override void HideView()
        {
            CancelPendingDialogue();
            _view?.Hide();
        }

        private void TalkAgain()
        {
            if (!string.IsNullOrWhiteSpace(_view.CurrentNpcId))
            {
                RequestDialogue(_view.CurrentNpcId);
            }
        }

        private async void RequestDialogue(string npcId)
        {
            CancelPendingDialogue();
            int requestVersion = ++_requestVersion;
            var requestCancellation = new CancellationTokenSource();
            _dialogueCancellation = requestCancellation;
            CancellationToken cancellationToken = requestCancellation.Token;
            _view.ShowLoading(_coordinator.Npcs, npcId);

            try
            {
                NpcDialogueViewState state = await _coordinator.GenerateAsync(
                    npcId,
                    cancellationToken);
                if (CanApply(requestVersion, cancellationToken))
                {
                    _view.Show(state, _coordinator.Npcs);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                if (CanApply(requestVersion, cancellationToken))
                {
                    _view.ShowFailure(_coordinator.Npcs, npcId);
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
