using CozyTown.Runtime.Application;
using CozyTown.Runtime.Farming;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Save;
using CozyTown.Unity.Shop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class ProductionUiViewsEditModeTests
    {
        private GameObject _root;
        private Texture2D _iconTexture;
        private Sprite _itemIcon;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Production UI Test Root");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_itemIcon);
            Object.DestroyImmediate(_iconTexture);
        }

        [Test]
        public void ShopView_DisablesUnavailableTransactionsAndRoutesButtonClicks()
        {
            _iconTexture = new Texture2D(1, 1);
            _itemIcon = Sprite.Create(
                _iconTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));

            var catalog = _root.AddComponent<CozyTownUiIconCatalog>();
            catalog.Configure(
                new[] { "seed.potato" },
                new[] { _itemIcon },
                System.Array.Empty<string>(),
                System.Array.Empty<Sprite>());

            var rowObject = CreateUiObject("Item Row");
            var row = rowObject.AddComponent<CozyTownUiListRow>();
            var itemLabel = CreateUiObject("Item Label").AddComponent<Text>();
            var itemImage = CreateUiObject("Item Icon").AddComponent<Image>();
            var buyButton = CreateUiObject("Buy Button").AddComponent<Button>();
            var buyLabel = CreateUiObject("Buy Label").AddComponent<Text>();
            var sellButton = CreateUiObject("Sell Button").AddComponent<Button>();
            var sellLabel = CreateUiObject("Sell Label").AddComponent<Text>();
            row.Configure(
                itemLabel,
                itemImage,
                new[] { buyButton, sellButton },
                new[] { buyLabel, sellLabel });

            var panel = CreateUiObject("Shop Panel");
            var balanceText = CreateUiObject("Balance Text").AddComponent<Text>();
            var feedbackText = CreateUiObject("Shop Feedback Text").AddComponent<Text>();
            var closeButton = CreateUiObject("Close Button").AddComponent<Button>();
            var view = _root.AddComponent<CozyTownShopDebugView>();
            view.ConfigureUi(
                panel,
                balanceText,
                feedbackText,
                new[] { row },
                closeButton,
                catalog);

            var boughtId = string.Empty;
            var soldId = string.Empty;
            var closeCalls = 0;
            view.BuyRequested += id => boughtId = id;
            view.SellRequested += id => soldId = id;
            view.CloseRequested += () => closeCalls++;

            view.Show(
                new ShopViewState(
                    balance: 5,
                    items: new[] { new ShopLineItem("seed.potato", "Potato Seed", 10, 3, 0) }),
                feedback: "Not enough coins.");

            Assert.That(panel.activeSelf, Is.True);
            Assert.That(balanceText.text, Is.EqualTo("Town Shop — Coins: 5"));
            Assert.That(feedbackText.text, Is.EqualTo("Not enough coins."));
            Assert.That(itemLabel.text, Is.EqualTo("Potato Seed  Owned: 0"));
            Assert.That(itemImage.sprite, Is.SameAs(_itemIcon));
            Assert.That(buyButton.interactable, Is.False);
            Assert.That(sellButton.interactable, Is.False);

            view.Show(
                new ShopViewState(
                    balance: 20,
                    items: new[] { new ShopLineItem("seed.potato", "Potato Seed", 10, 3, 1) }),
                feedback: string.Empty);
            buyButton.onClick.Invoke();
            sellButton.onClick.Invoke();
            closeButton.onClick.Invoke();

            Assert.That(buyButton.interactable, Is.True);
            Assert.That(sellButton.interactable, Is.True);
            Assert.That(boughtId, Is.EqualTo("seed.potato"));
            Assert.That(soldId, Is.EqualTo("seed.potato"));
            Assert.That(closeCalls, Is.EqualTo(1));
        }

        [Test]
        public void FarmView_UsesInventoryStateAndRoutesVisiblePlotActions()
        {
            EnsureItemIcon();
            var catalog = _root.AddComponent<CozyTownUiIconCatalog>();
            catalog.Configure(
                new[] { "seed.potato" },
                new[] { _itemIcon },
                System.Array.Empty<string>(),
                System.Array.Empty<Sprite>());

            var rowObject = CreateUiObject("Farm Row");
            var row = rowObject.AddComponent<CozyTownUiListRow>();
            var plotLabel = CreateUiObject("Plot Label").AddComponent<Text>();
            var plotIcon = CreateUiObject("Plot Icon").AddComponent<Image>();
            var buttons = new Button[5];
            var buttonLabels = new Text[5];
            for (var index = 0; index < buttons.Length; index++)
            {
                buttons[index] = CreateUiObject($"Farm Button {index}").AddComponent<Button>();
                buttonLabels[index] = CreateUiObject($"Farm Button Label {index}").AddComponent<Text>();
            }
            row.Configure(plotLabel, plotIcon, buttons, buttonLabels);

            var panel = CreateUiObject("Farm Panel");
            var feedbackText = CreateUiObject("Farm Feedback Text").AddComponent<Text>();
            var closeButton = CreateUiObject("Farm Close Button").AddComponent<Button>();
            var view = _root.AddComponent<CozyTownFarmDebugView>();
            view.ConfigureUi(panel, feedbackText, new[] { row }, closeButton, catalog);

            var plantedPlot = string.Empty;
            var plantedSeed = string.Empty;
            var wateredPlot = string.Empty;
            var harvestedPlot = string.Empty;
            view.PlantRequested += (plotId, seedId) =>
            {
                plantedPlot = plotId;
                plantedSeed = seedId;
            };
            view.WaterRequested += plotId => wateredPlot = plotId;
            view.HarvestRequested += plotId => harvestedPlot = plotId;

            view.Show(
                CreateFarmState(FarmPlotStatus.Empty, watered: false, seedQuantity: 0),
                "Choose a seed.");
            Assert.That(panel.activeSelf, Is.True);
            Assert.That(feedbackText.text, Is.EqualTo("Choose a seed."));
            Assert.That(buttons[0].gameObject.activeSelf, Is.True);
            Assert.That(buttons[0].interactable, Is.False);
            Assert.That(buttons[3].gameObject.activeSelf, Is.False);
            Assert.That(buttons[4].gameObject.activeSelf, Is.False);

            view.Show(
                CreateFarmState(FarmPlotStatus.Empty, watered: false, seedQuantity: 1),
                string.Empty);
            buttons[0].onClick.Invoke();
            Assert.That(plantedPlot, Is.EqualTo("plot.01"));
            Assert.That(plantedSeed, Is.EqualTo("seed.potato"));

            view.Show(
                CreateFarmState(FarmPlotStatus.Growing, watered: false, seedQuantity: 0),
                string.Empty);
            Assert.That(buttons[3].gameObject.activeSelf, Is.True);
            Assert.That(buttons[3].interactable, Is.True);
            buttons[3].onClick.Invoke();
            Assert.That(wateredPlot, Is.EqualTo("plot.01"));

            view.Show(
                CreateFarmState(FarmPlotStatus.Ready, watered: true, seedQuantity: 0),
                string.Empty);
            Assert.That(buttons[4].gameObject.activeSelf, Is.True);
            Assert.That(buttons[4].interactable, Is.True);
            buttons[4].onClick.Invoke();
            Assert.That(harvestedPlot, Is.EqualTo("plot.01"));
        }

        [Test]
        public void SecondaryProductionViews_RouteOnlyAvailableActionsThroughButtons()
        {
            EnsureItemIcon();
            var catalog = _root.AddComponent<CozyTownUiIconCatalog>();
            catalog.Configure(
                new[] { "feed.chicken", "animal_product.egg", "fish.carp", "food.baked_potato" },
                new[] { _itemIcon, _itemIcon, _itemIcon, _itemIcon },
                System.Array.Empty<string>(),
                System.Array.Empty<Sprite>());

            var bedPanel = CreateUiObject("Bed Panel");
            var bedFeedback = CreateUiObject("Bed Feedback").AddComponent<Text>();
            var bedClose = CreateUiObject("Bed Close").AddComponent<Button>();
            var sleepButton = CreateUiObject("Sleep Button").AddComponent<Button>();
            var bed = _root.AddComponent<CozyTownBedDebugView>();
            bed.ConfigureUi(bedPanel, bedFeedback, bedClose, sleepButton);
            var sleepCalls = 0;
            bed.SleepRequested += () => sleepCalls++;
            bed.Show(string.Empty);
            sleepButton.onClick.Invoke();
            Assert.That(sleepCalls, Is.EqualTo(1));

            var coopRow = CreateListRow("Coop Row", buttonCount: 2, out var coopButtons);
            var coopPanel = CreateUiObject("Coop Panel");
            var coopFeedback = CreateUiObject("Coop Feedback").AddComponent<Text>();
            var coopClose = CreateUiObject("Coop Close").AddComponent<Button>();
            var coop = _root.AddComponent<CozyTownCoopDebugView>();
            coop.ConfigureUi(coopPanel, coopFeedback, new[] { coopRow }, coopClose, catalog);
            var fedId = string.Empty;
            var collectedId = string.Empty;
            coop.FeedRequested += id => fedId = id;
            coop.CollectRequested += id => collectedId = id;
            coop.Show(CreateLivestockState(feed: 0, fed: false, ready: false), string.Empty);
            Assert.That(coopButtons[0].interactable, Is.False);
            Assert.That(coopButtons[1].interactable, Is.False);
            coop.Show(CreateLivestockState(feed: 1, fed: false, ready: false), string.Empty);
            coopButtons[0].onClick.Invoke();
            Assert.That(fedId, Is.EqualTo("animal.hen_01"));
            coop.Show(CreateLivestockState(feed: 0, fed: true, ready: true), string.Empty);
            coopButtons[1].onClick.Invoke();
            Assert.That(collectedId, Is.EqualTo("animal.hen_01"));

            var pondRow = CreateListRow("Pond Row", buttonCount: 0, out _);
            var pondPanel = CreateUiObject("Pond Panel");
            var pondFeedback = CreateUiObject("Pond Feedback").AddComponent<Text>();
            var pondClose = CreateUiObject("Pond Close").AddComponent<Button>();
            var castButton = CreateUiObject("Cast Button").AddComponent<Button>();
            var pond = _root.AddComponent<CozyTownPondDebugView>();
            pond.ConfigureUi(pondPanel, pondFeedback, new[] { pondRow }, pondClose, castButton, catalog);
            var castCalls = 0;
            pond.CatchRequested += () => castCalls++;
            pond.Show(
                new FishingViewState(
                    new[] { new FishingEntryView("fish_definition.carp", "fish.carp", "Carp", 0, 10, 2) }),
                string.Empty);
            castButton.onClick.Invoke();
            Assert.That(pondRow.Label.text, Is.EqualTo("Carp  Owned: 2"));
            Assert.That(castCalls, Is.EqualTo(1));

            var kitchenRow = CreateListRow("Kitchen Row", buttonCount: 1, out var kitchenButtons);
            var kitchenPanel = CreateUiObject("Kitchen Panel");
            var kitchenFeedback = CreateUiObject("Kitchen Feedback").AddComponent<Text>();
            var kitchenClose = CreateUiObject("Kitchen Close").AddComponent<Button>();
            var kitchen = _root.AddComponent<CozyTownKitchenDebugView>();
            kitchen.ConfigureUi(kitchenPanel, kitchenFeedback, new[] { kitchenRow }, kitchenClose, catalog);
            var cookedId = string.Empty;
            kitchen.CookRequested += id => cookedId = id;
            kitchen.Show(CreateCookingState(hasIngredients: false), string.Empty);
            Assert.That(kitchenButtons[0].interactable, Is.False);
            kitchen.Show(CreateCookingState(hasIngredients: true), string.Empty);
            kitchenButtons[0].onClick.Invoke();
            Assert.That(cookedId, Is.EqualTo("recipe.baked_potato"));
        }

        [Test]
        public void HudAndSaveViews_DriveConfiguredTextButtonsAndVisibility()
        {
            var hudPanel = CreateUiObject("HUD Panel");
            var clockText = CreateUiObject("Clock Text").AddComponent<Text>();
            var coinText = CreateUiObject("Coin Text").AddComponent<Text>();
            var hud = _root.AddComponent<CozyTownDebugHudView>();
            hud.ConfigureUi(hudPanel, clockText, coinText);

            hud.Render(new CozyTownHudState(day: 3, minuteOfDay: 8 * 60 + 5, balance: 250));

            Assert.That(hudPanel.activeSelf, Is.True);
            Assert.That(clockText.text, Is.EqualTo("Day 3  08:05"));
            Assert.That(coinText.text, Is.EqualTo("Coins: 250"));

            var savePanel = CreateUiObject("Save Panel");
            var feedbackText = CreateUiObject("Feedback Text").AddComponent<Text>();
            var saveButton = CreateUiObject("Save Button").AddComponent<Button>();
            var loadButton = CreateUiObject("Load Button").AddComponent<Button>();
            var save = _root.AddComponent<CozyTownSaveDebugView>();
            save.ConfigureUi(savePanel, feedbackText, saveButton, loadButton);
            var saveCalls = 0;
            var loadCalls = 0;
            save.SaveRequested += () => saveCalls++;
            save.LoadRequested += () => loadCalls++;

            save.Show(hasSave: false, feedback: "No save yet.");
            saveButton.onClick.Invoke();
            loadButton.onClick.Invoke();

            Assert.That(savePanel.activeSelf, Is.True);
            Assert.That(feedbackText.text, Is.EqualTo("No save yet."));
            Assert.That(loadButton.interactable, Is.False);
            Assert.That(saveCalls, Is.EqualTo(1));
            Assert.That(loadCalls, Is.Zero);

            save.Show(hasSave: true, feedback: "Ready.");
            loadButton.onClick.Invoke();
            Assert.That(loadButton.interactable, Is.True);
            Assert.That(loadCalls, Is.EqualTo(1));

            save.Hide();
            Assert.That(savePanel.activeSelf, Is.False);
        }

        private GameObject CreateUiObject(string name)
        {
            var value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(_root.transform, false);
            return value;
        }

        private void EnsureItemIcon()
        {
            if (_itemIcon != null)
            {
                return;
            }

            _iconTexture = new Texture2D(1, 1);
            _itemIcon = Sprite.Create(
                _iconTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));
        }

        private CozyTownUiListRow CreateListRow(
            string name,
            int buttonCount,
            out Button[] buttons)
        {
            var rowObject = CreateUiObject(name);
            var row = rowObject.AddComponent<CozyTownUiListRow>();
            var label = CreateUiObject(name + " Label").AddComponent<Text>();
            var icon = CreateUiObject(name + " Icon").AddComponent<Image>();
            buttons = new Button[buttonCount];
            var labels = new Text[buttonCount];
            for (var index = 0; index < buttonCount; index++)
            {
                buttons[index] = CreateUiObject($"{name} Button {index}").AddComponent<Button>();
                labels[index] = CreateUiObject($"{name} Button Label {index}").AddComponent<Text>();
            }

            row.Configure(label, icon, buttons, labels);
            return row;
        }

        private static LivestockViewState CreateLivestockState(int feed, bool fed, bool ready)
        {
            return new LivestockViewState(
                new[]
                {
                    new AnimalView(
                        "animal.hen_01",
                        "species.chicken",
                        "feed.chicken",
                        "Chicken Feed",
                        feed,
                        "animal_product.egg",
                        "Egg",
                        1,
                        fed,
                        ready)
                });
        }

        private static CookingViewState CreateCookingState(bool hasIngredients)
        {
            return new CookingViewState(
                new[]
                {
                    new RecipeView(
                        "recipe.baked_potato",
                        "food.baked_potato",
                        "Baked Potato",
                        1,
                        hasIngredients,
                        System.Array.Empty<RecipeIngredientView>())
                });
        }

        private static FarmViewState CreateFarmState(
            FarmPlotStatus status,
            bool watered,
            int seedQuantity)
        {
            return new FarmViewState(
                new[]
                {
                    new FarmPlotView(
                        "plot.01",
                        status == FarmPlotStatus.Empty ? string.Empty : "crop_definition.potato",
                        status == FarmPlotStatus.Empty ? string.Empty : "Potato",
                        status,
                        growthProgressDays: status == FarmPlotStatus.Ready ? 2 : 1,
                        growthDays: 2,
                        wateredToday: watered)
                },
                new[]
                {
                    new FarmSeedOption(
                        "crop_definition.potato",
                        "seed.potato",
                        "Potato Seed",
                        seedQuantity,
                        growthDays: 2,
                        harvestQuantity: 2)
                });
        }
    }
}
