using CozyTown.Runtime.Inventory;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Inventory;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class InventoryUiViewsEditModeTests
    {
        private GameObject _root;
        private Texture2D _texture;
        private Sprite _sprite;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Inventory UI Test Root");
            _texture = new Texture2D(1, 1);
            _sprite = Sprite.Create(
                _texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_sprite);
            Object.DestroyImmediate(_texture);
        }

        [Test]
        public void BackpackAndHotbarViews_RenderReadOnlySlotsAndSelection()
        {
            var catalog = _root.AddComponent<CozyTownUiIconCatalog>();
            catalog.Configure(
                new[] { "item.feed" },
                new[] { _sprite },
                System.Array.Empty<string>(),
                System.Array.Empty<Sprite>());
            var projection = new InventoryProjection(
                2,
                new[]
                {
                    new InventorySlotProjection("item.feed", "Feed", 2),
                    new InventorySlotProjection(string.Empty, string.Empty, 0)
                });

            var backpackPanel = CreateUiObject("Backpack Panel");
            var closeButton = CreateUiObject("Backpack Close").AddComponent<Button>();
            var backpackSlots = new[] { CreateSlot("Backpack 1"), CreateSlot("Backpack 2") };
            var backpack = _root.AddComponent<CozyTownBackpackView>();
            backpack.ConfigureUi(backpackPanel, backpackSlots, closeButton, catalog);

            var hotbarSlots = new CozyTownInventorySlotView[5];
            for (var index = 0; index < hotbarSlots.Length; index++)
            {
                hotbarSlots[index] = CreateSlot($"Hotbar {index + 1}");
            }
            var hotbar = _root.AddComponent<CozyTownHotbarView>();
            hotbar.ConfigureUi(hotbarSlots, catalog);

            backpack.Show(projection);
            hotbar.Render(projection, selectedIndex: 3);

            Assert.That(backpackPanel.activeSelf, Is.True);
            Assert.That(backpackSlots[0].ItemId, Is.EqualTo("item.feed"));
            Assert.That(backpackSlots[0].Icon.sprite, Is.SameAs(_sprite));
            Assert.That(backpackSlots[0].QuantityText.text, Is.EqualTo("2"));
            Assert.That(backpackSlots[1].Icon.enabled, Is.False);
            Assert.That(hotbarSlots[0].ItemId, Is.EqualTo("item.feed"));
            Assert.That(hotbarSlots[3].IsSelected, Is.True);
            Assert.That(hotbarSlots[4].IsSelected, Is.False);
        }

        [Test]
        public void Presenter_TogglesBackpackAndChangesHotbarSelectionWithoutWritingInventory()
        {
            var inventory = new InMemoryInventory(
                new[] { new ItemDefinition("item.feed", "Feed", ItemCategory.Material, 10) },
                capacitySlots: 5);
            Assert.That(inventory.Add("item.feed", 2).IsSuccess, Is.True);
            var catalog = _root.AddComponent<CozyTownUiIconCatalog>();
            catalog.Configure(
                new[] { "item.feed" },
                new[] { _sprite },
                System.Array.Empty<string>(),
                System.Array.Empty<Sprite>());
            var backpackSlots = new CozyTownInventorySlotView[5];
            var hotbarSlots = new CozyTownInventorySlotView[5];
            for (var index = 0; index < 5; index++)
            {
                backpackSlots[index] = CreateSlot($"Backpack Presenter {index + 1}");
                hotbarSlots[index] = CreateSlot($"Hotbar Presenter {index + 1}");
            }
            var backpackPanel = CreateUiObject("Backpack Presenter Panel");
            var backpack = _root.AddComponent<CozyTownBackpackView>();
            backpack.ConfigureUi(
                backpackPanel,
                backpackSlots,
                CreateUiObject("Backpack Presenter Close").AddComponent<Button>(),
                catalog);
            var hotbar = _root.AddComponent<CozyTownHotbarView>();
            hotbar.ConfigureUi(hotbarSlots, catalog);

            var player = CreateUiObject("Presenter Player");
            player.SetActive(false);
            player.AddComponent<Rigidbody2D>();
            var playerInput = player.AddComponent<InventoryPresenterPlayerInputSource>();
            var movement = player.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(playerInput);
            var probe = player.AddComponent<InteractionProbe2D>();
            var interactor = player.AddComponent<PlayerInteractor2D>();
            interactor.Configure(playerInput, probe);
            var gate = player.AddComponent<PlayerModalInputGate2D>();
            player.SetActive(true);

            var input = new TestInventoryUiInputSource();
            var presenter = _root.AddComponent<CozyTownInventoryPresenter>();
            presenter.Configure(input, gate, backpack, hotbar);
            presenter.Bind(inventory);

            input.HotbarSelectionPressedThisFrame = 4;
            presenter.ProcessInput();
            Assert.That(presenter.SelectedHotbarIndex, Is.EqualTo(4));
            Assert.That(hotbar.SelectedIndex, Is.EqualTo(4));
            Assert.That(inventory.Count("item.feed"), Is.EqualTo(2));

            input.HotbarSelectionPressedThisFrame = -1;
            input.BackpackTogglePressedThisFrame = true;
            presenter.ProcessInput();
            Assert.That(backpack.IsVisible, Is.True);
            Assert.That(gate.IsAcquired, Is.True);
            Assert.That(movement.enabled, Is.False);

            presenter.ProcessInput();
            Assert.That(backpack.IsVisible, Is.False);
            Assert.That(gate.IsAcquired, Is.False);
            Assert.That(movement.enabled, Is.True);
        }

        private CozyTownInventorySlotView CreateSlot(string name)
        {
            var slotObject = CreateUiObject(name);
            var icon = CreateUiObject(name + " Icon").AddComponent<Image>();
            var quantity = CreateUiObject(name + " Quantity").AddComponent<Text>();
            var selection = CreateUiObject(name + " Selection");
            var slot = slotObject.AddComponent<CozyTownInventorySlotView>();
            slot.ConfigureUi(icon, quantity, selection);
            return slot;
        }

        private GameObject CreateUiObject(string name)
        {
            var value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(_root.transform, false);
            return value;
        }
    }

    internal sealed class TestInventoryUiInputSource : IInventoryUiInputSource
    {
        public bool BackpackTogglePressedThisFrame { get; set; }

        public int HotbarSelectionPressedThisFrame { get; set; } = -1;
    }

    internal sealed class InventoryPresenterPlayerInputSource : MonoBehaviour, IPlayerInputSource
    {
        public Vector2 Movement => Vector2.zero;

        public bool InteractPressedThisFrame => false;
    }
}
