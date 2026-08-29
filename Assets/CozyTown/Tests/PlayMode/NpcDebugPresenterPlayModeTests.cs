using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Npc;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class NpcDebugPresenterPlayModeTests
    {
        private GameObject _actor;
        private GameObject _pointObject;
        private GameObject _presenterObject;

        [UnityTest]
        public IEnumerator Interaction_GeneratesDialogueAndTalkAgainUsesNarrowCoordinator()
        {
            var coordinator = new StubCoordinator();
            var point = CreatePoint();
            var presenter = CreatePresenter(point, out CozyTownNpcDebugView view);
            presenter.Bind(coordinator);
            CreateActor();

            point.Interact(new InteractionContext(_actor));
            yield return null;

            Assert.That(presenter.IsOpen, Is.True);
            Assert.That(view.IsVisible, Is.True);
            Assert.That(view.State.Text, Is.EqualTo("Dialogue 1"));
            Assert.That(coordinator.Calls, Is.EqualTo(1));
            view.RequestTalk();
            yield return null;
            Assert.That(view.State.Text, Is.EqualTo("Dialogue 2"));
            Assert.That(coordinator.Calls, Is.EqualTo(2));

            view.RequestClose();
            Assert.That(presenter.IsOpen, Is.False);
            Assert.That(view.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator CloseBeforeCompletion_IgnoresLateDialogueResult()
        {
            var coordinator = new DeferredCoordinator();
            var point = CreatePoint();
            var presenter = CreatePresenter(point, out CozyTownNpcDebugView view);
            presenter.Bind(coordinator);
            CreateActor();

            point.Interact(new InteractionContext(_actor));
            yield return null;
            Assert.That(view.IsLoading, Is.True);

            view.RequestClose();
            coordinator.Complete();
            yield return null;

            Assert.That(presenter.IsOpen, Is.False);
            Assert.That(view.IsVisible, Is.False);
            Assert.That(view.State, Is.Null);
        }

        [TearDown]
        public void TearDown()
        {
            Destroy(_presenterObject);
            Destroy(_pointObject);
            Destroy(_actor);
        }

        private TownInteractionPoint2D CreatePoint()
        {
            _pointObject = new GameObject("NPC Point");
            var point = _pointObject.AddComponent<TownInteractionPoint2D>();
            point.Configure(TownInteractionKind.Npc, "Talk");
            return point;
        }

        private CozyTownNpcDebugPresenter CreatePresenter(
            TownInteractionPoint2D point,
            out CozyTownNpcDebugView view)
        {
            _presenterObject = new GameObject("NPC UI");
            view = _presenterObject.AddComponent<CozyTownNpcDebugView>();
            var presenter = _presenterObject.AddComponent<CozyTownNpcDebugPresenter>();
            presenter.Configure(point, view, "npc.farmer_eli");
            return presenter;
        }

        private void CreateActor()
        {
            _actor = new GameObject("Actor");
            _actor.SetActive(false);
            _actor.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var input = _actor.AddComponent<PlayModePlayerInputSource>();
            var movement = _actor.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(input);
            var probe = _actor.AddComponent<InteractionProbe2D>();
            var interactor = _actor.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);
            _actor.AddComponent<PlayerModalInputGate2D>();
            _actor.SetActive(true);
        }

        private static void Destroy(Object target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }

        private sealed class StubCoordinator : INpcDialogueCoordinator
        {
            public IReadOnlyList<NpcDialogueOption> Npcs { get; } =
                new[] { new NpcDialogueOption("npc.farmer_eli", "Eli") };

            public int Calls { get; private set; }

            public Task<NpcDialogueViewState> GenerateAsync(
                string npcId,
                CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new NpcDialogueViewState(
                    npcId,
                    "Eli",
                    $"Dialogue {Calls}",
                    "neutral",
                    "idle",
                    false,
                    $"request-{Calls}",
                    NpcDialogueFallbackReason.None));
            }
        }

        private sealed class DeferredCoordinator : INpcDialogueCoordinator
        {
            private readonly TaskCompletionSource<NpcDialogueViewState> _completion =
                new TaskCompletionSource<NpcDialogueViewState>();

            public IReadOnlyList<NpcDialogueOption> Npcs { get; } =
                new[] { new NpcDialogueOption("npc.farmer_eli", "Eli") };

            public Task<NpcDialogueViewState> GenerateAsync(
                string npcId,
                CancellationToken cancellationToken)
            {
                return _completion.Task;
            }

            public void Complete()
            {
                _completion.TrySetResult(new NpcDialogueViewState(
                    "npc.farmer_eli",
                    "Eli",
                    "Late dialogue",
                    "neutral",
                    "idle",
                    false,
                    "request-late",
                    NpcDialogueFallbackReason.None));
            }
        }
    }
}
