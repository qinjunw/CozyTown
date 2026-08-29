#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Unity.Core;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using CozyTown.Unity.Shop;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Save;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class DevelopmentScenePlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        private Scene _loadedScene;

        [UnityTest]
        public IEnumerator DevelopmentScene_StartsWalkingAndShopTradingSlice()
        {
            var loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return loadOperation;
            yield return null;

            _loadedScene = SceneManager.GetSceneByPath(ScenePath);
            Assert.That(_loadedScene.IsValid(), Is.True);
            Assert.That(_loadedScene.isLoaded, Is.True);

            var bootstrap = RequireRoot(_loadedScene, "CozyTown")
                .GetComponent<CozyTownBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.IsInitialized, Is.True);

            var player = RequireRoot(_loadedScene, "Player");
            Assert.That(player.GetComponent<InputSystemPlayerInputSource>()?.enabled, Is.True);
            var body = player.GetComponent<Rigidbody2D>();
            var movement = player.GetComponent<PlayerMovement2D>();
            var probe = player.GetComponent<InteractionProbe2D>();
            var interactor = player.GetComponent<PlayerInteractor2D>();
            Assert.That(body, Is.Not.Null);
            Assert.That(movement?.enabled, Is.True);
            Assert.That(probe?.enabled, Is.True);
            Assert.That(interactor?.enabled, Is.True);

            var testInput = player.AddComponent<PlayModePlayerInputSource>();
            movement.SetInputSource(testInput);
            interactor.Configure(testInput, probe);

            var startPosition = body.position;
            testInput.Movement = Vector2.right;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            testInput.Movement = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            Assert.That(body.position.x, Is.GreaterThan(startPosition.x));

            var world = RequireRoot(_loadedScene, "World");
            var eastBoundary = world.transform.Find("Boundaries/East Boundary")
                ?.GetComponent<BoxCollider2D>();
            Assert.That(eastBoundary, Is.Not.Null);
            var boundaryInnerEdge = eastBoundary.bounds.min.x;

            body.position = new Vector2(boundaryInnerEdge - 1f, 0f);
            player.transform.position = body.position;
            Physics2D.SyncTransforms();
            testInput.Movement = Vector2.right;
            for (var fixedFrame = 0; fixedFrame < 20; fixedFrame++)
            {
                yield return new WaitForFixedUpdate();
            }

            testInput.Movement = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            Assert.That(body.position.x, Is.LessThan(boundaryInnerEdge));

            var points = world.GetComponentsInChildren<TownInteractionPoint2D>(true);
            Assert.That(points, Has.Length.EqualTo(7));
            var hud = RequireRoot(_loadedScene, "Debug HUD");
            var shopView = hud.GetComponent<CozyTownShopDebugView>();
            var shopPresenter = hud.GetComponent<CozyTownShopDebugPresenter>();
            var farmView = hud.GetComponent<CozyTownFarmDebugView>();
            var bedView = hud.GetComponent<CozyTownBedDebugView>();
            var coopView = hud.GetComponent<CozyTownCoopDebugView>();
            var pondView = hud.GetComponent<CozyTownPondDebugView>();
            var pondPresenter = hud.GetComponent<CozyTownPondDebugPresenter>();
            var kitchenView = hud.GetComponent<CozyTownKitchenDebugView>();
            var npcView = hud.GetComponent<CozyTownNpcDebugView>();
            var saveView = hud.GetComponent<CozyTownSaveDebugView>();
            Assert.That(shopView, Is.Not.Null);
            Assert.That(shopPresenter, Is.Not.Null);
            Assert.That(farmView, Is.Not.Null);
            Assert.That(bedView, Is.Not.Null);
            Assert.That(coopView, Is.Not.Null);
            Assert.That(pondView, Is.Not.Null);
            Assert.That(kitchenView, Is.Not.Null);
            Assert.That(npcView, Is.Not.Null);
            Assert.That(saveView, Is.Not.Null);
            Assert.That(saveView.IsVisible, Is.True);
            pondPresenter.SetRollSource(new FixedFishingRollSource(0));

            IEnumerator Open(TownInteractionKind kind)
            {
                var point = points.Single(candidate => candidate.Kind == kind);
                var position = (Vector2)point.transform.position;
                body.position = position;
                player.transform.position = position;
                Physics2D.SyncTransforms();
                yield return null;
                Assert.That(interactor.CurrentPrompt, Is.EqualTo(point.PromptText));
                testInput.PressInteract();
                yield return null;
            }

            var shopPoint = points.Single(point => point.Kind == TownInteractionKind.Shop);
            var shopPosition = (Vector2)shopPoint.transform.position;
            body.position = shopPosition;
            player.transform.position = shopPosition;
            Physics2D.SyncTransforms();
            yield return null;
            Assert.That(interactor.CurrentPrompt, Is.EqualTo(shopPoint.PromptText));

            testInput.PressInteract();
            yield return null;
            Assert.That(shopView.IsVisible, Is.True);
            Assert.That(shopPresenter.IsOpen, Is.True);
            Assert.That(movement.enabled, Is.False);
            Assert.That(interactor.enabled, Is.False);
            Assert.That(shopView.State.Balance, Is.EqualTo(300));

            shopView.RequestBuy(DefaultMvpIds.Items.PotatoSeed);
            Assert.That(shopView.State.Balance, Is.EqualTo(280));
            Assert.That(
                shopView.State.Items.Single(item => item.ItemId == DefaultMvpIds.Items.PotatoSeed).OwnedQuantity,
                Is.EqualTo(1));
            Assert.That(shopView.Feedback, Is.EqualTo("Bought 1 x Potato Seed for 20 coins."));

            shopView.RequestSell(DefaultMvpIds.Items.Potato);
            Assert.That(shopView.State.Balance, Is.EqualTo(280));
            Assert.That(shopView.Feedback, Is.EqualTo(
                "Sell failed: inventory.insufficient_quantity"));
            shopView.RequestBuy(DefaultMvpIds.Items.ChickenFeed);
            shopView.RequestBuy(DefaultMvpIds.Items.Salt);
            shopView.RequestBuy(DefaultMvpIds.Items.Salt);
            Assert.That(shopView.State.Balance, Is.EqualTo(260));
            shopView.RequestClose();
            Assert.That(shopView.IsVisible, Is.False);
            Assert.That(movement.enabled, Is.True);
            Assert.That(interactor.enabled, Is.True);

            yield return Open(TownInteractionKind.Farm);
            farmView.RequestPlant("plot.01", DefaultMvpIds.Items.PotatoSeed);
            farmView.RequestWater("plot.01");
            farmView.RequestClose();
            yield return Open(TownInteractionKind.Coop);
            coopView.RequestFeed(DefaultMvpIds.Livestock.Hen);
            coopView.RequestClose();
            yield return Open(TownInteractionKind.Pond);
            pondView.RequestCatch();
            Assert.That(pondView.State.Entries.Single(e => e.ItemId == DefaultMvpIds.Items.Carp).OwnedQuantity, Is.EqualTo(1));
            pondView.RequestClose();
            yield return Open(TownInteractionKind.Bed);
            bedView.RequestSleep();
            Assert.That(bedView.Feedback, Is.EqualTo("Slept to day 2."));
            bedView.RequestClose();
            yield return Open(TownInteractionKind.Coop);
            coopView.RequestCollect(DefaultMvpIds.Livestock.Hen);
            coopView.RequestClose();
            yield return Open(TownInteractionKind.Farm);
            farmView.RequestWater("plot.01");
            farmView.RequestClose();
            yield return Open(TownInteractionKind.Bed);
            bedView.RequestSleep();
            Assert.That(bedView.Feedback, Is.EqualTo("Slept to day 3."));
            bedView.RequestClose();
            yield return Open(TownInteractionKind.Farm);
            farmView.RequestHarvest("plot.01");
            farmView.RequestClose();
            yield return Open(TownInteractionKind.Kitchen);
            kitchenView.RequestCook(DefaultMvpIds.Recipes.BakedPotato);
            kitchenView.RequestCook(DefaultMvpIds.Recipes.GrilledFish);
            kitchenView.RequestClose();
            yield return Open(TownInteractionKind.Shop);
            shopView.RequestSell(DefaultMvpIds.Items.Potato);
            shopView.RequestSell(DefaultMvpIds.Items.BakedPotato);
            shopView.RequestSell(DefaultMvpIds.Items.GrilledFish);
            shopView.RequestSell(DefaultMvpIds.Items.Egg);
            Assert.That(shopView.State.Balance, Is.EqualTo(405));
            shopView.RequestBuy(DefaultMvpIds.Items.PotatoSeed);
            Assert.That(shopView.State.Balance, Is.EqualTo(385));
            Assert.That(shopView.State.Items.Single(i => i.ItemId == DefaultMvpIds.Items.PotatoSeed).OwnedQuantity, Is.EqualTo(1));
            shopView.RequestClose();

            foreach (var point in points)
            {
                var targetPosition = (Vector2)point.transform.position;
                body.position = targetPosition;
                player.transform.position = targetPosition;
                Physics2D.SyncTransforms();
                yield return null;

                Assert.That(interactor.CurrentPrompt, Is.EqualTo(point.PromptText));
                var previousCount = point.InteractionCount;
                testInput.PressInteract();
                yield return null;

                Assert.That(point.InteractionCount, Is.EqualTo(previousCount + 1));
                Assert.That(interactor.LastInteractionFeedback, Does.Contain(point.PromptText));
                if (shopView.IsVisible)
                {
                    shopView.RequestClose();
                }
                if (farmView.IsVisible)
                {
                    farmView.RequestClose();
                }
                if (bedView.IsVisible)
                {
                    bedView.RequestClose();
                }
                if (coopView.IsVisible)
                {
                    coopView.RequestClose();
                }
                if (pondView.IsVisible)
                {
                    pondView.RequestClose();
                }
                if (kitchenView.IsVisible)
                {
                    kitchenView.RequestClose();
                }
                if (npcView.IsVisible)
                {
                    Assert.That(npcView.State, Is.Not.Null);
                    Assert.That(npcView.State.IsFallback, Is.True);
                    Assert.That(npcView.NpcCount, Is.EqualTo(4));
                    npcView.RequestClose();
                }
            }

            Assert.That(hud.GetComponent<CozyTownHudPresenter>()?.enabled, Is.True);
            Assert.That(hud.GetComponent<CozyTownInteractionDebugView>()?.enabled, Is.True);
        }

        [UnityTearDown]
        public IEnumerator UnloadDevelopmentScene()
        {
            if (!_loadedScene.IsValid() || !_loadedScene.isLoaded)
            {
                yield break;
            }

            var unloadOperation = SceneManager.UnloadSceneAsync(_loadedScene);
            if (unloadOperation != null)
            {
                yield return unloadOperation;
            }
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            var root = Array.Find(
                scene.GetRootGameObjects(),
                candidate => candidate.name == name);
            Assert.That(root, Is.Not.Null, $"Root object '{name}' was not found.");
            return root;
        }

        private sealed class FixedFishingRollSource : IFishingRollSource
        {
            private readonly int _roll;
            public FixedFishingRollSource(int roll) => _roll = roll;
            public int NextRoll() => _roll;
        }
    }
}
#endif
