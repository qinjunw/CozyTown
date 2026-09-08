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
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Save;
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
    public sealed class EconomyUiInteractionPlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private Scene _scene;
        private GameObject _hud;
        private GameObject _world;
        private GameObject _player;
        private DevelopmentSceneInputTestFixture _input;
        private Mouse _mouse;

        [SetUp]
        public void SetUp()
        {
            _input = new DevelopmentSceneInputTestFixture();
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
            Debug.Log($"Economy input test screen={Screen.width}x{Screen.height}");
        }

        [UnityTest]
        public IEnumerator ScrollWheel_OverLabelBlankSpaceAndButtonReachesLastPlot()
        {
            yield return Load();
            Open(TownInteractionKind.Farm);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var row = Rows("Farm Panel")[0];
            var scroll = row.GetComponentInParent<ScrollRect>();
            var blank = RectTransformUtility.WorldToScreenPoint(null, scroll.viewport.TransformPoint(
                new Vector3(scroll.viewport.rect.xMax - 2f, scroll.viewport.rect.yMax - 2f)));
            var points = new[] { Center(row.Label.rectTransform), blank, Center((RectTransform)row.Buttons[0].transform) };
            foreach (Vector2 point in points)
            {
                yield return Wheel(point, 120);
                Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0f).Within(0.1f));
                yield return Wheel(point, -120);
                Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0f));
                AssertButtonInViewport(Rows("Farm Panel").Last().Buttons[0], scroll);
            }
        }

        [UnityTest]
        public IEnumerator Mouse_CompletesProductionCookingTradeAndSaveRestore()
        {
            yield return Load();
            var shop = _hud.GetComponent<CozyTownShopDebugView>();
            var farm = _hud.GetComponent<CozyTownFarmDebugView>();
            var coop = _hud.GetComponent<CozyTownCoopDebugView>();
            var pond = _hud.GetComponent<CozyTownPondDebugView>();
            var kitchen = _hud.GetComponent<CozyTownKitchenDebugView>();
            Open(TownInteractionKind.Shop);
            yield return null;
            yield return ClickInList(Row("Shop Panel", "Carrot Seed").Buttons[0]);
            yield return ClickInList(Row("Shop Panel", "Chicken Feed").Buttons[0]);
            yield return ClickInList(Row("Shop Panel", "Chicken Feed").Buttons[0]);
            Assert.That(shop.State.CharacterBalance, Is.EqualTo(255));
            shop.RequestClose();

            Open(TownInteractionKind.Farm);
            yield return null;
            int seedIndex = farm.State.SeedOptions.ToList().FindIndex(option => option.SeedItemId == DefaultMvpIds.Items.CarrotSeed);
            yield return ClickInList(Rows("Farm Panel")[0].Buttons[seedIndex]);
            yield return ClickInList(Rows("Farm Panel")[0].Buttons[3]);
            Assert.That(farm.State.Plots[0].WateredToday, Is.True);
            farm.RequestClose();
            Open(TownInteractionKind.Coop);
            yield return null;
            yield return ClickInList(Rows("Coop Panel")[0].Buttons[0]);
            Assert.That(coop.State.Animals[0].FedToday, Is.True);
            coop.RequestClose();

            for (int morning = 1; morning <= 3; morning++)
            {
                Open(TownInteractionKind.Bed);
                yield return null;
                for (int sleep = 0; sleep < 3; sleep++) yield return Click(ActiveButton("Sleep Button"));
                _hud.GetComponent<CozyTownBedDebugView>().RequestClose();
                Open(TownInteractionKind.Farm);
                yield return null;
                Assert.That(farm.State.Plots[0].GrowthProgressDays, Is.EqualTo(morning));
                if (morning < 3) yield return ClickInList(Rows("Farm Panel")[0].Buttons[3]);
                else yield return ClickInList(Rows("Farm Panel")[0].Buttons[4]);
                farm.RequestClose();
                if (morning == 1)
                {
                    Open(TownInteractionKind.Coop);
                    yield return null;
                    Assert.That(Rows("Coop Panel")[0].Buttons[0].interactable, Is.False);
                    yield return ClickInList(Rows("Coop Panel")[0].Buttons[1]);
                    yield return ClickInList(Rows("Coop Panel")[0].Buttons[0]);
                    Assert.That(coop.State.Animals[0].OwnedFeedQuantity, Is.Zero);
                    coop.RequestClose();
                    Open(TownInteractionKind.Shop);
                    yield return null;
                    yield return ClickInList(Row("Shop Panel", "Salt").Buttons[0]);
                    shop.RequestClose();
                }
            }
            Assert.That(farm.State.Plots[0].Status, Is.EqualTo(FarmPlotStatus.Empty));
            Open(TownInteractionKind.Pond);
            _hud.GetComponent<CozyTownPondDebugPresenter>().SetRollSource(new FixedRoll());
            yield return null;
            yield return Click(ActiveButton("Cast Button"));
            Assert.That(pond.State.Entries.Single(item => item.ItemId == DefaultMvpIds.Items.Carp).OwnedQuantity, Is.EqualTo(1));
            pond.RequestClose();
            Open(TownInteractionKind.Kitchen);
            yield return null;
            yield return ClickInList(Row("Kitchen Panel", "Grilled Fish").Buttons[0]);
            Assert.That(kitchen.Feedback, Is.EqualTo("Cooked Grilled Fish x1."));
            kitchen.RequestClose();

            yield return Click(ActiveButton("Gear Button"));
            yield return Click(ActiveButton("Save Button"));
            Assert.That(_hud.GetComponent<CozyTownSaveDebugView>().Feedback, Is.EqualTo("Game saved."));
            yield return Click(ActiveButton("Gear Button"));
            Open(TownInteractionKind.Shop);
            yield return null;
            Assert.That(shop.State.SaleItems.Single(item => item.ItemId == DefaultMvpIds.Items.Carrot).Quantity, Is.EqualTo(2));
            Assert.That(shop.State.SaleItems.Single(item => item.ItemId == DefaultMvpIds.Items.Egg).Quantity, Is.EqualTo(1));
            int coinsBeforeSale = shop.State.CharacterBalance;
            Assert.That(coinsBeforeSale, Is.EqualTo(250));
            yield return Click(ActiveButton("Sell Tab"));
            yield return ClickInList(Row("Shop Panel", "Grilled Fish").Buttons[0]);
            Assert.That(shop.State.CharacterBalance, Is.EqualTo(coinsBeforeSale + 55));
            Assert.That(shop.State.SaleItems.Any(item => item.ItemId == DefaultMvpIds.Items.GrilledFish), Is.False);
            shop.RequestClose();
            yield return Click(ActiveButton("Gear Button"));
            yield return Click(ActiveButton("Load Button"));
            Assert.That(_hud.GetComponent<CozyTownSaveDebugView>().Feedback, Is.EqualTo("Game loaded."));
            yield return Click(ActiveButton("Gear Button"));
            Open(TownInteractionKind.Shop);
            Assert.That(shop.State.CharacterBalance, Is.EqualTo(coinsBeforeSale));
            Assert.That(shop.State.SaleItems.Single(item => item.ItemId == DefaultMvpIds.Items.GrilledFish).Quantity, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SellTab_UsesVisibleMouseEntryAndUpdatesActualAssets()
        {
            yield return Load();
            Open(TownInteractionKind.Pond);
            var pond = _hud.GetComponent<CozyTownPondDebugView>();
            _hud.GetComponent<CozyTownPondDebugPresenter>().SetRollSource(new FixedRoll());
            pond.RequestCatch();
            pond.RequestClose();
            Open(TownInteractionKind.Shop);
            yield return null;
            var shop = _hud.GetComponent<CozyTownShopDebugView>();
            var carp = shop.State.SaleItems.Single(item => item.ItemId == DefaultMvpIds.Items.Carp);
            int coinsBefore = shop.State.CharacterBalance;
            var sellTab = _hud.GetComponentsInChildren<Button>()
                .SingleOrDefault(button => button.name == "Sell Tab");
            Assert.That(sellTab, Is.Not.Null, "Shop must expose a visible Sell entry above the list.");
            yield return Click(sellTab);
            var row = Rows("Shop Panel").Single();
            Assert.That(row.Label.text, Does.StartWith("Carp"));
            yield return Click(row.Buttons[0]);
            Assert.That(shop.State.CharacterBalance, Is.EqualTo(coinsBefore + carp.UnitPrice));
            Assert.That(shop.State.SaleItems, Is.Empty);
            Assert.That(Rows("Shop Panel"), Is.Empty);
            Assert.That(_hud.GetComponentsInChildren<Text>()
                .Any(label => label.text.Contains("No items to sell")), Is.True);
        }

        [UnityTest]
        public IEnumerator ShopList_KeepsItemsReachableAtEndAfterSalesAndWhenReopened()
        {
            yield return Load();
            Open(TownInteractionKind.Pond);
            var pond = _hud.GetComponent<CozyTownPondDebugView>();
            foreach (int roll in new[] { 0, 40, 70 })
            {
                _hud.GetComponent<CozyTownPondDebugPresenter>().SetRollSource(new FixedRoll(roll));
                pond.RequestCatch();
            }
            pond.RequestClose();
            Open(TownInteractionKind.Shop);
            yield return null;
            var shop = _hud.GetComponent<CozyTownShopDebugView>();
            var scroll = Rows("Shop Panel")[0].GetComponentInParent<ScrollRect>();
            yield return Wheel(Center(Rows("Shop Panel")[0].Label.rectTransform), -120);
            AssertButtonInViewport(Rows("Shop Panel").Last().Buttons[0], scroll);

            yield return Click(_hud.GetComponentsInChildren<Button>().Single(button => button.name == "Sell Tab"));
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0f).Within(0.1f), "Changing tabs starts at the first item.");
            yield return Wheel(Center(Rows("Shop Panel")[0].Label.rectTransform), -120);
            var lastSale = Rows("Shop Panel").Last().Buttons[0];
            AssertButtonInViewport(lastSale, scroll);
            yield return Click(lastSale);
            Assert.That(Rows("Shop Panel"), Has.Length.EqualTo(2));
            yield return Wheel(Center(scroll.viewport), -120);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0f).Within(0.1f), "Two sale rows fit the viewport without an empty scrolling tail.");
            foreach (int remaining in new[] { 1, 0 })
            {
                yield return Click(Rows("Shop Panel").Last().Buttons[0]);
                Assert.That(Rows("Shop Panel"), Has.Length.EqualTo(remaining));
            }
            yield return Wheel(Center(scroll.viewport), -120);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0f).Within(0.1f));

            yield return Click(_hud.GetComponentsInChildren<Button>().Single(button => button.name == "Buy Tab"));
            yield return Wheel(Center(Rows("Shop Panel")[0].Label.rectTransform), -120);
            shop.RequestClose();
            Open(TownInteractionKind.Shop);
            yield return null;
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0f).Within(0.1f), "Reopening starts at the first purchase item.");
            AssertButtonInViewport(Rows("Shop Panel")[0].Buttons[0], scroll);
        }

        private IEnumerator Wheel(Vector2 point, float amount)
        {
            _input.Set(_mouse.position, point);
            yield return null;
            _input.Set(_mouse.scroll, new Vector2(0, amount));
            yield return null;
            yield return null;
        }

        private static void AssertButtonInViewport(Button button, ScrollRect scroll)
        {
            Assert.That(RectTransformUtility.RectangleContainsScreenPoint(
                scroll.viewport, Center((RectTransform)button.transform)), Is.True,
                "Scrolling to the end must leave the last item visible.");
        }

        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.That(button.IsInteractable(), Is.True, button.name);
            var center = Center((RectTransform)button.transform);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = center }, hits);
            var list = button.GetComponentInParent<ScrollRect>();
            Assert.That(hits.Count, Is.GreaterThan(0), $"{button.transform.parent.name}/{button.name} must receive a raycast; "
                + $"center={center}, listOffset={list?.content.anchoredPosition}, graphicCulled={button.targetGraphic.canvasRenderer.cull}, "
                + $"raycastTarget={button.targetGraphic.raycastTarget}.");
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), button.name + " must be the first raycast target.");
            _input.Set(_mouse.position, center);
            yield return null;
            _input.Press(_mouse.leftButton);
            yield return null;
            _input.Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        private IEnumerator ClickInList(Button button)
        {
            var scroll = button.GetComponentInParent<ScrollRect>();
            Canvas.ForceUpdateCanvases();
            for (int attempt = 0; scroll != null && attempt < 20; attempt++)
            {
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, button.transform);
                if (bounds.min.y >= scroll.viewport.rect.yMin + 1f && bounds.max.y <= scroll.viewport.rect.yMax - 1f) break;
                float direction = bounds.min.y < scroll.viewport.rect.yMin + 1f ? -1f : 1f;
                yield return Wheel(Center(scroll.viewport), direction * 0.25f);
            }
            yield return Click(button);
        }

        private Button ActiveButton(string name) => _hud.GetComponentsInChildren<Button>().Single(button => button.name == name);

        private static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
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
            private readonly int _roll;
            public FixedRoll(int roll = 0) { _roll = roll; }
            public int NextRoll() => _roll;
        }
    }
}
#endif
