using System.Collections;
using System.Collections.Generic;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Interaction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

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

        [UnityTest]
        public IEnumerator InteractionBubble_FollowsCurrentPromptAnchorAndHidesWhenTargetIsUnavailable()
        {
            var actor = CreateObject("Actor", Vector2.zero, false);
            var input = actor.AddComponent<PlayModePlayerInputSource>();
            var probe = actor.AddComponent<InteractionProbe2D>();
            var interactor = actor.AddComponent<PlayerInteractor2D>();
            interactor.Configure(input, probe);

            var firstTargetObject = CreateObject("First Target", new Vector2(0.3f, 0f));
            firstTargetObject.AddComponent<CircleCollider2D>().radius = 0.04f;
            var firstTarget = firstTargetObject.AddComponent<TownInteractionPoint2D>();
            firstTarget.Configure(TownInteractionKind.Npc, "Talk");
            Assert.That(firstTarget.PromptAnchor, Is.SameAs(firstTarget.transform));
            var firstAnchor = CreateObject("First Prompt Anchor", new Vector2(0.3f, 0.8f)).transform;
            firstAnchor.SetParent(firstTargetObject.transform, true);
            firstTarget.ConfigurePromptAnchor(firstAnchor);

            var secondTargetObject = CreateObject("Second Target", new Vector2(2f, 0f));
            secondTargetObject.AddComponent<CircleCollider2D>().radius = 0.04f;
            var secondTarget = secondTargetObject.AddComponent<TownInteractionPoint2D>();
            secondTarget.Configure(TownInteractionKind.Shop, "Shop");
            var secondAnchor = CreateObject("Second Prompt Anchor", new Vector2(2f, 0.8f)).transform;
            secondAnchor.SetParent(secondTargetObject.transform, true);
            secondTarget.ConfigurePromptAnchor(secondAnchor);

            var cameraObject = CreateObject("World Camera", new Vector2(0f, 0f));
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var worldCamera = cameraObject.AddComponent<Camera>();
            worldCamera.orthographic = true;

            var uiRoot = CreateUiObject("Interaction Bubble UI", false);
            uiRoot.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var bubbleObject = CreateUiObject("Bubble");
            bubbleObject.transform.SetParent(uiRoot.transform, false);
            var bubbleRect = bubbleObject.GetComponent<RectTransform>();
            var keyObject = CreateUiObject("Key Text");
            keyObject.transform.SetParent(bubbleObject.transform, false);
            var keyText = keyObject.AddComponent<Text>();
            var bubbleView = uiRoot.AddComponent<CozyTownInteractionBubbleView>();
            bubbleView.Configure(interactor, worldCamera);
            bubbleView.ConfigureUi(bubbleRect, keyText);

            var observedAnchors = new List<Transform>();
            interactor.CurrentPromptAnchorChanged += observedAnchors.Add;
            actor.SetActive(true);
            uiRoot.SetActive(true);
            Physics2D.SyncTransforms();

            yield return null;
            Assert.That(firstTarget.PromptAnchor, Is.SameAs(firstAnchor));
            Assert.That(interactor.CurrentPromptAnchor, Is.SameAs(firstAnchor));
            Assert.That(observedAnchors, Is.EqualTo(new[] { firstAnchor }));
            Assert.That(bubbleView.IsVisible, Is.True);
            Assert.That(bubbleView.CurrentAnchor, Is.SameAs(firstAnchor));
            Assert.That(keyText.text, Is.EqualTo("E"));
            Assert.That(bubbleObject.activeSelf, Is.True);
            Assert.That(
                Vector3.Distance(
                    bubbleRect.position,
                    worldCamera.WorldToScreenPoint(firstAnchor.position)),
                Is.LessThan(0.001f));

            secondTargetObject.transform.position = new Vector2(0.1f, 0f);
            Physics2D.SyncTransforms();
            yield return null;
            Assert.That(interactor.CurrentPromptAnchor, Is.SameAs(secondAnchor));
            Assert.That(observedAnchors[observedAnchors.Count - 1], Is.SameAs(secondAnchor));
            Assert.That(bubbleView.CurrentAnchor, Is.SameAs(secondAnchor));

            firstTargetObject.transform.position = new Vector2(2f, 0f);
            secondTargetObject.transform.position = new Vector2(2f, 0f);
            Physics2D.SyncTransforms();
            yield return null;
            Assert.That(interactor.CurrentPromptAnchor, Is.Null);
            Assert.That(observedAnchors[observedAnchors.Count - 1], Is.Null);
            Assert.That(bubbleView.IsVisible, Is.False);
            Assert.That(bubbleObject.activeSelf, Is.False);

            firstTargetObject.transform.position = new Vector2(0.2f, 0f);
            Physics2D.SyncTransforms();
            yield return null;
            Assert.That(bubbleView.IsVisible, Is.True);

            interactor.enabled = false;
            Assert.That(interactor.CurrentPromptAnchor, Is.Null);
            Assert.That(bubbleView.IsVisible, Is.False);
            Assert.That(bubbleObject.activeSelf, Is.False);
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

        private GameObject CreateUiObject(string name, bool active = true)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.SetActive(active);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
