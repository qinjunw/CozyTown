using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Farming;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Kitchen;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class ProductionActionFeedbackEditModeTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Production Action Feedback Test Root");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void CoopView_WithPendingProductAndSpareFeed_DisablesFeedAndEnablesCollect()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.Inventory.Add(DefaultMvpIds.Items.ChickenFeed, 2).IsSuccess, Is.True);
            Assert.That(services.LivestockGameplay.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess, Is.True);
            Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);

            var row = CreateListRow("Coop Row", buttonCount: 2);
            var view = _root.AddComponent<CozyTownCoopDebugView>();
            view.ConfigureUi(
                CreateUiObject("Coop Panel"),
                CreateUiObject("Coop Feedback").AddComponent<Text>(),
                new[] { row },
                CreateUiObject("Coop Close").AddComponent<Button>(),
                _root.AddComponent<CozyTownUiIconCatalog>());

            view.Show(services.LivestockGameplay.GetCurrentState(), string.Empty);

            Assert.That(row.Buttons[0].interactable, Is.False,
                "A pending product must be collected before the animal can be fed again.");
            Assert.That(row.Buttons[1].interactable, Is.True);
        }

        [TestCase(FarmPlotStatus.Empty, false, 0, "No seeds. Buy seeds at the shop.")]
        [TestCase(FarmPlotStatus.Empty, false, 1, "Choose a seed.")]
        [TestCase(FarmPlotStatus.Growing, false, 0, "Needs water before 05:00.")]
        [TestCase(FarmPlotStatus.Growing, true, 0, "Watered. Growth at 05:00.")]
        [TestCase(FarmPlotStatus.Ready, false, 0, "Ready to harvest. No watering needed.")]
        public void FarmView_ShowsTheNextActionForEachProductionState(
            FarmPlotStatus status,
            bool watered,
            int ownedSeeds,
            string expectedGuidance)
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            int seedQuantity = status == FarmPlotStatus.Empty ? ownedSeeds : 1;
            if (seedQuantity > 0)
            {
                Assert.That(services.Inventory.Add(DefaultMvpIds.Items.PotatoSeed, seedQuantity).IsSuccess, Is.True);
            }
            if (status != FarmPlotStatus.Empty)
            {
                Assert.That(services.FarmGameplay.Plant("plot.01", DefaultMvpIds.Items.PotatoSeed).IsSuccess, Is.True);
            }
            if (watered || status == FarmPlotStatus.Ready)
            {
                Assert.That(services.FarmGameplay.Water("plot.01").IsSuccess, Is.True);
            }
            if (status == FarmPlotStatus.Ready)
            {
                Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);
                Assert.That(services.FarmGameplay.Water("plot.01").IsSuccess, Is.True);
                Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);
            }

            var row = CreateListRow("Farm Row", buttonCount: 5);
            var view = _root.AddComponent<CozyTownFarmDebugView>();
            view.ConfigureUi(
                CreateUiObject("Farm Panel"),
                CreateUiObject("Farm Feedback").AddComponent<Text>(),
                new[] { row },
                CreateUiObject("Farm Close").AddComponent<Button>(),
                _root.AddComponent<CozyTownUiIconCatalog>());

            view.Show(services.FarmGameplay.GetCurrentState(), string.Empty);

            Assert.That(row.Label.text, Does.Contain(expectedGuidance));
            if (status == FarmPlotStatus.Ready)
            {
                Assert.That(row.Label.text, Does.Not.Contain("Needs water"));
                Assert.That(row.Buttons[3].gameObject.activeSelf, Is.False);
                Assert.That(row.Buttons[4].interactable, Is.True);
            }
        }

        [TestCase(0, false, false, "No feed. Buy Chicken Feed at the shop.", "Expected: Egg x1", false, false)]
        [TestCase(1, false, false, "Feed to produce at 05:00.", "Expected: Egg x1", true, false)]
        [TestCase(0, true, false, "Fed. Production at 05:00.", "Expected: Egg x1", false, false)]
        [TestCase(1, false, true, "Collect before feeding again.", "Ready: Egg x1", false, true)]
        public void CoopView_DistinguishesExpectedAndPendingProductsWithActionGuidance(
            int ownedFeed,
            bool fed,
            bool ready,
            string expectedGuidance,
            string expectedProduct,
            bool canFeed,
            bool canCollect)
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            int feedQuantity = ownedFeed + (fed || ready ? 1 : 0);
            if (feedQuantity > 0)
            {
                Assert.That(services.Inventory.Add(DefaultMvpIds.Items.ChickenFeed, feedQuantity).IsSuccess, Is.True);
            }
            if (fed || ready)
            {
                Assert.That(services.LivestockGameplay.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess, Is.True);
            }
            if (ready)
            {
                Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);
            }

            var row = CreateListRow("Coop Row", buttonCount: 2);
            var view = _root.AddComponent<CozyTownCoopDebugView>();
            view.ConfigureUi(
                CreateUiObject("Coop Panel"),
                CreateUiObject("Coop Feedback").AddComponent<Text>(),
                new[] { row },
                CreateUiObject("Coop Close").AddComponent<Button>(),
                _root.AddComponent<CozyTownUiIconCatalog>());

            view.Show(services.LivestockGameplay.GetCurrentState(), string.Empty);

            Assert.That(row.Label.text, Does.Contain(expectedGuidance));
            Assert.That(row.Label.text, Does.Contain(expectedProduct));
            Assert.That(row.Buttons[0].interactable, Is.EqualTo(canFeed));
            Assert.That(row.Buttons[1].interactable, Is.EqualTo(canCollect));
        }

        [TestCase("Baked Potato", "Potato: 0 owned / 1 needed (missing 1)\nSalt: 0 owned / 1 needed (missing 1)")]
        [TestCase("Vegetable Soup", "Carrot: 0 owned / 1 needed (missing 1)\nTomato: 0 owned / 1 needed (missing 1)")]
        [TestCase("Grilled Fish", "Carp: 0 owned / 1 needed (missing 1)\nSalt: 0 owned / 1 needed (missing 1)")]
        [TestCase("Tomato and Egg", "Egg: 0 owned / 1 needed (missing 1)\nTomato: 0 owned / 1 needed (missing 1)")]
        [TestCase("Fish Pie", "Egg: 0 owned / 1 needed (missing 1)\nTrout: 0 owned / 1 needed (missing 1)\nFlour: 0 owned / 1 needed (missing 1)")]
        public void KitchenView_ShowsEveryDefaultRecipesIngredientRequirements(
            string outputName,
            string expectedIngredients)
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            var view = CreateKitchenView(out var rows);

            view.Show(services.CookingGameplay.GetCurrentState(), string.Empty);

            Assert.That(view.State.Recipes.Count, Is.EqualTo(5));
            var row = rows.Single(candidate => candidate.Label.text.StartsWith(outputName + " x1"));
            Assert.That(row.Label.text, Does.Contain(expectedIngredients));
            Assert.That(row.Buttons[0].interactable, Is.False);
        }

        [Test]
        public void KitchenView_AfterAddingIngredientsAndCooking_RefreshesOwnedAndMissingQuantities()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            var view = CreateKitchenView(out var rows);
            view.CookRequested += recipeId =>
            {
                Assert.That(services.CookingGameplay.Cook(recipeId).IsSuccess, Is.True);
                view.Show(services.CookingGameplay.GetCurrentState(), string.Empty);
            };

            Assert.That(services.Inventory.Add(DefaultMvpIds.Items.Potato, 2).IsSuccess, Is.True);
            view.Show(services.CookingGameplay.GetCurrentState(), string.Empty);
            var row = rows.Single(candidate => candidate.Label.text.StartsWith("Baked Potato x1"));
            Assert.That(row.Label.text, Does.Contain("Potato: 2 owned / 1 needed"));
            Assert.That(row.Label.text, Does.Contain("Salt: 0 owned / 1 needed (missing 1)"));
            Assert.That(row.Buttons[0].interactable, Is.False);

            Assert.That(services.Inventory.Add(DefaultMvpIds.Items.Salt, 1).IsSuccess, Is.True);
            view.Show(services.CookingGameplay.GetCurrentState(), string.Empty);
            Assert.That(row.Label.text, Does.Contain("Salt: 1 owned / 1 needed"));
            Assert.That(row.Label.text, Does.Not.Contain("missing"));
            Assert.That(row.Buttons[0].interactable, Is.True);
            row.Buttons[0].onClick.Invoke();

            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.BakedPotato), Is.EqualTo(1));
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.Potato), Is.EqualTo(1));
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.Salt), Is.Zero);
            Assert.That(row.Label.text, Does.Contain("Potato: 1 owned / 1 needed"));
            Assert.That(row.Label.text, Does.Contain("Salt: 0 owned / 1 needed (missing 1)"));
            Assert.That(row.Buttons[0].interactable, Is.False);
        }

        private CozyTownKitchenDebugView CreateKitchenView(out CozyTownUiListRow[] rows)
        {
            rows = new CozyTownUiListRow[5];
            for (var index = 0; index < rows.Length; index++)
            {
                rows[index] = CreateListRow($"Kitchen Row {index}", buttonCount: 1);
            }

            var view = _root.AddComponent<CozyTownKitchenDebugView>();
            view.ConfigureUi(
                CreateUiObject("Kitchen Panel"),
                CreateUiObject("Kitchen Feedback").AddComponent<Text>(),
                rows,
                CreateUiObject("Kitchen Close").AddComponent<Button>(),
                _root.AddComponent<CozyTownUiIconCatalog>());
            return view;
        }

        private GameObject CreateUiObject(string name)
        {
            var value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(_root.transform, false);
            return value;
        }

        private CozyTownUiListRow CreateListRow(string name, int buttonCount)
        {
            var row = CreateUiObject(name).AddComponent<CozyTownUiListRow>();
            var buttons = new Button[buttonCount];
            var labels = new Text[buttonCount];
            for (var index = 0; index < buttonCount; index++)
            {
                buttons[index] = CreateUiObject($"{name} Button {index}").AddComponent<Button>();
                labels[index] = CreateUiObject($"{name} Button Label {index}").AddComponent<Text>();
            }

            row.Configure(
                CreateUiObject(name + " Label").AddComponent<Text>(),
                CreateUiObject(name + " Icon").AddComponent<Image>(),
                buttons,
                labels);
            return row;
        }
    }
}
