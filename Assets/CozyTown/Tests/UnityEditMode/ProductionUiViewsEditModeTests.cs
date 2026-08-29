using CozyTown.Unity.Hud;
using CozyTown.Unity.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class ProductionUiViewsEditModeTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Production UI Test Root");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
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
    }
}
