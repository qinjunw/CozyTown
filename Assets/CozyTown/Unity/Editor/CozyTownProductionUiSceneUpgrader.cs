using System;
using System.IO;
using CozyTown.Runtime.Content;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Core;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Inventory;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Save;
using CozyTown.Unity.Shop;
using CozyTown.Unity.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownProductionUiSceneUpgrader
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private const string UiPath = "Assets/CozyTown/Art/Production/UI/ui_mvp_16.png";
        private const string SettingsIconPath = "Assets/CozyTown/Art/Production/UI/ui_icon_settings.png";
        private const string ItemPath = "Assets/CozyTown/Art/Production/Items/item_mvp_16.png";
        private const string PortraitPath = "Assets/CozyTown/Art/Production/Characters/npc_portraits_48.png";
        private const string InputActionsPath = "Assets/Settings/InputSystem_Actions.inputactions";
        private static readonly Color32 FarmButtonTint = new Color32(0x6F, 0x5A, 0x4A, 0xFF);
        private static readonly Color32 CreamText = new Color32(0xFF, 0xF4, 0xD6, 0xFF);

        [MenuItem("CozyTown/Art/Upgrade Development Scene for A1 Production UI")]
        public static void UpgradeDevelopmentSceneForA1ProductionUi()
        {
            if (!File.Exists(ScenePath))
            {
                throw new FileNotFoundException("Development scene was not found.", ScenePath);
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool closeWhenFinished = !scene.IsValid() || !scene.isLoaded;
            if (closeWhenFinished)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                var uiSprites = new UiSprites();
                var hud = RequireRoot(scene, "Debug HUD");
                var canvasTransform = ConfigureCanvas(hud.transform);
                ConfigureHud(canvasTransform, uiSprites);
                ConfigurePersistentUiShells(canvasTransform, uiSprites);
                ConfigureModalShells(canvasTransform, uiSprites);
                ConfigureInteractionPromptAnchors(scene);
                var iconCatalog = ConfigureIconCatalog(canvasTransform);
                ConfigurePersistentViewBindings(scene, hud, canvasTransform, iconCatalog);
                ConfigureShopViewBinding(hud, canvasTransform, uiSprites, iconCatalog);
                ConfigureFarmViewBinding(hud, canvasTransform, uiSprites, iconCatalog);
                ConfigureSecondaryProductionViewBindings(hud, canvasTransform, uiSprites, iconCatalog);
                ConfigureNpcProductionViewBinding(hud, canvasTransform, uiSprites, iconCatalog);
                ConfigureEventSystem(scene);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Upgraded development scene with A1 Production UI at {ScenePath}.");
            }
            finally
            {
                if (closeWhenFinished && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static RectTransform ConfigureCanvas(Transform hud)
        {
            var existing = hud.Find("Production UI");
            if (existing == null)
            {
                var rootObject = new GameObject(
                    "Production UI",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                rootObject.transform.SetParent(hud, false);
                existing = rootObject.transform;
            }

            var root = existing as RectTransform
                ?? throw new InvalidOperationException("Production UI must use RectTransform.");
            Stretch(root);

            var canvas = root.GetComponent<Canvas>() ?? root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 100;

            var scaler = root.GetComponent<CanvasScaler>() ?? root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(320f, 180f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 16f;
            if (root.GetComponent<GraphicRaycaster>() == null)
            {
                root.gameObject.AddComponent<GraphicRaycaster>();
            }
            return root;
        }

        private static void ConfigureHud(RectTransform canvas, UiSprites sprites)
        {
            var panel = CreatePanel(
                canvas,
                "HUD Panel",
                sprites.Panel,
                new Vector2(0f, 1f),
                new Vector2(4f, -4f),
                new Vector2(94f, 34f));
            CreateIcon(panel, "Clock Icon", sprites.Clock, new Vector2(4f, -3f), new Vector2(14f, 14f));
            CreateText(panel, "Clock Text", "Day 1  06:00", new Vector2(21f, -3f), new Vector2(69f, 14f));
            CreateIcon(panel, "Coin Icon", sprites.Coin, new Vector2(4f, -18f), new Vector2(14f, 14f));
            CreateText(panel, "Coin Text", "Coins: 300", new Vector2(21f, -18f), new Vector2(69f, 14f));
        }

        private static void ConfigurePersistentUiShells(RectTransform canvas, UiSprites sprites)
        {
            RemoveManagedNode(canvas, "Save Panel");
            RemoveManagedNode(canvas, "Interaction Panel");

            var gearButton = CreateStandaloneIconButton(
                canvas,
                "Gear Button",
                sprites.Settings,
                Vector2.zero,
                new Vector2(16f, 16f));
            var gearRect = (RectTransform)gearButton.transform;
            gearRect.anchorMin = Vector2.one;
            gearRect.anchorMax = Vector2.one;
            gearRect.pivot = Vector2.one;
            gearRect.anchoredPosition = new Vector2(-4f, -4f);

            var systemPanel = CreatePanel(
                canvas,
                "System Menu Panel",
                sprites.Panel,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(128f, 132f));

            var mainPage = GetOrCreateRect(systemPanel, "Main Page");
            Stretch(mainPage);
            var title = CreateText(mainPage, "Title Text", "系统设置", new Vector2(8f, -7f), new Vector2(112f, 14f), 10);
            title.alignment = TextAnchor.MiddleCenter;
            var saveContent = GetOrCreateRect(mainPage, "Save Content");
            Stretch(saveContent);
            var feedback = CreateText(saveContent, "Feedback Text", string.Empty, new Vector2(8f, -22f), new Vector2(112f, 12f), 7);
            feedback.alignment = TextAnchor.MiddleCenter;
            CreateButton(
                saveContent, "Save Button", "保存游戏", sprites.Save,
                new Vector2(8f, -36f), new Vector2(112f, 18f), sprites, 10, FarmButtonTint);
            CreateButton(
                saveContent, "Load Button", "加载存档", sprites.LoadIcon,
                new Vector2(8f, -58f), new Vector2(112f, 18f), sprites, 10, FarmButtonTint);
            CreateButton(
                mainPage, "Settings Button", "设置", sprites.Settings,
                new Vector2(8f, -80f), new Vector2(112f, 18f), sprites, 10, FarmButtonTint);
            CreateButton(
                mainPage, "Quit Button", "离开游戏", null,
                new Vector2(8f, -102f), new Vector2(112f, 18f), sprites, 10, FarmButtonTint);

            var settingsPage = GetOrCreateRect(systemPanel, "Settings Page");
            Stretch(settingsPage);
            var settingsTitle = CreateText(settingsPage, "Title Text", "设置", new Vector2(8f, -8f), new Vector2(112f, 16f), 10);
            settingsTitle.alignment = TextAnchor.MiddleCenter;
            var settingsMessage = CreateText(
                settingsPage,
                "Message Text",
                "设置项将在后续范围定义",
                new Vector2(12f, -38f),
                new Vector2(104f, 38f),
                8);
            settingsMessage.alignment = TextAnchor.MiddleCenter;
            CreateButton(settingsPage, "Back Button", "返回", null, new Vector2(24f, -96f), new Vector2(80f, 18f), sprites);

            var bubble = GetOrCreateRect(canvas, "Interaction Bubble");
            bubble.anchorMin = new Vector2(0.5f, 0.5f);
            bubble.anchorMax = new Vector2(0.5f, 0.5f);
            bubble.pivot = new Vector2(0.5f, 0.5f);
            bubble.sizeDelta = new Vector2(16f, 16f);
            bubble.anchoredPosition = Vector2.zero;
            var bubbleImage = GetOrAdd<Image>(bubble.gameObject);
            bubbleImage.sprite = sprites.Interact;
            bubbleImage.preserveAspect = true;
            bubbleImage.raycastTarget = false;
            var keyText = CreateText(bubble, "Key Text", "E", Vector2.zero, new Vector2(16f, 13f), 8);
            keyText.alignment = TextAnchor.MiddleCenter;

            ConfigureInventoryShells(canvas, sprites);

            systemPanel.gameObject.SetActive(false);
            bubble.gameObject.SetActive(false);
        }

        private static void ConfigureInventoryShells(RectTransform canvas, UiSprites sprites)
        {
            var hotbar = CreatePanel(
                canvas,
                "Hotbar Panel",
                sprites.Panel,
                new Vector2(0.5f, 0f),
                new Vector2(0f, 4f),
                new Vector2(112f, 24f));
            for (var index = 0; index < 5; index++)
            {
                var slot = CreateInventorySlot(
                    hotbar,
                    $"Hotbar Slot {index + 1}",
                    new Vector2(4f + index * 21f, -2f),
                    sprites);
                var keyLabel = CreateText(
                    (RectTransform)slot.transform,
                    "Key Label",
                    (index + 1).ToString(),
                    new Vector2(1f, -1f),
                    new Vector2(7f, 7f),
                    6);
                keyLabel.alignment = TextAnchor.UpperLeft;
            }

            var backpack = CreatePanel(
                canvas,
                "Backpack Panel",
                sprites.Panel,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(158f, 116f));
            CreateText(backpack, "Title Text", "包裹  B", new Vector2(8f, -6f), new Vector2(118f, 14f), 10);
            CreateButton(backpack, "Close Button", string.Empty, sprites.Close, new Vector2(132f, -4f), new Vector2(20f, 18f), sprites);
            for (var index = 0; index < 24; index++)
            {
                var column = index % 6;
                var row = index / 6;
                CreateInventorySlot(
                    backpack,
                    $"Backpack Slot {index + 1:00}",
                    new Vector2(14f + column * 22f, -24f - row * 22f),
                    sprites);
            }

            backpack.gameObject.SetActive(false);
        }

        private static CozyTownInventorySlotView CreateInventorySlot(
            RectTransform parent,
            string name,
            Vector2 anchoredPosition,
            UiSprites sprites)
        {
            var rect = GetOrCreateRect(parent, name);
            ConfigureTopLeft(rect, anchoredPosition, new Vector2(20f, 20f));
            var background = GetOrAdd<Image>(rect.gameObject);
            background.sprite = sprites.ButtonNormal;
            background.type = Image.Type.Sliced;
            background.raycastTarget = false;
            var icon = CreateIcon(rect, "Icon", null, new Vector2(2f, -2f), new Vector2(16f, 16f));
            var quantity = CreateText(rect, "Quantity Text", string.Empty, new Vector2(9f, -10f), new Vector2(9f, 8f), 6);
            quantity.alignment = TextAnchor.LowerRight;
            var selection = CreateIcon(
                rect,
                "Selection",
                sprites.Selection,
                new Vector2(2f, -2f),
                new Vector2(16f, 16f));
            selection.gameObject.SetActive(false);
            var slot = GetOrAdd<CozyTownInventorySlotView>(rect.gameObject);
            slot.ConfigureUi(icon, quantity, selection.gameObject);
            return slot;
        }

        private static void ConfigureModalShells(RectTransform canvas, UiSprites sprites)
        {
            CreateModal(canvas, "Shop Panel", "Town Shop", false, sprites);
            CreateModal(canvas, "Farm Panel", "Farm", false, sprites);
            CreateModal(canvas, "Bed Panel", "Bed", false, sprites);
            CreateModal(canvas, "Coop Panel", "Chicken Coop", false, sprites);
            CreateModal(canvas, "Pond Panel", "Fishing Pond", false, sprites);
            CreateModal(canvas, "Kitchen Panel", "Kitchen", false, sprites);
            CreateModal(canvas, "NPC Panel", "Town NPC Dialogue", true, sprites);
        }

        private static void ConfigurePersistentViewBindings(
            Scene scene,
            GameObject hud,
            RectTransform canvas,
            CozyTownUiIconCatalog iconCatalog)
        {
            var hudPanel = RequireChild(canvas, "HUD Panel");
            var hudView = hud.GetComponent<CozyTownDebugHudView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownDebugHudView.");
            hudView.ConfigureUi(
                hudPanel.gameObject,
                RequireChild(hudPanel, "Clock Text").GetComponent<Text>(),
                RequireChild(hudPanel, "Coin Text").GetComponent<Text>());

            var systemPanel = RequireChild(canvas, "System Menu Panel");
            var mainPage = RequireChild(systemPanel, "Main Page");
            var saveContent = RequireChild(mainPage, "Save Content");
            var saveView = hud.GetComponent<CozyTownSaveDebugView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownSaveDebugView.");
            saveView.ConfigureUi(
                saveContent.gameObject,
                RequireChild(saveContent, "Feedback Text").GetComponent<Text>(),
                RequireChild(saveContent, "Save Button").GetComponent<Button>(),
                RequireChild(saveContent, "Load Button").GetComponent<Button>());

            var systemView = GetOrAdd<CozyTownSystemMenuView>(hud);
            var settingsPage = RequireChild(systemPanel, "Settings Page");
            systemView.ConfigureUi(
                RequireChild(canvas, "Gear Button").GetComponent<Button>(),
                systemPanel.gameObject,
                mainPage.gameObject,
                settingsPage.gameObject,
                RequireChild(mainPage, "Settings Button").GetComponent<Button>(),
                RequireChild(settingsPage, "Back Button").GetComponent<Button>(),
                RequireChild(mainPage, "Quit Button").GetComponent<Button>());

            var player = RequireRoot(scene, "Player");
            var inputGate = player.GetComponent<PlayerModalInputGate2D>()
                ?? throw new InvalidOperationException("Player is missing PlayerModalInputGate2D.");
            var systemController = GetOrAdd<CozyTownSystemMenuController>(hud);
            systemController.Configure(inputGate, systemView);

            var interactor = player.GetComponent<PlayerInteractor2D>()
                ?? throw new InvalidOperationException("Player is missing PlayerInteractor2D.");
            var worldCamera = RequireRoot(scene, "Main Camera").GetComponent<Camera>()
                ?? throw new InvalidOperationException("Main Camera is missing Camera.");
            var bubble = RequireChild(canvas, "Interaction Bubble");
            var bubbleView = GetOrAdd<CozyTownInteractionBubbleView>(hud);
            bubbleView.Configure(interactor, worldCamera);
            bubbleView.ConfigureUi(
                bubble,
                RequireChild(bubble, "Key Text").GetComponent<Text>());

            var oldInteractionView = hud.GetComponent<CozyTownInteractionDebugView>();
            if (oldInteractionView != null)
            {
                UnityEngine.Object.DestroyImmediate(oldInteractionView);
            }

            var backpackPanel = RequireChild(canvas, "Backpack Panel");
            var backpackSlots = backpackPanel
                .GetComponentsInChildren<CozyTownInventorySlotView>(true);
            Array.Sort(
                backpackSlots,
                (left, right) => string.CompareOrdinal(left.name, right.name));
            var backpackView = GetOrAdd<CozyTownBackpackView>(hud);
            backpackView.ConfigureUi(
                backpackPanel.gameObject,
                backpackSlots,
                RequireChild(backpackPanel, "Close Button").GetComponent<Button>(),
                iconCatalog);

            var hotbarPanel = RequireChild(canvas, "Hotbar Panel");
            var hotbarSlots = hotbarPanel
                .GetComponentsInChildren<CozyTownInventorySlotView>(true);
            Array.Sort(
                hotbarSlots,
                (left, right) => string.CompareOrdinal(left.name, right.name));
            var hotbarView = GetOrAdd<CozyTownHotbarView>(hud);
            hotbarView.ConfigureUi(hotbarSlots, iconCatalog);

            var inputSource = player.GetComponent<InputSystemPlayerInputSource>();
            if (inputSource is not IInventoryUiInputSource inventoryInput)
            {
                throw new InvalidOperationException(
                    "Player input source must implement IInventoryUiInputSource.");
            }

            var inventoryPresenter = GetOrAdd<CozyTownInventoryPresenter>(hud);
            inventoryPresenter.Configure(inventoryInput, inputGate, backpackView, hotbarView);
            var bootstrap = RequireRoot(scene, "CozyTown").GetComponent<CozyTownBootstrap>()
                ?? throw new InvalidOperationException("CozyTown root is missing CozyTownBootstrap.");
            bootstrap.RegisterInventoryPresenter(inventoryPresenter);
        }

        private static void ConfigureInteractionPromptAnchors(Scene scene)
        {
            var world = RequireRoot(scene, "World");
            foreach (var point in world.GetComponentsInChildren<TownInteractionPoint2D>(true))
            {
                var anchor = point.transform.Find("Prompt Anchor");
                if (anchor == null)
                {
                    var anchorObject = new GameObject("Prompt Anchor");
                    anchorObject.transform.SetParent(point.transform, false);
                    anchor = anchorObject.transform;
                }

                var renderers = point.GetComponentsInChildren<SpriteRenderer>(true);
                var highestY = point.transform.position.y + 0.75f;
                foreach (var renderer in renderers)
                {
                    if (renderer.sprite != null)
                    {
                        highestY = Mathf.Max(highestY, renderer.bounds.max.y + 0.25f);
                    }
                }

                anchor.position = new Vector3(
                    point.transform.position.x,
                    highestY,
                    point.transform.position.z);
                anchor.localRotation = Quaternion.identity;
                anchor.localScale = Vector3.one;
                point.ConfigurePromptAnchor(anchor);
            }
        }

        private static CozyTownUiIconCatalog ConfigureIconCatalog(RectTransform canvas)
        {
            var catalog = GetOrAdd<CozyTownUiIconCatalog>(canvas.gameObject);
            catalog.Configure(
                new[]
                {
                    DefaultMvpIds.Items.PotatoSeed,
                    DefaultMvpIds.Items.CarrotSeed,
                    DefaultMvpIds.Items.TomatoSeed,
                    DefaultMvpIds.Items.Potato,
                    DefaultMvpIds.Items.Carrot,
                    DefaultMvpIds.Items.Tomato,
                    DefaultMvpIds.Items.ChickenFeed,
                    DefaultMvpIds.Items.Egg,
                    DefaultMvpIds.Items.Carp,
                    DefaultMvpIds.Items.Trout,
                    DefaultMvpIds.Items.Bass,
                    DefaultMvpIds.Items.Salt,
                    DefaultMvpIds.Items.Flour,
                    DefaultMvpIds.Items.BakedPotato,
                    DefaultMvpIds.Items.VegetableSoup,
                    DefaultMvpIds.Items.GrilledFish,
                    DefaultMvpIds.Items.TomatoEgg,
                    DefaultMvpIds.Items.FishPie
                },
                LoadSprites(
                    ItemPath,
                    "item_seed_potato",
                    "item_seed_carrot",
                    "item_seed_tomato",
                    "item_crop_potato",
                    "item_crop_carrot",
                    "item_crop_tomato",
                    "item_feed_chicken",
                    "item_animal_product_egg",
                    "item_fish_carp",
                    "item_fish_trout",
                    "item_fish_bass",
                    "item_ingredient_salt",
                    "item_ingredient_flour",
                    "item_food_baked_potato",
                    "item_food_vegetable_soup",
                    "item_food_grilled_fish",
                    "item_food_tomato_egg",
                    "item_food_fish_pie"),
                new[]
                {
                    DefaultMvpIds.Npcs.Shopkeeper,
                    DefaultMvpIds.Npcs.Farmer,
                    DefaultMvpIds.Npcs.Fisher,
                    DefaultMvpIds.Npcs.Cook
                },
                LoadSprites(
                    PortraitPath,
                    "npc_shopkeeper_mina_portrait",
                    "npc_farmer_eli_portrait",
                    "npc_fisher_ren_portrait",
                    "npc_cook_sora_portrait"));
            return catalog;
        }

        private static void ConfigureShopViewBinding(
            GameObject hud,
            RectTransform canvas,
            UiSprites sprites,
            CozyTownUiIconCatalog iconCatalog)
        {
            var panel = RequireChild(canvas, "Shop Panel");
            var rows = ConfigureListRows(panel, "Shop Rows", 18, 22f, sprites, CreateShopRow);
            var view = hud.GetComponent<CozyTownShopDebugView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownShopDebugView.");
            view.ConfigureUi(
                panel.gameObject,
                RequireChild(panel, "Title Text").GetComponent<Text>(),
                RequireChild(panel, "Feedback Text").GetComponent<Text>(),
                rows,
                RequireChild(panel, "Close Button").GetComponent<Button>(),
                iconCatalog);
        }

        private static void ConfigureFarmViewBinding(
            GameObject hud,
            RectTransform canvas,
            UiSprites sprites,
            CozyTownUiIconCatalog iconCatalog)
        {
            var panel = RequireChild(canvas, "Farm Panel");
            var rows = ConfigureListRows(panel, "Farm Rows", 6, 58f, sprites, CreateFarmRow);
            var view = hud.GetComponent<CozyTownFarmDebugView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownFarmDebugView.");
            view.ConfigureUi(
                panel.gameObject,
                RequireChild(panel, "Feedback Text").GetComponent<Text>(),
                rows,
                RequireChild(panel, "Close Button").GetComponent<Button>(),
                iconCatalog);
        }

        private static void ConfigureSecondaryProductionViewBindings(
            GameObject hud,
            RectTransform canvas,
            UiSprites sprites,
            CozyTownUiIconCatalog iconCatalog)
        {
            var bedPanel = RequireChild(canvas, "Bed Panel");
            var sleepButton = CreateButton(
                RequireChild(bedPanel, "Content"),
                "Sleep Button",
                "Sleep until tomorrow",
                null,
                Vector2.zero,
                new Vector2(272f, 22f),
                sprites);
            var bed = hud.GetComponent<CozyTownBedDebugView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownBedDebugView.");
            bed.ConfigureUi(
                bedPanel.gameObject,
                RequireChild(bedPanel, "Feedback Text").GetComponent<Text>(),
                RequireChild(bedPanel, "Close Button").GetComponent<Button>(),
                sleepButton);

            var coopPanel = RequireChild(canvas, "Coop Panel");
            var coopRows = ConfigureListRows(coopPanel, "Coop Rows", 1, 40f, sprites, CreateTwoButtonRow);
            var coop = hud.GetComponent<CozyTownCoopDebugView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownCoopDebugView.");
            coop.ConfigureUi(
                coopPanel.gameObject,
                RequireChild(coopPanel, "Feedback Text").GetComponent<Text>(),
                coopRows,
                RequireChild(coopPanel, "Close Button").GetComponent<Button>(),
                iconCatalog);

            var pondPanel = RequireChild(canvas, "Pond Panel");
            var pondRows = ConfigureListRows(pondPanel, "Pond Rows", 3, 22f, sprites, CreateReadOnlyRow);
            var castButton = CreateButton(
                RequireChild(pondPanel, "Content"),
                "Cast Button",
                "Cast",
                null,
                new Vector2(0f, -87f),
                new Vector2(272f, 20f),
                sprites);
            var pond = hud.GetComponent<CozyTownPondDebugView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownPondDebugView.");
            pond.ConfigureUi(
                pondPanel.gameObject,
                RequireChild(pondPanel, "Feedback Text").GetComponent<Text>(),
                pondRows,
                RequireChild(pondPanel, "Close Button").GetComponent<Button>(),
                castButton,
                iconCatalog);

            var kitchenPanel = RequireChild(canvas, "Kitchen Panel");
            var kitchenRows = ConfigureListRows(kitchenPanel, "Kitchen Rows", 5, 26f, sprites, CreateOneButtonRow);
            var kitchen = hud.GetComponent<CozyTownKitchenDebugView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownKitchenDebugView.");
            kitchen.ConfigureUi(
                kitchenPanel.gameObject,
                RequireChild(kitchenPanel, "Feedback Text").GetComponent<Text>(),
                kitchenRows,
                RequireChild(kitchenPanel, "Close Button").GetComponent<Button>(),
                iconCatalog);
        }

        private static void ConfigureNpcProductionViewBinding(
            GameObject hud,
            RectTransform canvas,
            UiSprites sprites,
            CozyTownUiIconCatalog iconCatalog)
        {
            var panel = RequireChild(canvas, "NPC Panel");
            var content = RequireChild(panel, "Content");
            var rowsRoot = GetOrCreateRect(content, "NPC Rows");
            ConfigureTopLeft(rowsRoot, Vector2.zero, new Vector2(108f, 104f));
            var rows = new CozyTownUiListRow[4];
            for (var index = 0; index < rows.Length; index++)
            {
                var rowRect = GetOrCreateRect(rowsRoot, $"Row {index + 1:00}");
                ConfigureTopLeft(rowRect, new Vector2(0f, -index * 26f), new Vector2(108f, 24f));
                var row = GetOrAdd<CozyTownUiListRow>(rowRect.gameObject);
                var icon = CreateIcon(rowRect, "Portrait", null, new Vector2(14f, -4f), new Vector2(16f, 16f));
                var label = CreateText(rowRect, "NPC Name", string.Empty, new Vector2(32f, -2f), new Vector2(44f, 20f), 8);
                var select = CreateButton(rowRect, "Talk Button", "Talk", null, new Vector2(78f, -2f), new Vector2(30f, 20f), sprites);
                row.Configure(
                    label,
                    icon,
                    new[] { select },
                    new[] { RequireChild(select.transform, "Label").GetComponent<Text>() });
                rows[index] = row;
            }

            var selection = RequireChild(content, "Selection Marker").GetComponent<Image>();
            selection.rectTransform.sizeDelta = new Vector2(12f, 12f);
            var portrait = CreateIcon(content, "Current Portrait", null, new Vector2(116f, 0f), new Vector2(48f, 48f));
            var dialogue = CreateText(content, "Dialogue Text", string.Empty, new Vector2(168f, 0f), new Vector2(104f, 48f), 8);
            var metadata = CreateText(content, "Metadata Text", string.Empty, new Vector2(116f, -52f), new Vector2(156f, 30f), 8);
            var talkAgain = CreateButton(content, "Talk Again Button", "Talk again", null, new Vector2(116f, -86f), new Vector2(156f, 20f), sprites);
            var view = hud.GetComponent<CozyTownNpcDebugView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownNpcDebugView.");
            view.ConfigureUi(
                panel.gameObject,
                RequireChild(panel, "Feedback Text").GetComponent<Text>(),
                rows,
                selection,
                portrait,
                dialogue,
                metadata,
                talkAgain,
                RequireChild(panel, "Close Button").GetComponent<Button>(),
                iconCatalog);
        }

        private static CozyTownUiListRow[] ConfigureListRows(
            RectTransform panel,
            string rowsName,
            int rowCount,
            float rowHeight,
            UiSprites sprites,
            Action<RectTransform, UiSprites, CozyTownUiListRow> configureRow)
        {
            var viewport = RequireChild(panel, "Content");
            GetOrAdd<RectMask2D>(viewport.gameObject);
            var content = GetOrCreateRect(viewport, rowsName);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, rowCount * rowHeight);

            var scroll = GetOrAdd<ScrollRect>(viewport.gameObject);
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 12f;

            var rows = new CozyTownUiListRow[rowCount];
            for (var index = 0; index < rowCount; index++)
            {
                var rowRect = GetOrCreateRect(content, $"Row {index + 1:00}");
                rowRect.anchorMin = new Vector2(0f, 1f);
                rowRect.anchorMax = new Vector2(1f, 1f);
                rowRect.pivot = new Vector2(0.5f, 1f);
                rowRect.anchoredPosition = new Vector2(0f, -index * rowHeight);
                rowRect.sizeDelta = new Vector2(0f, rowHeight - 2f);
                var row = GetOrAdd<CozyTownUiListRow>(rowRect.gameObject);
                configureRow(rowRect, sprites, row);
                rows[index] = row;
            }

            return rows;
        }

        private static void CreateShopRow(
            RectTransform rowRect,
            UiSprites sprites,
            CozyTownUiListRow row)
        {
            var icon = CreateIcon(rowRect, "Item Icon", null, new Vector2(0f, -2f));
            var label = CreateText(rowRect, "Item Label", string.Empty, new Vector2(20f, 0f), new Vector2(106f, 20f), 8);
            var buy = CreateButton(rowRect, "Buy Button", "Buy", null, new Vector2(128f, -1f), new Vector2(68f, 18f), sprites);
            var sell = CreateButton(rowRect, "Sell Button", "Sell", null, new Vector2(198f, -1f), new Vector2(68f, 18f), sprites);
            row.Configure(
                label,
                icon,
                new[] { buy, sell },
                new[]
                {
                    RequireChild(buy.transform, "Label").GetComponent<Text>(),
                    RequireChild(sell.transform, "Label").GetComponent<Text>()
                });
        }

        private static void CreateFarmRow(
            RectTransform rowRect,
            UiSprites sprites,
            CozyTownUiListRow row)
        {
            var icon = CreateIcon(rowRect, "Plot Icon", null, new Vector2(0f, -1f));
            var label = CreateText(rowRect, "Plot Label", string.Empty, new Vector2(20f, 0f), new Vector2(246f, 16f), 8);
            var buttons = new[]
            {
                CreateButton(rowRect, "Seed Button 1", "Seed 1", null, new Vector2(0f, -18f), new Vector2(86f, 18f), sprites),
                CreateButton(rowRect, "Seed Button 2", "Seed 2", null, new Vector2(90f, -18f), new Vector2(86f, 18f), sprites),
                CreateButton(rowRect, "Seed Button 3", "Seed 3", null, new Vector2(180f, -18f), new Vector2(86f, 18f), sprites),
                CreateButton(rowRect, "Water Button", "Water", null, new Vector2(0f, -38f), new Vector2(130f, 18f), sprites),
                CreateButton(rowRect, "Harvest Button", "Harvest", null, new Vector2(136f, -38f), new Vector2(130f, 18f), sprites)
            };
            var labels = new Text[buttons.Length];
            for (var index = 0; index < buttons.Length; index++)
            {
                labels[index] = RequireChild(buttons[index].transform, "Label").GetComponent<Text>();
            }

            row.Configure(label, icon, buttons, labels);
        }

        private static void CreateTwoButtonRow(
            RectTransform rowRect,
            UiSprites sprites,
            CozyTownUiListRow row)
        {
            var icon = CreateIcon(rowRect, "Item Icon", null, new Vector2(0f, -1f));
            var label = CreateText(rowRect, "Item Label", string.Empty, new Vector2(20f, 0f), new Vector2(246f, 16f), 8);
            var first = CreateButton(rowRect, "Action Button 1", "Action 1", null, new Vector2(0f, -18f), new Vector2(130f, 18f), sprites);
            var second = CreateButton(rowRect, "Action Button 2", "Action 2", null, new Vector2(136f, -18f), new Vector2(130f, 18f), sprites);
            row.Configure(
                label,
                icon,
                new[] { first, second },
                new[]
                {
                    RequireChild(first.transform, "Label").GetComponent<Text>(),
                    RequireChild(second.transform, "Label").GetComponent<Text>()
                });
        }

        private static void CreateOneButtonRow(
            RectTransform rowRect,
            UiSprites sprites,
            CozyTownUiListRow row)
        {
            var icon = CreateIcon(rowRect, "Item Icon", null, new Vector2(0f, -2f));
            var label = CreateText(rowRect, "Item Label", string.Empty, new Vector2(20f, 0f), new Vector2(166f, 22f), 8);
            var action = CreateButton(rowRect, "Action Button", "Action", null, new Vector2(190f, -2f), new Vector2(76f, 18f), sprites);
            row.Configure(
                label,
                icon,
                new[] { action },
                new[] { RequireChild(action.transform, "Label").GetComponent<Text>() });
        }

        private static void CreateReadOnlyRow(
            RectTransform rowRect,
            UiSprites sprites,
            CozyTownUiListRow row)
        {
            var icon = CreateIcon(rowRect, "Item Icon", null, new Vector2(0f, -2f));
            var label = CreateText(rowRect, "Item Label", string.Empty, new Vector2(20f, 0f), new Vector2(246f, 20f), 8);
            row.Configure(label, icon, Array.Empty<Button>(), Array.Empty<Text>());
        }

        private static void CreateModal(
            RectTransform canvas,
            string name,
            string title,
            bool hasSelectionMarker,
            UiSprites sprites)
        {
            var panel = CreatePanel(
                canvas,
                name,
                sprites.Panel,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(288f, 160f));
            CreateText(panel, "Title Text", title, new Vector2(8f, -6f), new Vector2(236f, 18f), 11);
            CreateText(panel, "Feedback Text", string.Empty, new Vector2(8f, -25f), new Vector2(272f, 14f));
            CreateButton(panel, "Close Button", string.Empty, sprites.Close, new Vector2(258f, -4f), new Vector2(24f, 20f), sprites);

            var content = GetOrCreateRect(panel, "Content");
            ConfigureTopLeft(content, new Vector2(8f, -41f), new Vector2(272f, 111f));
            if (hasSelectionMarker)
            {
                CreateIcon(content, "Selection Marker", sprites.Selection, new Vector2(0f, 0f));
            }

            panel.gameObject.SetActive(false);
        }

        private static void ConfigureEventSystem(Scene scene)
        {
            var eventSystemObject = FindRoot(scene, "EventSystem");
            if (eventSystemObject == null)
            {
                eventSystemObject = new GameObject("EventSystem");
                SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
            }

            GetOrAdd<EventSystem>(eventSystemObject);
            var inputModule = GetOrAdd<InputSystemUIInputModule>(eventSystemObject);
            AssignAction(inputModule, "Navigate", reference => inputModule.move = reference);
            AssignAction(inputModule, "Submit", reference => inputModule.submit = reference);
            AssignAction(inputModule, "Cancel", reference => inputModule.cancel = reference);
            AssignAction(inputModule, "Point", reference => inputModule.point = reference);
            AssignAction(inputModule, "Click", reference => inputModule.leftClick = reference);
            AssignAction(inputModule, "RightClick", reference => inputModule.rightClick = reference);
            AssignAction(inputModule, "MiddleClick", reference => inputModule.middleClick = reference);
            AssignAction(inputModule, "ScrollWheel", reference => inputModule.scrollWheel = reference);
        }

        private static void AssignAction(
            InputSystemUIInputModule module,
            string actionName,
            Action<InputActionReference> assign)
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath)
                ?? throw new FileNotFoundException("Input actions asset was not found.", InputActionsPath);
            module.actionsAsset = asset;
            foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(InputActionsPath))
            {
                if (candidate is InputActionReference reference
                    && reference.action != null
                    && reference.action.actionMap?.name == "UI"
                    && reference.action.name == actionName)
                {
                    assign(reference);
                    return;
                }
            }

            throw new InvalidOperationException($"UI action '{actionName}' was not found in {InputActionsPath}.");
        }

        private static RectTransform CreatePanel(
            RectTransform parent,
            string name,
            Sprite sprite,
            Vector2 anchor,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            var rect = GetOrCreateRect(parent, name);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, anchor.y);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var image = GetOrAdd<Image>(rect.gameObject);
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.raycastTarget = false;
            return rect;
        }

        private static Button CreateButton(
            RectTransform parent,
            string name,
            string label,
            Sprite icon,
            Vector2 anchoredPosition,
            Vector2 size,
            UiSprites sprites,
            int labelFontSize = 9,
            Color? backgroundTint = null)
        {
            var rect = GetOrCreateRect(parent, name);
            ConfigureTopLeft(rect, anchoredPosition, size);
            var image = GetOrAdd<Image>(rect.gameObject);
            image.sprite = sprites.ButtonNormal;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            image.color = backgroundTint ?? Color.white;

            var button = GetOrAdd<Button>(rect.gameObject);
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = sprites.ButtonHover,
                pressedSprite = sprites.ButtonPressed,
                selectedSprite = sprites.ButtonHover,
                disabledSprite = sprites.ButtonDisabled
            };

            CreateIcon(rect, "Icon", icon, new Vector2(3f, -2f), new Vector2(14f, 14f));
            float labelLeft = icon != null ? 19f : 3f;
            var labelText = CreateText(
                rect,
                "Label",
                label,
                new Vector2(labelLeft, -2f),
                new Vector2(size.x - labelLeft - 3f, size.y - 4f),
                labelFontSize);
            labelText.color = CreamText;
            return button;
        }

        private static Button CreateStandaloneIconButton(
            RectTransform parent,
            string name,
            Sprite icon,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            var rect = GetOrCreateRect(parent, name);
            ConfigureTopLeft(rect, anchoredPosition, size);
            RemoveManagedNode(rect, "Icon");
            RemoveManagedNode(rect, "Label");

            var image = GetOrAdd<Image>(rect.gameObject);
            image.sprite = icon;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = true;

            var button = GetOrAdd<Button>(rect.gameObject);
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.spriteState = default;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color32(230, 230, 230, 255),
                pressedColor = new Color32(180, 180, 180, 255),
                selectedColor = new Color32(230, 230, 230, 255),
                disabledColor = new Color32(111, 111, 111, 128),
                colorMultiplier = 1f,
                fadeDuration = 0.05f
            };
            return button;
        }

        private static Image CreateIcon(
            RectTransform parent,
            string name,
            Sprite sprite,
            Vector2 anchoredPosition,
            Vector2? size = null)
        {
            var rect = GetOrCreateRect(parent, name);
            ConfigureTopLeft(rect, anchoredPosition, size ?? new Vector2(16f, 16f));
            var image = GetOrAdd<Image>(rect.gameObject);
            image.sprite = sprite;
            image.enabled = sprite != null;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(
            RectTransform parent,
            string name,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize = 9)
        {
            var rect = GetOrCreateRect(parent, name);
            ConfigureTopLeft(rect, anchoredPosition, size);
            var text = GetOrAdd<Text>(rect.gameObject);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.resizeTextForBestFit = false;
            text.resizeTextMinSize = fontSize;
            text.resizeTextMaxSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.color = CreamText;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static void ConfigureTopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static RectTransform GetOrCreateRect(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child == null)
            {
                var childObject = new GameObject(name, typeof(RectTransform));
                childObject.transform.SetParent(parent, false);
                child = childObject.transform;
            }

            if (child is not RectTransform rect)
            {
                throw new InvalidOperationException($"UI node '{name}' must use RectTransform.");
            }

            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void RemoveManagedNode(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        private static RectTransform RequireChild(Transform parent, string name)
        {
            var child = parent.Find(name) as RectTransform;
            return child != null
                ? child
                : throw new InvalidOperationException($"UI node '{name}' was not found below '{parent.name}'.");
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            return FindRoot(scene, name)
                ?? throw new InvalidOperationException($"Root object '{name}' was not found.");
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            return Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == name);
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            return target.GetComponent<T>() ?? target.AddComponent<T>();
        }

        private static Sprite[] LoadSprites(string assetPath, params string[] spriteNames)
        {
            var sprites = new Sprite[spriteNames.Length];
            for (var index = 0; index < spriteNames.Length; index++)
            {
                sprites[index] = LoadSprite(assetPath, spriteNames[index]);
            }

            return sprites;
        }

        private static Sprite LoadSprite(string assetPath, string spriteName)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset is Sprite sprite && sprite.name == spriteName)
                {
                    return sprite;
                }
            }

            throw new FileNotFoundException(
                $"Sprite '{spriteName}' was not found in '{assetPath}'.",
                assetPath);
        }

        private sealed class UiSprites
        {
            public UiSprites()
            {
                Panel = Load("ui_panel");
                ButtonNormal = Load("ui_button_normal");
                ButtonHover = Load("ui_button_hover");
                ButtonPressed = Load("ui_button_pressed");
                ButtonDisabled = Load("ui_button_disabled");
                Coin = Load("ui_icon_coin");
                Clock = Load("ui_icon_clock");
                Save = Load("ui_icon_save");
                LoadIcon = Load("ui_icon_load");
                Close = Load("ui_icon_close");
                Selection = Load("ui_marker_selection");
                Interact = Load("ui_marker_interact");
                Settings = LoadSprite(SettingsIconPath, "ui_icon_settings");
            }

            public Sprite Panel { get; }
            public Sprite ButtonNormal { get; }
            public Sprite ButtonHover { get; }
            public Sprite ButtonPressed { get; }
            public Sprite ButtonDisabled { get; }
            public Sprite Coin { get; }
            public Sprite Clock { get; }
            public Sprite Save { get; }
            public Sprite LoadIcon { get; }
            public Sprite Close { get; }
            public Sprite Selection { get; }
            public Sprite Interact { get; }
            public Sprite Settings { get; }

            private static Sprite Load(string spriteName)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(UiPath))
                {
                    if (asset is Sprite sprite && sprite.name == spriteName)
                    {
                        return sprite;
                    }
                }

                throw new FileNotFoundException(
                    $"Sprite '{spriteName}' was not found in '{UiPath}'.",
                    UiPath);
            }
        }
    }
}
