using System;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using CozyTown.Unity.Pond;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Tests.PlayMode
{
    public sealed class GameplayFeedbackPlayModeTests
    {
        private GameObject _root;
        private CozyTownPondDebugView _pondView;
        private Text _feedback;
        private Button _castButton;

        [Test]
        public void CastWithoutABite_RealButtonExplainsRetryWithoutChangingCatchQuantities()
        {
            CreatePondFixture(99);

            _castButton.onClick.Invoke();

            Assert.That(_feedback.text, Is.EqualTo("No bite this time. Try casting again."));
            Assert.That(_pondView.State.Entries.All(entry => entry.OwnedQuantity == 0), Is.True);
        }

        [Test]
        public void CatchFish_RealButtonShowsDisplayNameAndAddsCatch()
        {
            CreatePondFixture(0);

            _castButton.onClick.Invoke();

            Assert.That(_feedback.text, Is.EqualTo("Caught Carp."));
            Assert.That(
                _pondView.State.Entries.Single(entry => entry.ItemId == DefaultMvpIds.Items.Carp).OwnedQuantity,
                Is.EqualTo(1));
        }

        private void CreatePondFixture(int roll)
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            _root = new GameObject("Gameplay Feedback Test");
            _root.SetActive(false);

            var actor = Child("Actor", _root.transform);
            actor.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var input = actor.AddComponent<PlayModePlayerInputSource>();
            actor.AddComponent<PlayerMovement2D>().SetInputSource(input);
            var probe = actor.AddComponent<InteractionProbe2D>();
            actor.AddComponent<PlayerInteractor2D>().Configure(input, probe);
            actor.AddComponent<PlayerModalInputGate2D>();

            var point = Child("Pond", _root.transform).AddComponent<TownInteractionPoint2D>();
            point.Configure(TownInteractionKind.Pond, "Fish");
            var hud = Child("Pond HUD", _root.transform);
            _pondView = hud.AddComponent<CozyTownPondDebugView>();
            var panel = UiChild("Pond Panel", hud.transform);
            _feedback = UiChild("Feedback", panel.transform).AddComponent<Text>();
            var closeButton = UiChild("Close", panel.transform).AddComponent<Button>();
            _castButton = UiChild("Cast", panel.transform).AddComponent<Button>();
            var rowObject = UiChild("Fish Row", panel.transform);
            var row = rowObject.AddComponent<CozyTownUiListRow>();
            row.Configure(
                UiChild("Label", rowObject.transform).AddComponent<Text>(),
                UiChild("Icon", rowObject.transform).AddComponent<Image>(),
                Array.Empty<Button>(),
                Array.Empty<Text>());
            _pondView.ConfigureUi(
                panel, _feedback, new[] { row }, closeButton, _castButton,
                hud.AddComponent<CozyTownUiIconCatalog>());
            var presenter = hud.AddComponent<CozyTownPondDebugPresenter>();
            presenter.Configure(point, _pondView);
            presenter.Bind(services.FishingGameplay);
            presenter.SetRollSource(new FixedRollSource(roll));
            _root.SetActive(true);
            point.Interact(new InteractionContext(actor));
            Assert.That(_pondView.IsVisible, Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        private static GameObject Child(string name, Transform parent)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static GameObject UiChild(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
        }

        private sealed class FixedRollSource : IFishingRollSource
        {
            private readonly int _roll;

            public FixedRollSource(int roll) => _roll = roll;

            public int NextRoll() => _roll;
        }
    }
}
