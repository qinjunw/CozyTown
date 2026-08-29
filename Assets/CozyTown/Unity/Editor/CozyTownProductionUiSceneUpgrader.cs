using System;
using System.IO;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Save;
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
        private const string InputActionsPath = "Assets/Settings/InputSystem_Actions.inputactions";

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
                ConfigureSavePanel(canvasTransform, uiSprites);
                ConfigureInteractionPanel(canvasTransform, uiSprites);
                ConfigureModalShells(canvasTransform, uiSprites);
                ConfigurePersistentViewBindings(hud, canvasTransform);
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
                new Vector2(108f, 42f));
            CreateIcon(panel, "Clock Icon", sprites.Clock, new Vector2(4f, -4f));
            CreateText(panel, "Clock Text", "Day 1  06:00", new Vector2(23f, -4f), new Vector2(80f, 16f));
            CreateIcon(panel, "Coin Icon", sprites.Coin, new Vector2(4f, -22f));
            CreateText(panel, "Coin Text", "Coins: 300", new Vector2(23f, -22f), new Vector2(80f, 16f));
        }

        private static void ConfigureSavePanel(RectTransform canvas, UiSprites sprites)
        {
            var panel = CreatePanel(
                canvas,
                "Save Panel",
                sprites.Panel,
                new Vector2(1f, 1f),
                new Vector2(-4f, -4f),
                new Vector2(92f, 62f));
            CreateText(panel, "Feedback Text", string.Empty, new Vector2(4f, -4f), new Vector2(84f, 14f), 8);
            CreateButton(panel, "Save Button", "Save", sprites.Save, new Vector2(4f, -20f), new Vector2(84f, 18f), sprites);
            CreateButton(panel, "Load Button", "Load", sprites.LoadIcon, new Vector2(4f, -40f), new Vector2(84f, 18f), sprites);
        }

        private static void ConfigureInteractionPanel(RectTransform canvas, UiSprites sprites)
        {
            var panel = CreatePanel(
                canvas,
                "Interaction Panel",
                sprites.Panel,
                new Vector2(0f, 0f),
                new Vector2(4f, 4f),
                new Vector2(220f, 34f));
            CreateIcon(panel, "Interact Icon", sprites.Interact, new Vector2(4f, -4f));
            CreateText(
                panel,
                "Prompt Text",
                "Move near a town location",
                new Vector2(23f, -4f),
                new Vector2(192f, 12f));
            CreateText(
                panel,
                "Feedback Text",
                string.Empty,
                new Vector2(23f, -17f),
                new Vector2(192f, 12f));
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

        private static void ConfigurePersistentViewBindings(GameObject hud, RectTransform canvas)
        {
            var hudPanel = RequireChild(canvas, "HUD Panel");
            var hudView = hud.GetComponent<CozyTownDebugHudView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownDebugHudView.");
            hudView.ConfigureUi(
                hudPanel.gameObject,
                RequireChild(hudPanel, "Clock Text").GetComponent<Text>(),
                RequireChild(hudPanel, "Coin Text").GetComponent<Text>());

            var savePanel = RequireChild(canvas, "Save Panel");
            var saveView = hud.GetComponent<CozyTownSaveDebugView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownSaveDebugView.");
            saveView.ConfigureUi(
                savePanel.gameObject,
                RequireChild(savePanel, "Feedback Text").GetComponent<Text>(),
                RequireChild(savePanel, "Save Button").GetComponent<Button>(),
                RequireChild(savePanel, "Load Button").GetComponent<Button>());

            var interactionPanel = RequireChild(canvas, "Interaction Panel");
            var interactionView = hud.GetComponent<CozyTownInteractionDebugView>()
                ?? throw new InvalidOperationException("Debug HUD is missing CozyTownInteractionDebugView.");
            interactionView.ConfigureUi(
                interactionPanel.gameObject,
                RequireChild(interactionPanel, "Prompt Text").GetComponent<Text>(),
                RequireChild(interactionPanel, "Feedback Text").GetComponent<Text>());
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

        private static void CreateButton(
            RectTransform parent,
            string name,
            string label,
            Sprite icon,
            Vector2 anchoredPosition,
            Vector2 size,
            UiSprites sprites)
        {
            var rect = GetOrCreateRect(parent, name);
            ConfigureTopLeft(rect, anchoredPosition, size);
            var image = GetOrAdd<Image>(rect.gameObject);
            image.sprite = sprites.ButtonNormal;
            image.type = Image.Type.Sliced;

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
            CreateText(rect, "Label", label, new Vector2(19f, -2f), new Vector2(size.x - 22f, size.y - 4f));
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
            text.fontStyle = FontStyle.Bold;
            text.color = new Color32(255, 244, 214, 255);
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
