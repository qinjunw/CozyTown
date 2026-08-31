using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        private GameObject _secondPointObject;
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

        [UnityTest]
        public IEnumerator SharedView_OnlyOpenPresenterHandlesEventsAndProjectsItsNpc()
        {
            var coordinator = new MultiNpcCoordinator();
            var minaPoint = CreatePoint();
            _secondPointObject = new GameObject("Eli NPC Point");
            var eliPoint = _secondPointObject.AddComponent<TownInteractionPoint2D>();
            eliPoint.Configure(TownInteractionKind.Npc, "Talk to Eli");

            _presenterObject = new GameObject("Shared NPC UI");
            var view = _presenterObject.AddComponent<CozyTownNpcDebugView>();
            var minaPresenter = _presenterObject.AddComponent<CozyTownNpcDebugPresenter>();
            minaPresenter.Configure(minaPoint, view, "npc.shopkeeper_mina");
            minaPresenter.Bind(coordinator);
            var eliPresenter = _presenterObject.AddComponent<CozyTownNpcDebugPresenter>();
            eliPresenter.Configure(eliPoint, view, "npc.farmer_eli");
            eliPresenter.Bind(coordinator);
            CreateActor();

            Assert.That(minaPresenter.NpcId, Is.EqualTo("npc.shopkeeper_mina"));
            Assert.That(eliPresenter.NpcId, Is.EqualTo("npc.farmer_eli"));

            minaPoint.Interact(new InteractionContext(_actor));
            yield return null;

            Assert.That(view.CurrentNpcId, Is.EqualTo("npc.shopkeeper_mina"));
            Assert.That(view.NpcCount, Is.EqualTo(1));
            CollectionAssert.AreEqual(
                new[] { "npc.shopkeeper_mina" },
                coordinator.RequestedNpcIds);

            view.RequestNpc("npc.farmer_eli");
            yield return null;
            Assert.That(view.CurrentNpcId, Is.EqualTo("npc.shopkeeper_mina"));
            Assert.That(coordinator.RequestedNpcIds, Has.Count.EqualTo(1));

            view.RequestTalk();
            yield return null;

            CollectionAssert.AreEqual(
                new[] { "npc.shopkeeper_mina", "npc.shopkeeper_mina" },
                coordinator.RequestedNpcIds);

            view.RequestClose();
            Assert.That(minaPresenter.IsOpen, Is.False);
            Assert.That(eliPresenter.IsOpen, Is.False);
            Assert.That(view.IsVisible, Is.False);

            eliPoint.Interact(new InteractionContext(_actor));
            yield return null;

            Assert.That(view.CurrentNpcId, Is.EqualTo("npc.farmer_eli"));
            Assert.That(view.NpcCount, Is.EqualTo(1));
            Assert.That(coordinator.RequestedNpcIds.Last(), Is.EqualTo("npc.farmer_eli"));
            Assert.That(coordinator.RequestedNpcIds, Has.Count.EqualTo(3));
        }

        [TearDown]
        public void TearDown()
        {
            Destroy(_presenterObject);
            Destroy(_pointObject);
            Destroy(_secondPointObject);
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

        private sealed class MultiNpcCoordinator : INpcDialogueCoordinator
        {
            public IReadOnlyList<NpcDialogueOption> Npcs { get; } = new[]
            {
                new NpcDialogueOption("npc.shopkeeper_mina", "Mina"),
                new NpcDialogueOption("npc.farmer_eli", "Eli"),
                new NpcDialogueOption("npc.fisher_ren", "Ren"),
                new NpcDialogueOption("npc.cook_sora", "Sora")
            };

            public List<string> RequestedNpcIds { get; } = new List<string>();

            public Task<NpcDialogueViewState> GenerateAsync(
                string npcId,
                CancellationToken cancellationToken)
            {
                RequestedNpcIds.Add(npcId);
                string displayName = Npcs.Single(npc => npc.NpcId == npcId).DisplayName;
                return Task.FromResult(new NpcDialogueViewState(
                    npcId,
                    displayName,
                    $"Hello from {displayName}.",
                    "neutral",
                    "idle",
                    false,
                    $"request-{RequestedNpcIds.Count}",
                    NpcDialogueFallbackReason.None));
            }
        }
    }
}
