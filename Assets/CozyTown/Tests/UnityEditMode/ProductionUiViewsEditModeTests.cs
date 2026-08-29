using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
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
