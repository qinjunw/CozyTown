#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Unity.Content;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Shop;
using CozyTown.Unity.Time;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CozyTown.Tests.PlayMode
{
    public sealed class EconomyTextReadabilityPlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private Scene _scene;
        private InputTestFixture _input;
        private GameObject _hud;
        private readonly List<string> _failures = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _failures.Clear();
            _input = new InputTestFixture();
            _input.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        [UnityTest]
        public IEnumerator DevelopmentScene_EconomyTextFitsItsOwnRectInNormalAndFailureStates()
        {
            Assert.That(Application.isBatchMode, Is.True,
                "Batch mode keeps the scene bootstrap on an isolated in-memory save slot.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
            _hud = _scene.GetRootGameObjects().Single(root => root.name == "Debug HUD");
            _scene.GetRootGameObjects().Single(root => root.name == "CozyTown")
                .GetComponent<DaytimeClockDriver>().SetApplicationFocus(false);
            yield return null;

            var asset = AssetDatabase.LoadAssetAtPath<CozyTownMvpContentAsset>(
                "Assets/CozyTown/Content/DefaultMvpContent.asset");
            Assert.That(asset, Is.Not.Null);
            var content = asset.Load();
            Assert.That(content.IsSuccess, Is.True, content.ErrorCode);
            var services = CozyTownCompositionRoot.Create(content.Value);
            var farm = _hud.GetComponent<CozyTownFarmDebugView>();
            var coop = _hud.GetComponent<CozyTownCoopDebugView>();
            var kitchen = _hud.GetComponent<CozyTownKitchenDebugView>();
            var pond = _hud.GetComponent<CozyTownPondDebugView>();
            var shop = _hud.GetComponent<CozyTownShopDebugView>();

            yield return CheckPanel("Farm Panel",
                feedback => farm.Show(services.FarmGameplay.GetCurrentState(), feedback),
                CozyTownGameplayFeedback.Failure("Plant", "inventory.insufficient_quantity", farm),
                "No seeds. Buy seeds at the shop.");
            yield return CheckPanel("Coop Panel",
                feedback => coop.Show(services.LivestockGameplay.GetCurrentState(), feedback),
                CozyTownGameplayFeedback.Failure("Collect", "livestock.product_not_ready", coop),
                "Expected: Egg x1", "No feed.");
            yield return CheckPanel("Kitchen Panel",
                feedback => kitchen.Show(services.CookingGameplay.GetCurrentState(), feedback),
                CozyTownGameplayFeedback.Failure("Cook", "cooking.ingredients_missing", kitchen),
                "Baked Potato", "Vegetable Soup", "Grilled Fish", "Tomato and Egg", "Fish Pie",
                "Potato: 0 owned / 1 needed (missing 1)", "Flour: 0 owned / 1 needed (missing 1)");

            foreach (var item in content.Value.Items)
            {
                Assert.That(services.Inventory.Add(item.Id, 99).IsSuccess, Is.True, item.Id);
            }
            Assert.That(services.FarmGameplay.Plant("plot.01", DefaultMvpIds.Items.PotatoSeed).IsSuccess, Is.True);
            Assert.That(services.FarmGameplay.Plant("plot.02", DefaultMvpIds.Items.CarrotSeed).IsSuccess, Is.True);
            Assert.That(services.FarmGameplay.Plant("plot.03", DefaultMvpIds.Items.TomatoSeed).IsSuccess, Is.True);
            Assert.That(services.FarmGameplay.Water("plot.01").IsSuccess, Is.True);
            Assert.That(services.LivestockGameplay.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess, Is.True);
            Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);
            Assert.That(services.FarmGameplay.Water("plot.01").IsSuccess, Is.True);
            Assert.That(services.FarmGameplay.Water("plot.02").IsSuccess, Is.True);
            Assert.That(services.DayTransition.SleepToNextDay().IsSuccess, Is.True);
            Assert.That(services.FarmGameplay.Water("plot.02").IsSuccess, Is.True);

            yield return CheckPanel("Farm Panel",
                feedback => farm.Show(services.FarmGameplay.GetCurrentState(), feedback),
                CozyTownGameplayFeedback.Failure("Harvest", "inventory.capacity_exceeded", farm),
                "Ready to harvest. No watering needed.", "Watered. Growth at 05:00.",
                "Needs water before 05:00.", "Choose a seed.");
            yield return CheckPanel("Coop Panel",
                feedback => coop.Show(services.LivestockGameplay.GetCurrentState(), feedback),
                CozyTownGameplayFeedback.Failure("Feed", "livestock.product_pending", coop),
                "Ready: Egg x1", "Collect before feeding again.");
            yield return CheckPanel("Kitchen Panel",
                feedback => kitchen.Show(services.CookingGameplay.GetCurrentState(), feedback),
                CozyTownGameplayFeedback.Failure("Cook", "cooking.ingredients_missing", kitchen),
                "Potato: 99 owned / 1 needed", "Flour: 99 owned / 1 needed");
            yield return CheckPanel("Pond Panel",
                feedback => pond.Show(services.FishingGameplay.GetCurrentState(), feedback),
                CozyTownGameplayFeedback.Failure("Catch", "inventory.capacity_exceeded", pond),
                "Carp", "Trout", "Bass", "99");

            var shopState = services.ShopTrading.GetCurrentState(
                DefaultMvpIds.Shops.TownGeneral, DefaultMvpIds.Characters.Player);
            Assert.That(shopState.IsSuccess, Is.True, shopState.ErrorCode);
            yield return CheckPanel("Shop Panel",
                feedback => shop.Show(shopState.Value, feedback),
                CozyTownGameplayFeedback.Failure("Buy", "inventory.insufficient_quantity", shop),
                "Chicken Feed", "Buy", "Sell");
            yield return CheckPanel("Shop Panel", feedback =>
                {
                    shop.Show(shopState.Value, feedback);
                    _hud.GetComponentsInChildren<Button>()
                        .Single(button => button.name == "Sell Tab").onClick.Invoke();
                },
                CozyTownGameplayFeedback.Failure("Sell", "wallet.insufficient_funds", shop),
                "Tomato and Egg", "Vegetable Soup", "Sell");

            Assert.That(_failures, Is.Empty, string.Join("\n", _failures));
        }

        private IEnumerator CheckPanel(
            string panelName,
            Action<string> show,
            string failureFeedback,
            params string[] requiredContent)
        {
            HidePanels();
            foreach (string feedback in new[] { string.Empty, failureFeedback })
            {
                show(feedback);
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();

                var panel = _hud.transform.Find("Production UI/" + panelName);
                Assert.That(panel, Is.Not.Null, panelName);
                Assert.That(panel.gameObject.activeInHierarchy, Is.True, panelName);
                Text[] texts = panel.GetComponentsInChildren<Text>(false)
                    .Where(text => text.enabled && !string.IsNullOrWhiteSpace(text.text)).ToArray();
                Assert.That(texts.Length, Is.GreaterThan(0), panelName);
                string displayed = string.Join("\n", texts.Select(text => text.text));
                foreach (string required in requiredContent)
                {
                    if (!displayed.Contains(required))
                        _failures.Add($"{panelName}: required content is absent: {required}");
                }
                if (!string.IsNullOrEmpty(feedback) && !displayed.Contains(feedback))
                    _failures.Add($"{panelName}: the complete action feedback is absent: {feedback}");

                foreach (Text text in texts)
                {
                    Rect rect = text.rectTransform.rect;
                    string measurement = $"{HierarchyPath(text.transform)}: font={text.fontSize}, "
                        + $"rect={rect.width:0.##}x{rect.height:0.##}, preferredHeight={text.preferredHeight:0.##}, "
                        + $"text='{text.text.Replace("\n", " | ")}'";
                    if (rect.width <= 0f || rect.height <= 0f || text.preferredHeight > rect.height + 0.1f)
                        _failures.Add(measurement);
                    TestContext.Progress.WriteLine(measurement);
                }
            }

        }

        private void HidePanels()
        {
            _hud.GetComponent<CozyTownShopDebugView>().Hide();
            _hud.GetComponent<CozyTownFarmDebugView>().Hide();
            _hud.GetComponent<CozyTownCoopDebugView>().Hide();
            _hud.GetComponent<CozyTownKitchenDebugView>().Hide();
            _hud.GetComponent<CozyTownPondDebugView>().Hide();
        }

        private static string HierarchyPath(Transform target)
        {
            string path = target.name;
            while (target.parent != null)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_scene);
            _input?.TearDown();
        }
    }
}
#endif
