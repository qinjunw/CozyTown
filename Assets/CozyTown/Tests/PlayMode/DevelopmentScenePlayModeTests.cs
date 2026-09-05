#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Unity.Core;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Inventory;
using CozyTown.Unity.Player;
using CozyTown.Unity.Shop;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Save;
using CozyTown.Unity.Time;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CozyTown.Tests.PlayMode
{
    public sealed class DevelopmentScenePlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        private Scene _loadedScene;

        [UnityTest]
        public IEnumerator DevelopmentScene_SmallAcceptedIntervalMovesMinaOnceAtConfiguredSpeed()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _loadedScene = SceneManager.GetSceneByPath(ScenePath);
            var root = RequireRoot(_loadedScene, "CozyTown");
            var driver = root.GetComponent<DaytimeClockDriver>();
            driver.SetApplicationFocus(false);
            var mina = RequireRoot(_loadedScene, "World").GetComponentsInChildren<NpcWorldResident2D>(true)
                .Single(npc => npc.NpcId == DefaultMvpIds.Npcs.Shopkeeper);
            Vector2 initial = mina.Position;
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            driver.AdvanceFrame(0.25);
            driver.SetApplicationFocus(false);
            Assert.That(Vector2.Distance(initial, mina.Position), Is.EqualTo(0.5f).Within(0.001));
        }

        [UnityTest]
        public IEnumerator DevelopmentScene_MinaTraversesActualRoadsAndReturnsHomeBeforeNextMorning()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _loadedScene = SceneManager.GetSceneByPath(ScenePath);
            var driver = RequireRoot(_loadedScene, "CozyTown").GetComponent<DaytimeClockDriver>();
            driver.SetApplicationFocus(false);
            var world = RequireRoot(_loadedScene, "World");
            var mina = world.GetComponentsInChildren<NpcWorldResident2D>(true)
                .Single(npc => npc.NpcId == DefaultMvpIds.Npcs.Shopkeeper);
            var map = world.GetComponent<TownMap2D>();
            Assert.That(map.TryGetLocation("work.shopkeeper_mina", out var work), Is.True);
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            driver.AdvanceFrame(10);
            Assert.That(Vector2.Distance(mina.Position, work), Is.LessThan(0.001f));
            Assert.That(mina.Status, Is.EqualTo(TownRouteStatus.Arrived));
            driver.SetApplicationFocus(false);
            Vector2 paused = mina.Position;
            driver.AdvanceFrame(600);
            Assert.That(mina.Position, Is.EqualTo(paused));
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            driver.AdvanceFrame(300);
            driver.AdvanceFrame(20);
            Assert.That(mina.IsHome, Is.False, "17:00 starts the return journey; it is not an arrival command.");
            driver.AdvanceFrame(30);
            Assert.That(mina.IsHome, Is.True);
            Assert.That(mina.GetComponentInChildren<SpriteRenderer>(true).enabled, Is.False);
            driver.AdvanceFrame(360);
            Assert.That(mina.IsHome, Is.False);
            driver.AdvanceFrame(10);
            Assert.That(Vector2.Distance(mina.Position, work), Is.LessThan(0.001f));
            Assert.That(mina.Status, Is.EqualTo(TownRouteStatus.Arrived));
            driver.SetApplicationFocus(false);
        }

        [UnityTest]
        public IEnumerator DevelopmentScene_ClockUpdatesHudAndSystemMenuLoadUsesSameTimeline()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _loadedScene = SceneManager.GetSceneByPath(ScenePath);
            var driver = RequireRoot(_loadedScene, "CozyTown").GetComponent<DaytimeClockDriver>();
            Assert.That(driver, Is.Not.Null);
            var hud = RequireRoot(_loadedScene, "Debug HUD");
            var clockText = hud.GetComponentsInChildren<Text>(true).Single(text => text.name == "Clock Text");
            var gear = hud.GetComponentsInChildren<Button>(true).Single(button => button.name == "Gear Button");
            var save = hud.GetComponent<CozyTownSaveDebugView>();
            yield return null;

            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            driver.AdvanceFrame(5);
            driver.SetApplicationFocus(false);
            yield return null;
            Assert.That(clockText.text, Is.EqualTo("Day 1  06:10"));

            gear.onClick.Invoke();
            Assert.That(driver.IsSimulationPaused, Is.True);
            save.RequestSave();
            Assert.That(save.HasSave, Is.True);
            driver.AdvanceFrame(300);
            gear.onClick.Invoke();
            Assert.That(driver.IsSimulationPaused, Is.True, "Closing the menu must not clear focus pause.");
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(300);
            driver.AdvanceFrame(5);
            driver.SetApplicationFocus(false);
            yield return null;
            Assert.That(clockText.text, Is.EqualTo("Day 1  06:20"));

            gear.onClick.Invoke();
            save.RequestLoad();
            driver.AdvanceFrame(300);
            yield return null;
            Assert.That(clockText.text, Is.EqualTo("Day 1  06:10"));
            Assert.That(driver.IsSimulationPaused, Is.True);
            gear.onClick.Invoke();
        }

        [UnityTest]
        public IEnumerator DevelopmentScene_BedButtonsSelectTwoHoursAndUpdateSharedHudClock()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _loadedScene = SceneManager.GetSceneByPath(ScenePath);
            var driver = RequireRoot(_loadedScene, "CozyTown").GetComponent<DaytimeClockDriver>();
            driver.SetApplicationFocus(false);
            var player = RequireRoot(_loadedScene, "Player");
            var gate = player.GetComponent<PlayerModalInputGate2D>();
            var hud = RequireRoot(_loadedScene, "Debug HUD");
            var view = hud.GetComponent<CozyTownBedDebugView>();
            var clockText = hud.GetComponentsInChildren<Text>(true).Single(text => text.name == "Clock Text");
            var panel = hud.transform.Find("Production UI/Bed Panel");
            var hoursText = panel.GetComponentsInChildren<Text>(true).Single(text => text.name == "Sleep Hours Text");
            Button[] buttons = panel.GetComponentsInChildren<Button>(true);
            var decrease = buttons.Single(button => button.name == "Decrease Sleep Button");
            var increase = buttons.Single(button => button.name == "Increase Sleep Button");
            var confirm = buttons.Single(button => button.name == "Sleep Button");
            var close = buttons.Single(button => button.name == "Close Button");
            yield return null;
            Assert.That(clockText.text, Is.EqualTo("Day 1  06:00"));

            var bed = RequireRoot(_loadedScene, "World")
                .GetComponentsInChildren<TownInteractionPoint2D>(true)
                .Single(point => point.Kind == TownInteractionKind.Bed);
            bed.Interact(new InteractionContext(player));
            driver.SetApplicationFocus(true);
            Assert.That(view.IsVisible, Is.True);
            Assert.That(hoursText.text, Is.EqualTo("8 hours"));
            Assert.That(gate.IsAcquired, Is.True);
            Assert.That(driver.IsSimulationPaused, Is.True);

            increase.onClick.Invoke();
            for (int click = 0; click < 7; click++)
            {
                decrease.onClick.Invoke();
            }
            driver.AdvanceFrame(300);
            yield return null;
            Assert.That(hoursText.text, Is.EqualTo("2 hours"));
            Assert.That(clockText.text, Is.EqualTo("Day 1  06:00"));

            confirm.onClick.Invoke();
            driver.AdvanceFrame(300);
            yield return null;
            Assert.That(view.Feedback, Is.EqualTo("Slept to Day 1 08:00."));
            Assert.That(hoursText.text, Is.EqualTo("2 hours"));
            Assert.That(clockText.text, Is.EqualTo("Day 1  08:00"));
            Assert.That(gate.IsAcquired, Is.True);
            Assert.That(player.GetComponent<PlayerMovement2D>().enabled, Is.False);
            Assert.That(player.GetComponent<PlayerInteractor2D>().enabled, Is.False);

            close.onClick.Invoke();
            Assert.That(view.IsVisible, Is.False);
            Assert.That(gate.IsAcquired, Is.False);
            Assert.That(driver.IsSimulationPaused, Is.False);
            Assert.That(player.GetComponent<PlayerMovement2D>().enabled, Is.True);
            Assert.That(player.GetComponent<PlayerInteractor2D>().enabled, Is.True);
            driver.SetApplicationFocus(false);
            yield return null;
            Assert.That(clockText.text, Is.EqualTo("Day 1  08:00"));
        }

        [UnityTest]
        public IEnumerator DevelopmentScene_StartsWalkingAndShopTradingSlice()
        {
            var loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return loadOperation;
            _loadedScene = SceneManager.GetSceneByPath(ScenePath);
            RequireRoot(_loadedScene, "CozyTown").GetComponent<DaytimeClockDriver>()
                .SetApplicationFocus(false);
            yield return null;

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
            var playerRenderer = player.GetComponentInChildren<SpriteRenderer>(true);
            Assert.That(body, Is.Not.Null);
            Assert.That(movement?.enabled, Is.True);
            Assert.That(probe?.enabled, Is.True);
            Assert.That(interactor?.enabled, Is.True);
            Assert.That(playerRenderer, Is.Not.Null);

            var testInput = player.AddComponent<PlayModePlayerInputSource>();
            movement.SetInputSource(testInput);
            interactor.Configure(testInput, probe);

            var startPosition = body.position;
            testInput.Movement = Vector2.right;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(playerRenderer.sprite.name, Does.StartWith("chr_player_walk_right_"));
            testInput.Movement = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            yield return null;
            Assert.That(playerRenderer.sprite.name, Is.EqualTo("chr_player_idle_right"));
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
            Assert.That(points, Has.Length.EqualTo(10));
            var hud = RequireRoot(_loadedScene, "Debug HUD");
            var shopView = hud.GetComponent<CozyTownShopDebugView>();
            var shopPresenter = hud.GetComponent<CozyTownShopDebugPresenter>();
            var farmView = hud.GetComponent<CozyTownFarmDebugView>();
            var bedView = hud.GetComponent<CozyTownBedDebugView>();
            var sleepButton = hud.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "Sleep Button");
            var coopView = hud.GetComponent<CozyTownCoopDebugView>();
            var pondView = hud.GetComponent<CozyTownPondDebugView>();
            var pondPresenter = hud.GetComponent<CozyTownPondDebugPresenter>();
            var kitchenView = hud.GetComponent<CozyTownKitchenDebugView>();
            var npcView = hud.GetComponent<CozyTownNpcDebugView>();
            var saveView = hud.GetComponent<CozyTownSaveDebugView>();
            var systemMenuView = hud.GetComponent<CozyTownSystemMenuView>();
            var inventoryPresenter = hud.GetComponent<CozyTownInventoryPresenter>();
            var backpackView = hud.GetComponent<CozyTownBackpackView>();
            var hotbarView = hud.GetComponent<CozyTownHotbarView>();
            Assert.That(shopView, Is.Not.Null);
            Assert.That(shopPresenter, Is.Not.Null);
            Assert.That(farmView, Is.Not.Null);
            Assert.That(bedView, Is.Not.Null);
            Assert.That(coopView, Is.Not.Null);
            Assert.That(pondView, Is.Not.Null);
            Assert.That(kitchenView, Is.Not.Null);
            Assert.That(npcView, Is.Not.Null);
            Assert.That(saveView, Is.Not.Null);
            Assert.That(systemMenuView, Is.Not.Null);
            Assert.That(inventoryPresenter, Is.Not.Null);
            Assert.That(backpackView, Is.Not.Null);
            Assert.That(hotbarView, Is.Not.Null);
            Assert.That(saveView.IsVisible, Is.True);
            pondPresenter.SetRollSource(new FixedFishingRollSource(0));

            var pondPointBeforeSave = points.Single(
                point => point.Kind == TownInteractionKind.Pond);
            var pondPositionBeforeSave = (Vector2)pondPointBeforeSave.transform.position;
            body.position = pondPositionBeforeSave;
            player.transform.position = pondPositionBeforeSave;
            Physics2D.SyncTransforms();
            yield return null;
            Assert.That(
                interactor.CurrentPrompt,
                Is.EqualTo(pondPointBeforeSave.PromptText));
            testInput.PressInteract();
            yield return null;
            pondView.RequestCatch();
            Assert.That(
                pondView.State.Entries.Single(
                    entry => entry.ItemId == DefaultMvpIds.Items.Carp).OwnedQuantity,
                Is.EqualTo(1));
            pondView.RequestClose();

            var gearButton = RequireActiveButtonByIcon(hud, "ui_icon_settings");
            gearButton.onClick.Invoke();
            Assert.That(systemMenuView.IsVisible, Is.True);
            Assert.That(movement.enabled, Is.False);
            Assert.That(interactor.enabled, Is.False);
            var saveButton = RequireActiveButtonByIcon(hud, "ui_icon_save");
            var loadButton = RequireActiveButtonByIcon(hud, "ui_icon_load");
            Assert.That(loadButton.interactable, Is.False);
            saveButton.onClick.Invoke();
            Assert.That(saveView.Feedback, Is.EqualTo("Game saved."));
            Assert.That(loadButton.interactable, Is.True);
            gearButton.onClick.Invoke();
            Assert.That(systemMenuView.IsVisible, Is.False);
            Assert.That(movement.enabled, Is.True);
            Assert.That(interactor.enabled, Is.True);

            var farmPoint = points.Single(point => point.Kind == TownInteractionKind.Farm);
            var plot01Soil = farmPoint.transform.Find("Farm States/Plot 01/Soil")
                ?.GetComponent<SpriteRenderer>();
            var plot01Crop = farmPoint.transform.Find("Farm States/Plot 01/Crop")
                ?.GetComponent<SpriteRenderer>();
            Assert.That(plot01Soil, Is.Not.Null);
            Assert.That(plot01Crop, Is.Not.Null);
            Assert.That(plot01Soil.sprite.name, Is.EqualTo("farm_plot_soil_dry"));
            Assert.That(plot01Crop.sprite, Is.Null);

            var coopPoint = points.Single(point => point.Kind == TownInteractionKind.Coop);
            var henRenderer = coopPoint.transform.Find("Hen State")
                ?.GetComponent<SpriteRenderer>();
            Assert.That(henRenderer, Is.Not.Null);
            Assert.That(henRenderer.sprite.name, Is.EqualTo("animal_hen_idle"));

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
            Assert.That(shopView.State.CharacterBalance, Is.EqualTo(300));
            Assert.That(shopView.State.ShopBalance, Is.EqualTo(10000));
            int savedFeedStock = shopView.State.PurchaseItems.Single(
                item => item.ItemId == DefaultMvpIds.Items.ChickenFeed).Quantity;

            shopView.RequestBuy(DefaultMvpIds.Items.ChickenFeed);
            Assert.That(shopView.State.CharacterBalance, Is.EqualTo(290));
            Assert.That(shopView.State.ShopBalance, Is.EqualTo(10010));
            Assert.That(
                shopView.State.PurchaseItems.Single(
                    item => item.ItemId == DefaultMvpIds.Items.ChickenFeed).Quantity,
                Is.EqualTo(savedFeedStock - 1));
            Assert.That(shopView.Feedback, Is.EqualTo(
                "Bought 1 x Chicken Feed for 10 coins."));
            shopView.RequestSell(DefaultMvpIds.Items.Carp);
            Assert.That(shopView.State.CharacterBalance, Is.EqualTo(315));
            Assert.That(shopView.State.ShopBalance, Is.EqualTo(9985));
            Assert.That(
                shopView.State.SaleItems.Any(
                    item => item.ItemId == DefaultMvpIds.Items.Carp),
                Is.False);
            shopView.RequestClose();

            gearButton.onClick.Invoke();
            loadButton.onClick.Invoke();
            Assert.That(saveView.Feedback, Is.EqualTo("Game loaded."));
            gearButton.onClick.Invoke();
            yield return Open(TownInteractionKind.Shop);
            Assert.That(shopView.State.CharacterBalance, Is.EqualTo(300));
            Assert.That(shopView.State.ShopBalance, Is.EqualTo(10000));
            Assert.That(
                shopView.State.PurchaseItems.Single(
                    item => item.ItemId == DefaultMvpIds.Items.ChickenFeed).Quantity,
                Is.EqualTo(savedFeedStock));
            Assert.That(
                shopView.State.SaleItems.Single(
                    item => item.ItemId == DefaultMvpIds.Items.Carp).Quantity,
                Is.EqualTo(1));
            shopView.RequestBuy(DefaultMvpIds.Items.ChickenFeed);
            shopView.RequestClose();

            yield return Open(TownInteractionKind.Bed);
            sleepButton.onClick.Invoke();
            sleepButton.onClick.Invoke();
            sleepButton.onClick.Invoke();
            Assert.That(bedView.Feedback, Is.EqualTo("Slept to Day 2 06:00."));
            bedView.RequestClose();
            yield return Open(TownInteractionKind.Shop);
            shopView.RequestBuy(DefaultMvpIds.Items.PotatoSeed);
            shopView.RequestBuy(DefaultMvpIds.Items.Salt);
            shopView.RequestBuy(DefaultMvpIds.Items.Salt);
            Assert.That(shopView.State.CharacterBalance, Is.EqualTo(260));
            RequireActiveButtonByIcon(hud, "ui_icon_close").onClick.Invoke();
            Assert.That(shopView.IsVisible, Is.False);
            Assert.That(movement.enabled, Is.True);
            Assert.That(interactor.enabled, Is.True);

            yield return Open(TownInteractionKind.Farm);
            farmView.RequestPlant("plot.01", DefaultMvpIds.Items.PotatoSeed);
            farmView.RequestWater("plot.01");
            Assert.That(plot01Soil.sprite.name, Is.EqualTo("farm_plot_soil_watered"));
            Assert.That(plot01Crop.sprite.name, Is.EqualTo("crop_potato_stage_00"));
            farmView.RequestClose();
            yield return Open(TownInteractionKind.Coop);
            coopView.RequestFeed(DefaultMvpIds.Livestock.Hen);
            Assert.That(henRenderer.sprite.name, Is.EqualTo("animal_hen_fed"));
            coopView.RequestClose();
            yield return Open(TownInteractionKind.Pond);
            pondView.RequestCatch();
            Assert.That(pondView.State.Entries.Single(e => e.ItemId == DefaultMvpIds.Items.Carp).OwnedQuantity, Is.EqualTo(2));
            pondView.RequestClose();
            yield return Open(TownInteractionKind.Bed);
            sleepButton.onClick.Invoke();
            sleepButton.onClick.Invoke();
            sleepButton.onClick.Invoke();
            Assert.That(bedView.Feedback, Is.EqualTo("Slept to Day 3 06:00."));
            bedView.RequestClose();
            yield return Open(TownInteractionKind.Coop);
            Assert.That(henRenderer.sprite.name, Is.EqualTo("animal_hen_product_ready"));
            coopView.RequestCollect(DefaultMvpIds.Livestock.Hen);
            Assert.That(henRenderer.sprite.name, Is.EqualTo("animal_hen_idle"));
            coopView.RequestClose();
            yield return Open(TownInteractionKind.Farm);
            farmView.RequestWater("plot.01");
            farmView.RequestClose();
            yield return Open(TownInteractionKind.Bed);
            sleepButton.onClick.Invoke();
            sleepButton.onClick.Invoke();
            sleepButton.onClick.Invoke();
            Assert.That(bedView.Feedback, Is.EqualTo("Slept to Day 4 06:00."));
            bedView.RequestClose();
            yield return Open(TownInteractionKind.Farm);
            farmView.RequestHarvest("plot.01");
            Assert.That(plot01Soil.sprite.name, Is.EqualTo("farm_plot_soil_dry"));
            Assert.That(plot01Crop.sprite, Is.Null);
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
            Assert.That(shopView.State.CharacterBalance, Is.EqualTo(405));
            ShopTradingLineItem availableItem = shopView.State.PurchaseItems.First();
            shopView.RequestBuy(availableItem.ItemId);
            Assert.That(
                shopView.State.CharacterBalance,
                Is.EqualTo(405 - availableItem.UnitPrice));
            shopView.RequestClose();

            // Visit every NPC during working hours; Sora is still at home at 06:00.
            var routineClock = RequireRoot(_loadedScene, "CozyTown").GetComponent<DaytimeClockDriver>();
            routineClock.SetApplicationFocus(true);
            routineClock.AdvanceFrame(0);
            routineClock.AdvanceFrame(75);
            routineClock.SetApplicationFocus(false);

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
                    Assert.That(npcView.NpcCount, Is.EqualTo(1));
                    Assert.That(
                        npcView.CurrentNpcId,
                        Is.EqualTo(point.GetComponent<CozyTownNpcDebugPresenter>()?.NpcId));
                    npcView.RequestClose();
                }
            }

            Assert.That(hud.GetComponent<CozyTownHudPresenter>()?.enabled, Is.True);
            Assert.That(hud.GetComponent<CozyTownInteractionDebugView>(), Is.Null);
            Assert.That(hud.GetComponent<CozyTownInteractionBubbleView>()?.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator DevelopmentScene_DisablingProductionViewsHidesPanelsAndDisconnectsRows()
        {
            var loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return loadOperation;
            yield return null;

            _loadedScene = SceneManager.GetSceneByPath(ScenePath);
            var hud = RequireRoot(_loadedScene, "Debug HUD");
            var view = hud.GetComponent<CozyTownShopDebugView>();
            Assert.That(view, Is.Not.Null);
            view.Show(
                new ShopTradingViewState(
                    characterBalance: 300,
                    shopBalance: 10000,
                    new[]
                    {
                        new ShopTradingLineItem(
                            DefaultMvpIds.Items.PotatoSeed,
                            "Potato Seed",
                            20,
                            10)
                    },
                    Array.Empty<ShopTradingLineItem>()),
                string.Empty);

            var row = hud.GetComponentsInChildren<CozyTownUiListRow>(false)
                .Single(candidate => candidate.Label.text.StartsWith("Potato Seed", StringComparison.Ordinal));
            var buyButton = row.Buttons[0];
            var buyCalls = 0;
            view.BuyRequested += _ => buyCalls++;
            view.enabled = false;

            Assert.That(row.gameObject.activeInHierarchy, Is.False);
            buyButton.onClick.Invoke();
            Assert.That(buyCalls, Is.Zero);

            view.enabled = true;
            yield return null;
            Assert.That(row.gameObject.activeInHierarchy, Is.True);
            row.Buttons[0].onClick.Invoke();
            Assert.That(buyCalls, Is.EqualTo(1));
            view.Hide();

            var coop = hud.GetComponent<CozyTownCoopDebugView>();
            coop.Show(
                new LivestockViewState(
                    new[]
                    {
                        new AnimalView(
                            DefaultMvpIds.Livestock.Hen,
                            "species.chicken",
                            DefaultMvpIds.Items.ChickenFeed,
                            "Chicken Feed",
                            1,
                            DefaultMvpIds.Items.Egg,
                            "Egg",
                            0,
                            false,
                            false)
                    }),
                string.Empty);
            var coopRow = hud.GetComponentsInChildren<CozyTownUiListRow>(false)
                .Single(candidate => candidate.Label.text.StartsWith(DefaultMvpIds.Livestock.Hen, StringComparison.Ordinal));
            var feedCalls = 0;
            coop.FeedRequested += _ => feedCalls++;
            var feedButton = coopRow.Buttons[0];
            coop.enabled = false;
            Assert.That(coopRow.gameObject.activeInHierarchy, Is.False);
            feedButton.onClick.Invoke();
            Assert.That(feedCalls, Is.Zero);
            coop.enabled = true;
            yield return null;
            Assert.That(coopRow.gameObject.activeInHierarchy, Is.True);
            coopRow.Buttons[0].onClick.Invoke();
            Assert.That(feedCalls, Is.EqualTo(1));
            coop.Hide();

            var kitchen = hud.GetComponent<CozyTownKitchenDebugView>();
            kitchen.Show(
                new CookingViewState(
                    new[]
                    {
                        new RecipeView(
                            DefaultMvpIds.Recipes.BakedPotato,
                            DefaultMvpIds.Items.BakedPotato,
                            "Baked Potato",
                            1,
                            true,
                            Array.Empty<RecipeIngredientView>())
                    }),
                string.Empty);
            var kitchenRow = hud.GetComponentsInChildren<CozyTownUiListRow>(false)
                .Single(candidate => candidate.Label.text.StartsWith("Baked Potato", StringComparison.Ordinal));
            var cookCalls = 0;
            kitchen.CookRequested += _ => cookCalls++;
            var cookButton = kitchenRow.Buttons[0];
            kitchen.enabled = false;
            Assert.That(kitchenRow.gameObject.activeInHierarchy, Is.False);
            cookButton.onClick.Invoke();
            Assert.That(cookCalls, Is.Zero);
            kitchen.enabled = true;
            yield return null;
            Assert.That(kitchenRow.gameObject.activeInHierarchy, Is.True);
            kitchenRow.Buttons[0].onClick.Invoke();
            Assert.That(cookCalls, Is.EqualTo(1));
            kitchen.Hide();
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

        private static Button RequireActiveButtonByIcon(GameObject root, string spriteName)
        {
            var button = root.GetComponentsInChildren<Button>(false)
                .SingleOrDefault(candidate => candidate
                    .GetComponentsInChildren<Image>(true)
                    .Any(image => image.sprite != null && image.sprite.name == spriteName));
            Assert.That(button, Is.Not.Null, $"Active button with icon '{spriteName}' was not found.");
            return button;
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
