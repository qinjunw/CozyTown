#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Farming;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Shop;
using CozyTown.Unity.Time;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CozyTown.Tests.PlayMode
{
    // Audit probe: copy into the PlayMode test assembly to reproduce the report.
    public sealed class EconomyInteractionAuditPlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private Scene _scene;
        private GameObject _hud;
        private GameObject _world;
        private GameObject _player;
        private InputTestFixture _input;
        private Mouse _mouse;

        [SetUp]
        public void SetUp()
        {
            _input = new InputTestFixture();
            _input.Setup();
            _mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.AddDevice<Keyboard>();
        }

        private IEnumerator Load()
        {
            Assert.That(Application.isBatchMode, Is.True, "Use batch mode so Bootstrap uses an isolated in-memory save slot.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
            _hud = Root("Debug HUD");
            _world = Root("World");
            _player = Root("Player");
            Root("CozyTown").GetComponent<DaytimeClockDriver>().SetApplicationFocus(false);
            yield return null;
            Assert.That(EventSystem.current.gameObject.scene, Is.EqualTo(_scene));
            Debug.Log($"AUDIT screen={Screen.width}x{Screen.height}");
        }

        [UnityTest]
        public IEnumerator ActualMouse_PurchasesPlantsWatersFeedsAndSells()
        {
            yield return Load();
            Open(TownInteractionKind.Shop);
            yield return null;
            var shop = _hud.GetComponent<CozyTownShopDebugView>();
            var beforeCoins = shop.State.CharacterBalance;
            var seed = shop.State.PurchaseItems.First(item => IsSeed(item.ItemId));
            yield return Click(Row("Shop Panel", seed.DisplayName).Buttons[0]);
            Assert.That(shop.State.CharacterBalance, Is.EqualTo(beforeCoins - seed.UnitPrice), "Mouse purchase must change the actual wallet.");
            shop.RequestBuy(DefaultMvpIds.Items.ChickenFeed);
            shop.RequestClose();

            Open(TownInteractionKind.Farm);
            yield return null;
            var farm = _hud.GetComponent<CozyTownFarmDebugView>();
            var farmRow = Rows("Farm Panel")[0];
            var seedIndex = farm.State.SeedOptions.ToList().FindIndex(option => option.SeedItemId == seed.ItemId);
            yield return Click(farmRow.Buttons[seedIndex]);
            Assert.That(farm.State.Plots[0].Status, Is.EqualTo(FarmPlotStatus.Growing));
            yield return Click(farmRow.Buttons[3]);
            Assert.That(farm.State.Plots[0].WateredToday, Is.True);
            farm.RequestClose();

            Open(TownInteractionKind.Coop);
            yield return null;
            var coop = _hud.GetComponent<CozyTownCoopDebugView>();
            yield return Click(Rows("Coop Panel")[0].Buttons[0]);
            Assert.That(coop.State.Animals[0].FedToday, Is.True);
            coop.RequestClose();

            Open(TownInteractionKind.Pond);
            _hud.GetComponent<CozyTownPondDebugPresenter>().SetRollSource(new FixedRoll());
            _hud.GetComponent<CozyTownPondDebugView>().RequestCatch();
            _hud.GetComponent<CozyTownPondDebugView>().RequestClose();
            Open(TownInteractionKind.Shop);
            yield return null;
            var carp = shop.State.SaleItems.Single(item => item.ItemId == DefaultMvpIds.Items.Carp);
            beforeCoins = shop.State.CharacterBalance;
            yield return Click(Row("Shop Panel", "Carp").Buttons[0]);
            Assert.That(shop.State.CharacterBalance, Is.EqualTo(beforeCoins + carp.UnitPrice));
            Assert.That(shop.State.SaleItems.Any(item => item.ItemId == DefaultMvpIds.Items.Carp), Is.False);
            Debug.Log("AUDIT actual mouse: buy, plant, water, feed, sell passed; rows explicitly scrolled into view.");
        }

        [UnityTest]
        public IEnumerator ScrollWheel_OverProductLabelReachesShopList()
        {
            yield return Load();
            Open(TownInteractionKind.Shop);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var row = Rows("Shop Panel")[0];
            var scroll = row.GetComponentInParent<ScrollRect>();
            var point = Center(row.Label.rectTransform);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Debug.Log("AUDIT label hit targets=" + string.Join(",", hits.Select(hit => hit.gameObject.name)));
            _input.Set(_mouse.position, point);
            yield return null;
            float before = scroll.content.anchoredPosition.y;
            _input.Set(_mouse.scroll, new Vector2(0, -120));
            yield return null;
            yield return null;
            float after = scroll.content.anchoredPosition.y;
            Debug.Log($"AUDIT shop label wheel offset before={before}, after={after}");
            Assert.That(after, Is.GreaterThan(before), "Scrolling over the product label must move the list.");
        }

        [UnityTest]
        public IEnumerator MorningSettlement_RefreshesWorldSpritesWithoutReopeningPanels()
        {
            yield return Load();
            PrepareProduction();
            SleepOneDay();
            yield return null;
            var before = _world.GetComponentsInChildren<SpriteRenderer>(true)
                .ToDictionary(renderer => renderer, renderer => renderer.sprite);
            Open(TownInteractionKind.Farm);
            var farm = _hud.GetComponent<CozyTownFarmDebugView>();
            Assert.That(farm.State.Plots[0].GrowthProgressDays, Is.EqualTo(1));
            farm.RequestClose();
            Open(TownInteractionKind.Coop);
            var coop = _hud.GetComponent<CozyTownCoopDebugView>();
            Assert.That(coop.State.Animals[0].ProductReady, Is.True);
            Assert.That(coop.State.Animals[0].FedToday, Is.False);
            Assert.That(coop.State.Animals[0].OwnedFeedQuantity, Is.GreaterThan(0));
            coop.RequestClose();
            var changed = _world.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(renderer => before[renderer] != renderer.sprite)
                .Select(renderer => renderer.name + ": " + before[renderer]?.name + " -> " + renderer.sprite?.name).ToArray();
            Debug.Log("AUDIT sprites changed only after reopening=" + string.Join("; ", changed));
            Assert.That(changed, Is.Empty, "Already-settled world sprites must not need a panel to be reopened.");
        }

        [UnityTest]
        public IEnumerator PendingEgg_DisablesFeedUntilCollected()
        {
            yield return Load();
            PrepareProduction();
            SleepOneDay();
            Open(TownInteractionKind.Coop);
            yield return null;
            var coop = _hud.GetComponent<CozyTownCoopDebugView>();
            Assert.That(coop.State.Animals[0].ProductReady, Is.True);
            var feed = Rows("Coop Panel")[0].Buttons[0];
            bool enabled = feed.interactable;
            if (enabled) yield return Click(feed);
            Debug.Log($"AUDIT pending egg feed enabled={enabled}, feedback={coop.Feedback}, feedOwned={coop.State.Animals[0].OwnedFeedQuantity}");
            Assert.That(enabled, Is.False, "The visible Feed button must agree with the pending-product rule.");
        }

        private void PrepareProduction()
        {
            Open(TownInteractionKind.Shop);
            var shop = _hud.GetComponent<CozyTownShopDebugView>();
            var seed = shop.State.PurchaseItems.First(item => IsSeed(item.ItemId));
            int coinsBefore = shop.State.CharacterBalance;
            shop.RequestBuy(seed.ItemId);
            Assert.That(shop.State.CharacterBalance, Is.EqualTo(coinsBefore - seed.UnitPrice));
            shop.RequestBuy(DefaultMvpIds.Items.ChickenFeed);
            shop.RequestBuy(DefaultMvpIds.Items.ChickenFeed);
            shop.RequestClose();
            Open(TownInteractionKind.Farm);
            var farm = _hud.GetComponent<CozyTownFarmDebugView>();
            farm.RequestPlant(farm.State.Plots[0].PlotId, seed.ItemId);
            farm.RequestWater(farm.State.Plots[0].PlotId);
            Assert.That(farm.State.Plots[0].WateredToday, Is.True);
            farm.RequestClose();
            Open(TownInteractionKind.Coop);
            var coop = _hud.GetComponent<CozyTownCoopDebugView>();
            coop.RequestFeed(coop.State.Animals[0].AnimalId);
            Assert.That(coop.State.Animals[0].FedToday, Is.True);
            coop.RequestClose();
        }

        private void SleepOneDay()
        {
            Open(TownInteractionKind.Bed);
            var bed = _hud.GetComponent<CozyTownBedDebugView>();
            bed.RequestSleep();
            bed.RequestSleep();
            bed.RequestSleep();
            bed.RequestClose();
        }

        private IEnumerator Click(Button button)
        {
            var scroll = button.GetComponentInParent<ScrollRect>();
            if (scroll != null)
            {
                var row = button.GetComponentInParent<CozyTownUiListRow>();
                float rowTop = -((RectTransform)row.transform).anchoredPosition.y;
                scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(rowTop, 0, Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height)));
            }
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.That(button.IsInteractable(), Is.True, button.name);
            var center = Center((RectTransform)button.transform);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = center }, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), button.name + " must receive a raycast.");
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), button.name + " must be the first raycast target.");
            _input.Set(_mouse.position, center);
            yield return null;
            _input.Press(_mouse.leftButton);
            yield return null;
            _input.Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        private static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        private static bool IsSeed(string id) => id == DefaultMvpIds.Items.PotatoSeed || id == DefaultMvpIds.Items.CarrotSeed || id == DefaultMvpIds.Items.TomatoSeed;
        private GameObject Root(string name) => _scene.GetRootGameObjects().Single(root => root.name == name);
        private void Open(TownInteractionKind kind) => _world.GetComponentsInChildren<TownInteractionPoint2D>(true).Single(point => point.Kind == kind).Interact(new InteractionContext(_player));
        private CozyTownUiListRow[] Rows(string panel) => _hud.GetComponentsInChildren<CozyTownUiListRow>(false).Where(row => row.transform.GetComponentsInParent<Transform>().Any(parent => parent.name == panel)).OrderBy(row => row.name).ToArray();
        private CozyTownUiListRow Row(string panel, string label) => Rows(panel).Single(row => row.Label.text.StartsWith(label, StringComparison.Ordinal));

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            _input?.TearDown();
        }

        private sealed class FixedRoll : IFishingRollSource
        {
            public int NextRoll() => 0;
        }
    }
}
#endif
