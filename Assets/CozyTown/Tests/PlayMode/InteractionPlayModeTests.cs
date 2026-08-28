using System.Collections;
using System.Collections.Generic;
using CozyTown.Unity.Interaction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class InteractionPlayModeTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [UnityTest]
        public IEnumerator Probe_IgnoresInactiveAndDisabledCandidates_AndSelectsNearestInteractable()
        {
            var actor = CreateObject("Actor", Vector2.zero);
            var probe = actor.AddComponent<InteractionProbe2D>();

            var inactive = CreateCandidate("Inactive", new Vector2(0.1f, 0f));
            inactive.gameObject.SetActive(false);

            var disabled = CreateCandidate("Disabled", new Vector2(0.2f, 0f));
            disabled.enabled = false;

            var closestActive = CreateCandidate("Closest active", new Vector2(0.3f, 0f));
            CreateCandidate("Farther active", new Vector2(0.6f, 0f));

            yield return null;
            Physics2D.SyncTransforms();

            var found = probe.TryFindClosest(new InteractionContext(actor), out var selected);

            Assert.That(found, Is.True);
            Assert.That(selected, Is.SameAs(closestActive));
        }

        [UnityTest]
        public IEnumerator ConsecutiveInputEdges_TriggerOncePerEdgeAndUpdatePromptFeedback()
        {
            var actor = CreateObject("Actor", Vector2.zero, false);
            var input = actor.AddComponent<PlayModePlayerInputSource>();
            var probe = actor.AddComponent<InteractionProbe2D>();
            var interactor = actor.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);

            var targetObject = CreateObject("Target", new Vector2(0.3f, 0f));
            targetObject.AddComponent<CircleCollider2D>().radius = 0.04f;
            var target = targetObject.AddComponent<TownInteractionPoint2D>();
            target.Configure(TownInteractionKind.Npc, "Talk");

            actor.SetActive(true);
            Physics2D.SyncTransforms();

            yield return null;
            Assert.That(interactor.CurrentPrompt, Is.EqualTo("Talk"));

            input.PressInteract();
            yield return null;
            Assert.That(target.InteractionCount, Is.EqualTo(1));
            Assert.That(interactor.LastInteractionFeedback, Does.Contain("Talk"));

            input.PressInteract();
            yield return null;
            Assert.That(target.InteractionCount, Is.EqualTo(2));

            yield return null;
            Assert.That(target.InteractionCount, Is.EqualTo(2));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in _objects)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            _objects.Clear();
        }

        private CountingInteractable CreateCandidate(string name, Vector2 position)
        {
            var gameObject = CreateObject(name, position);
            gameObject.AddComponent<CircleCollider2D>().radius = 0.04f;
            return gameObject.AddComponent<CountingInteractable>();
        }

        private GameObject CreateObject(string name, Vector2 position, bool active = true)
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);
            gameObject.transform.position = position;
            gameObject.SetActive(active);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
