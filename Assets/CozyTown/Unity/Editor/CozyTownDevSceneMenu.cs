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
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Player;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Save;
using CozyTown.Unity.Shop;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownDevSceneMenu
    {
        private const string SceneFolder = "Assets/CozyTown/Scenes";
        private const string ScenePath = SceneFolder + "/CozyTown_Dev.unity";
        private const string InputActionsPath = "Assets/Settings/InputSystem_Actions.inputactions";
        private const string ArtRoot = "Assets/CozyTown/Art/Production";
        private const string TownTilesPath = ArtRoot + "/Environment/Tiles/tile_town_base_16.png";
        private const string BuildingsPath = ArtRoot + "/Buildings/bld_town_functions_64.png";
        private const string TownFunctionsPath = ArtRoot + "/Props/prop_town_functions_96x64.png";
        private const string FarmStatesPath = ArtRoot + "/Props/prop_farm_states_16.png";
        private const string HenStatesPath = ArtRoot + "/Props/prop_hen_states_16.png";
        private const string PlayerPath = ArtRoot + "/Characters/chr_player_move_16x24.png";
        private const string NpcTownsfolkPath =
            ArtRoot + "/Characters/npc_townsfolk_idle_down_16x24.png";
        private const string SceneTileFolder = "Assets/CozyTown/Art/Scene/Tiles";
        private const string UrpSpriteLitMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";

        private static readonly NpcWorldSpec[] NpcWorldSpecs =
        {
            new NpcWorldSpec(
                "NPC Mina",
                DefaultMvpIds.Npcs.Shopkeeper,
                "npc_shopkeeper_mina_idle_down",
                new Vector2(-4.2f, 0.35f)),
            new NpcWorldSpec(
                "NPC Eli",
                DefaultMvpIds.Npcs.Farmer,
                "npc_farmer_eli_idle_down",
                new Vector2(9.1f, -2f)),
            new NpcWorldSpec(
                "NPC Ren",
                DefaultMvpIds.Npcs.Fisher,
                "npc_fisher_ren_idle_down",
                new Vector2(-4.2f, -3f)),
            new NpcWorldSpec(
                "NPC Sora",
                DefaultMvpIds.Npcs.Cook,
                "npc_cook_sora_idle_down",
                new Vector2(3f, 0.35f))
        };

        [MenuItem("CozyTown/Create Development Scene")]
        public static void CreateDevelopmentScene()
        {
            var existingScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (existingScene != null || File.Exists(ScenePath))
            {
                EditorGUIUtility.PingObject(existingScene);
                Debug.Log($"Development scene already exists at {ScenePath}. No files were changed.");
                return;
            }

            EnsureFolder(SceneFolder);

            var previousScene = SceneManager.GetActiveScene();
            try
            {
                var scene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    GetSceneCreationMode(previousScene));
                SceneManager.SetActiveScene(scene);

                var bootstrap = CreateBootstrap();
                var playerInteractor = CreatePlayer();
                var points = CreateWorld();
                CreateHud(bootstrap, playerInteractor, points);
                CreateCamera();

                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
                Debug.Log(
                    $"Created development scene at {ScenePath} without modifying open scenes.");
            }
            finally
            {
                if (previousScene.IsValid())
                {
                    SceneManager.SetActiveScene(previousScene);
                }
            }
        }

        [MenuItem("CozyTown/Upgrade Development Scene for M4")]
        public static void UpgradeDevelopmentSceneForM4()
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
                var bootstrapObject = RequireRoot(scene, "CozyTown");
                var bootstrap = bootstrapObject.GetComponent<CozyTownBootstrap>();
                if (bootstrap == null)
                {
                    throw new InvalidOperationException("Development scene is missing CozyTownBootstrap.");
                }

                var hud = RequireRoot(scene, "Debug HUD");
                var world = RequireRoot(scene, "World");
                EnsureNpcWorldEntities(world, hud, bootstrap);

                var saveView = GetOrAdd<CozyTownSaveDebugView>(hud);
                var savePresenter = GetOrAdd<CozyTownSaveDebugPresenter>(hud);
                savePresenter.Configure(saveView);
                bootstrap.RegisterSavePresenter(savePresenter);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Upgraded development scene for M4 at {ScenePath}.");
            }
            finally
            {
                if (closeWhenFinished && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [MenuItem("CozyTown/Art/Upgrade Development Scene for A1 World Visuals")]
        public static void UpgradeDevelopmentSceneForA1WorldVisuals()
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
                var world = RequireRoot(scene, "World");
                var player = RequireRoot(scene, "Player");
                var camera = RequireRoot(scene, "Main Camera");
                var hud = RequireRoot(scene, "Debug HUD");
                var bootstrap = RequireRoot(scene, "CozyTown")
                    .GetComponent<CozyTownBootstrap>()
                    ?? throw new InvalidOperationException(
                        "Development scene is missing CozyTownBootstrap.");

                ConfigurePixelPerfectCamera(camera);
                ConfigureTownTilemap(world);
                ConfigureWorldBoundaries(world);
                EnsureNpcWorldEntities(world, hud, bootstrap);
                ConfigureWorldInteractionVisuals(world);
                CozyTownWorldCollisionSceneUpgrader.ConfigureWorld(world);
                ConfigureFarmWorldView(scene, world);
                ConfigureCoopWorldView(scene, world);
                var playerRenderer = ConfigureProductionRenderer(
                    player,
                    LoadSprite(PlayerPath, "chr_player_idle_down"),
                    sortingOrder: 20);
                ConfigurePlayerAnimation(player, playerRenderer);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Upgraded development scene with A1 world visuals at {ScenePath}.");
            }
            finally
            {
                if (closeWhenFinished && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static NewSceneMode GetSceneCreationMode(Scene previousScene)
        {
            if (!string.IsNullOrEmpty(previousScene.path))
            {
                return NewSceneMode.Additive;
            }

            if (Application.isBatchMode)
            {
                return NewSceneMode.Single;
            }

            throw new InvalidOperationException(
                "Save the active untitled scene before creating the development scene.");
        }

        private static CozyTownBootstrap CreateBootstrap()
        {
            var gameRoot = new GameObject("CozyTown");
            return gameRoot.AddComponent<CozyTownBootstrap>();
        }

        private static PlayerInteractor2D CreatePlayer()
        {
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions == null)
            {
                throw new FileNotFoundException("Default InputActionAsset was not found.", InputActionsPath);
            }

            var playerObject = new GameObject("Player");
            playerObject.transform.position = Vector3.zero;
            ConfigureRenderer(
                playerObject,
                new Vector2(0.7f, 0.7f),
                new Color(0.25f, 0.9f, 1f),
                sortingOrder: 10);

            var body = playerObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            playerObject.AddComponent<CircleCollider2D>().radius = 0.3f;

            var playerInput = playerObject.AddComponent<PlayerInput>();
            playerInput.actions = inputActions;
            playerInput.defaultActionMap = "Player";
            playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

            var inputSource = playerObject.AddComponent<InputSystemPlayerInputSource>();
            var movement = playerObject.AddComponent<PlayerMovement2D>();
            movement.SetInputSource(inputSource);

            var probe = playerObject.AddComponent<InteractionProbe2D>();
            var interactor = playerObject.AddComponent<PlayerInteractor2D>();
            interactor.Configure(inputSource, probe);
            playerObject.AddComponent<PlayerModalInputGate2D>();
            return interactor;
        }

        private static TownInteractionPoint2D[] CreateWorld()
        {
            var world = new GameObject("World");
            var boundaries = new GameObject("Boundaries");
            boundaries.transform.SetParent(world.transform, false);
            CreateBoundary(
                "North Boundary",
                new Vector2(0f, 3f),
                new Vector2(10.4f, 0.4f),
                boundaries.transform);
            CreateBoundary(
                "South Boundary",
                new Vector2(0f, -3f),
                new Vector2(10.4f, 0.4f),
                boundaries.transform);
            CreateBoundary(
                "West Boundary",
                new Vector2(-5f, 0f),
                new Vector2(0.4f, 6.4f),
                boundaries.transform);
            CreateBoundary(
                "East Boundary",
                new Vector2(5f, 0f),
                new Vector2(0.4f, 6.4f),
                boundaries.transform);

            var interactionPoints = new GameObject("Interaction Points");
            interactionPoints.transform.SetParent(world.transform, false);
            var shopPoint = CreateInteractionPoint(
                "Shop",
                TownInteractionKind.Shop,
                "Press E to browse the shop",
                new Vector2(-3f, 1.6f),
                new Color(1f, 0.65f, 0.2f),
                interactionPoints.transform);
            var minaPoint = CreateInteractionPoint(
                "NPC Mina",
                TownInteractionKind.Npc,
                "Press E to talk",
                new Vector2(3f, 1.6f),
                new Color(0.25f, 0.65f, 1f),
                interactionPoints.transform);
            var bedPoint = CreateInteractionPoint(
                "Bed",
                TownInteractionKind.Bed,
                "Press E to sleep until tomorrow",
                new Vector2(-3f, -1.6f),
                new Color(0.7f, 0.4f, 1f),
                interactionPoints.transform);
            var farmPoint = CreateInteractionPoint(
                "Farm",
                TownInteractionKind.Farm,
                "Press E to inspect the farm",
                new Vector2(3f, -1.6f),
                new Color(0.35f, 0.85f, 0.35f),
                interactionPoints.transform);
            var coopPoint = CreateInteractionPoint(
                "Coop", TownInteractionKind.Coop, "Press E to tend the coop",
                new Vector2(0f, 1.6f), new Color(.9f, .8f, .35f), interactionPoints.transform);
            var pondPoint = CreateInteractionPoint(
                "Pond", TownInteractionKind.Pond, "Press E to fish",
                new Vector2(0f, -1.6f), new Color(.2f, .75f, .9f), interactionPoints.transform);
            var kitchenPoint = CreateInteractionPoint(
                "Kitchen", TownInteractionKind.Kitchen, "Press E to cook",
                new Vector2(3f, 0f), new Color(.95f, .45f, .35f), interactionPoints.transform);
            var eliPoint = CreateInteractionPoint(
                "NPC Eli", TownInteractionKind.Npc, "Press E to talk",
                new Vector2(3.8f, 1.6f), new Color(.25f, .65f, 1f), interactionPoints.transform);
            var renPoint = CreateInteractionPoint(
                "NPC Ren", TownInteractionKind.Npc, "Press E to talk",
                new Vector2(3.8f, .8f), new Color(.25f, .65f, 1f), interactionPoints.transform);
            var soraPoint = CreateInteractionPoint(
                "NPC Sora", TownInteractionKind.Npc, "Press E to talk",
                new Vector2(3.8f, 0f), new Color(.25f, .65f, 1f), interactionPoints.transform);
            return new[]
            {
                shopPoint, minaPoint, bedPoint, farmPoint, coopPoint, pondPoint, kitchenPoint,
                eliPoint, renPoint, soraPoint
            };
        }

        private static void CreateHud(
            CozyTownBootstrap bootstrap,
            PlayerInteractor2D playerInteractor,
            TownInteractionPoint2D[] points)
        {
            var hudObject = new GameObject("Debug HUD");
            var view = hudObject.AddComponent<CozyTownDebugHudView>();
            var presenter = hudObject.AddComponent<CozyTownHudPresenter>();
            presenter.ConfigureView(view);
            bootstrap.RegisterHudPresenter(presenter);

            var interactionView = hudObject.AddComponent<CozyTownInteractionDebugView>();
            interactionView.Configure(playerInteractor);

            var shopView = hudObject.AddComponent<CozyTownShopDebugView>();
            var shopPresenter = hudObject.AddComponent<CozyTownShopDebugPresenter>();
            shopPresenter.Configure(FindUniquePoint(points, TownInteractionKind.Shop), shopView);
            bootstrap.RegisterShopPresenter(shopPresenter);

            var farmView = hudObject.AddComponent<CozyTownFarmDebugView>();
            var farmPresenter = hudObject.AddComponent<CozyTownFarmDebugPresenter>();
            farmPresenter.Configure(FindUniquePoint(points, TownInteractionKind.Farm), farmView);
            bootstrap.RegisterFarmPresenter(farmPresenter);
            var bedView = hudObject.AddComponent<CozyTownBedDebugView>();
            var bedPresenter = hudObject.AddComponent<CozyTownBedDebugPresenter>();
            bedPresenter.Configure(FindUniquePoint(points, TownInteractionKind.Bed), bedView);
            bootstrap.RegisterBedPresenter(bedPresenter);
            var coopView = hudObject.AddComponent<CozyTownCoopDebugView>();
            var coopPresenter = hudObject.AddComponent<CozyTownCoopDebugPresenter>();
            coopPresenter.Configure(FindUniquePoint(points, TownInteractionKind.Coop), coopView);
            bootstrap.RegisterCoopPresenter(coopPresenter);
            var pondView = hudObject.AddComponent<CozyTownPondDebugView>();
            var pondPresenter = hudObject.AddComponent<CozyTownPondDebugPresenter>();
            pondPresenter.Configure(FindUniquePoint(points, TownInteractionKind.Pond), pondView);
            bootstrap.RegisterPondPresenter(pondPresenter);
            var kitchenView = hudObject.AddComponent<CozyTownKitchenDebugView>();
            var kitchenPresenter = hudObject.AddComponent<CozyTownKitchenDebugPresenter>();
            kitchenPresenter.Configure(FindUniquePoint(points, TownInteractionKind.Kitchen), kitchenView);
            bootstrap.RegisterKitchenPresenter(kitchenPresenter);
            var npcView = hudObject.AddComponent<CozyTownNpcDebugView>();
            ConfigureNpcPresenters(points, npcView, bootstrap);
            var saveView = hudObject.AddComponent<CozyTownSaveDebugView>();
            var savePresenter = hudObject.AddComponent<CozyTownSaveDebugPresenter>();
            savePresenter.Configure(saveView);
            bootstrap.RegisterSavePresenter(savePresenter);
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3.7f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.11f, 0.12f);
        }

        private static void CreateBoundary(
            string name,
            Vector2 position,
            Vector2 size,
            Transform parent)
        {
            var boundary = new GameObject(name);
            boundary.transform.SetParent(parent, false);
            boundary.transform.position = position;
            boundary.AddComponent<BoxCollider2D>().size = size;
            ConfigureRenderer(
                boundary,
                size,
                new Color(0.18f, 0.23f, 0.25f),
                sortingOrder: 0);
        }

        private static TownInteractionPoint2D CreateInteractionPoint(
            string name,
            TownInteractionKind kind,
            string prompt,
            Vector2 position,
            Color color,
            Transform parent)
        {
            var pointObject = new GameObject(name);
            pointObject.transform.SetParent(parent, false);
            pointObject.transform.position = position;

            var collider = pointObject.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(1.2f, 1.2f);
            collider.isTrigger = true;

            ConfigureRenderer(
                pointObject,
                collider.size,
                color,
                sortingOrder: 1);
            var interactionPoint = pointObject.AddComponent<TownInteractionPoint2D>();
            interactionPoint.Configure(kind, prompt);
            return interactionPoint;
        }

        private static TownInteractionPoint2D FindUniquePoint(
            TownInteractionPoint2D[] points,
            TownInteractionKind kind)
        {
            TownInteractionPoint2D match = null;
            foreach (var point in points)
            {
                if (point.Kind != kind)
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"Development scene contains more than one {kind} interaction point.");
                }

                match = point;
            }

            return match ?? throw new InvalidOperationException(
                $"Development scene is missing its {kind} interaction point.");
        }

        private static void ConfigureNpcPresenters(
            TownInteractionPoint2D[] points,
            CozyTownNpcDebugView view,
            CozyTownBootstrap bootstrap)
        {
            var presenters = new CozyTownNpcDebugPresenter[NpcWorldSpecs.Length];
            for (var index = 0; index < NpcWorldSpecs.Length; index++)
            {
                var spec = NpcWorldSpecs[index];
                TownInteractionPoint2D point = Array.Find(
                    points,
                    candidate => string.Equals(candidate.name, spec.ObjectName, StringComparison.Ordinal));
                if (point == null || point.Kind != TownInteractionKind.Npc)
                {
                    throw new InvalidOperationException(
                        $"Development scene is missing NPC entity '{spec.ObjectName}'.");
                }

                var presenter = GetOrAdd<CozyTownNpcDebugPresenter>(point.gameObject);
                presenter.Configure(point, view, spec.NpcId);
                presenters[index] = presenter;
            }

            bootstrap.ConfigureNpcPresenters(presenters);
        }

        private static void EnsureNpcWorldEntities(
            GameObject world,
            GameObject hud,
            CozyTownBootstrap bootstrap)
        {
            var interactionPoints = world.transform.Find("Interaction Points")
                ?? throw new InvalidOperationException(
                    "Development scene is missing its Interaction Points object.");
            var existingNpcPoints = world.GetComponentsInChildren<TownInteractionPoint2D>(true);
            TownInteractionPoint2D legacyNpc = Array.Find(
                existingNpcPoints,
                point => point.Kind == TownInteractionKind.Npc
                    && Array.FindIndex(
                        NpcWorldSpecs,
                        spec => string.Equals(spec.ObjectName, point.name, StringComparison.Ordinal)) < 0);

            var resolved = new TownInteractionPoint2D[NpcWorldSpecs.Length];
            for (var index = 0; index < NpcWorldSpecs.Length; index++)
            {
                var spec = NpcWorldSpecs[index];
                var pointTransform = interactionPoints.Find(spec.ObjectName);
                TownInteractionPoint2D point = pointTransform?.GetComponent<TownInteractionPoint2D>();
                if (point == null && index == 0 && legacyNpc != null)
                {
                    point = legacyNpc;
                    point.name = spec.ObjectName;
                }

                if (point == null)
                {
                    point = CreateInteractionPoint(
                        spec.ObjectName,
                        TownInteractionKind.Npc,
                        "Press E to talk",
                        spec.Position,
                        new Color(.25f, .65f, 1f),
                        interactionPoints);
                }

                point.Configure(TownInteractionKind.Npc, "Press E to talk");
                point.transform.position = spec.Position;
                var trigger = point.GetComponent<BoxCollider2D>()
                    ?? point.gameObject.AddComponent<BoxCollider2D>();
                foreach (var collider in point.GetComponents<Collider2D>())
                {
                    if (!ReferenceEquals(collider, trigger))
                    {
                        UnityEngine.Object.DestroyImmediate(collider);
                    }
                }
                trigger.enabled = true;
                trigger.isTrigger = true;
                trigger.offset = new Vector2(0f, 0.3f);
                trigger.size = new Vector2(.8f, .8f);
                resolved[index] = point;
            }

            foreach (var legacyPresenter in hud.GetComponents<CozyTownNpcDebugPresenter>())
            {
                UnityEngine.Object.DestroyImmediate(legacyPresenter);
            }

            var view = GetOrAdd<CozyTownNpcDebugView>(hud);
            ConfigureNpcPresenters(resolved, view, bootstrap);
        }

        private static void ConfigureRenderer(
            GameObject target,
            Vector2 size,
            Color color,
            int sortingOrder)
        {
            var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (sprite == null)
            {
                throw new InvalidOperationException("Unity built-in UISprite could not be loaded.");
            }

            var visual = new GameObject("Visual");
            visual.transform.SetParent(target.transform, false);

            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;

            var spriteSize = sprite.bounds.size;
            visual.transform.localScale = new Vector3(
                size.x / spriteSize.x,
                size.y / spriteSize.y,
                1f);
        }

        private static void ConfigurePixelPerfectCamera(GameObject cameraObject)
        {
            var camera = cameraObject.GetComponent<Camera>()
                ?? throw new InvalidOperationException("Development scene is missing its Camera component.");
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.15f, 0.24f, 0.20f);

            var pixelPerfect = GetOrAdd<PixelPerfectCamera>(cameraObject);
            pixelPerfect.assetsPPU = 16;
            pixelPerfect.refResolutionX = 320;
            pixelPerfect.refResolutionY = 180;
            pixelPerfect.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
        }

        private static void ConfigureTownTilemap(GameObject world)
        {
            EnsureFolder(SceneTileFolder);
            var grassTiles = new[]
            {
                GetOrCreateTile("tile_grass_00"),
                GetOrCreateTile("tile_grass_01"),
                GetOrCreateTile("tile_grass_02"),
                GetOrCreateTile("tile_grass_03")
            };
            var pathHorizontal = GetOrCreateTile("tile_path_horizontal");
            var pathVertical = GetOrCreateTile("tile_path_vertical");
            var pathCross = GetOrCreateTile("tile_path_cross");

            var gridTransform = GetOrCreateChild(world.transform, "A1 Tile Grid");
            GetOrAdd<Grid>(gridTransform.gameObject);

            var tilemapTransform = GetOrCreateChild(gridTransform, "Ground Tilemap");
            var tilemap = GetOrAdd<Tilemap>(tilemapTransform.gameObject);
            var tilemapRenderer = GetOrAdd<TilemapRenderer>(tilemapTransform.gameObject);
            tilemapRenderer.sortingOrder = -20;
            tilemap.ClearAllTiles();

            for (var y = -6; y <= 6; y++)
            {
                for (var x = -10; x <= 10; x++)
                {
                    TileBase tile;
                    if (x == 0 && y == 0)
                    {
                        tile = pathCross;
                    }
                    else if (y == 0)
                    {
                        tile = pathHorizontal;
                    }
                    else if (x == 0)
                    {
                        tile = pathVertical;
                    }
                    else
                    {
                        var variant = Mathf.Abs((x * 3) + (y * 5)) % grassTiles.Length;
                        tile = grassTiles[variant];
                    }

                    tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }

            tilemap.CompressBounds();
        }

        private static void ConfigureWorldBoundaries(GameObject world)
        {
            var boundaries = world.transform.Find("Boundaries")
                ?? throw new InvalidOperationException("Development scene is missing its Boundaries object.");

            ConfigureBoundary(boundaries, "North Boundary", new Vector2(0f, 5.5f), new Vector2(20.4f, 0.4f));
            ConfigureBoundary(boundaries, "South Boundary", new Vector2(0f, -5.5f), new Vector2(20.4f, 0.4f));
            ConfigureBoundary(boundaries, "West Boundary", new Vector2(-10.2f, 0f), new Vector2(0.4f, 11.4f));
            ConfigureBoundary(boundaries, "East Boundary", new Vector2(10.2f, 0f), new Vector2(0.4f, 11.4f));
        }

        private static void ConfigureBoundary(
            Transform boundaries,
            string name,
            Vector2 position,
            Vector2 size)
        {
            var boundary = boundaries.Find(name)
                ?? throw new InvalidOperationException($"Development scene is missing '{name}'.");
            boundary.position = position;
            var collider = boundary.GetComponent<BoxCollider2D>()
                ?? throw new InvalidOperationException($"'{name}' is missing BoxCollider2D.");
            collider.size = size;

            var obsoleteVisual = boundary.Find("Visual");
            if (obsoleteVisual != null)
            {
                UnityEngine.Object.DestroyImmediate(obsoleteVisual.gameObject);
            }
        }

        private static void ConfigureWorldInteractionVisuals(GameObject world)
        {
            var points = world.GetComponentsInChildren<TownInteractionPoint2D>(true);
            if (points.Length != 10)
            {
                throw new InvalidOperationException(
                    $"Development scene must contain 10 interaction entities, but found {points.Length}.");
            }

            foreach (var point in points)
            {
                string assetPath;
                string spriteName;
                Vector2 position;
                switch (point.Kind)
                {
                    case TownInteractionKind.Shop:
                        assetPath = BuildingsPath;
                        spriteName = "bld_shop";
                        position = new Vector2(-7f, 1f);
                        break;
                    case TownInteractionKind.Npc:
                        var presenter = point.GetComponent<CozyTownNpcDebugPresenter>()
                            ?? throw new InvalidOperationException(
                                $"NPC entity '{point.name}' is missing its presenter.");
                        var npcSpec = FindNpcSpec(presenter.NpcId);
                        assetPath = NpcTownsfolkPath;
                        spriteName = npcSpec.SpriteName;
                        position = npcSpec.Position;
                        break;
                    case TownInteractionKind.Bed:
                        assetPath = BuildingsPath;
                        spriteName = "bld_home";
                        position = new Vector2(-7f, -4f);
                        break;
                    case TownInteractionKind.Farm:
                        assetPath = TownFunctionsPath;
                        spriteName = "prop_farm";
                        position = new Vector2(6f, -4f);
                        break;
                    case TownInteractionKind.Coop:
                        assetPath = BuildingsPath;
                        spriteName = "bld_coop";
                        position = new Vector2(0f, 1f);
                        break;
                    case TownInteractionKind.Pond:
                        assetPath = TownFunctionsPath;
                        spriteName = "prop_pond";
                        position = new Vector2(0f, -4f);
                        break;
                    case TownInteractionKind.Kitchen:
                        assetPath = BuildingsPath;
                        spriteName = "bld_kitchen";
                        position = new Vector2(6.5f, 1f);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(point.Kind), point.Kind, null);
                }

                point.transform.position = position;
                ConfigureProductionRenderer(
                    point.gameObject,
                    LoadSprite(assetPath, spriteName),
                    sortingOrder: point.Kind == TownInteractionKind.Npc ? 15 : 5);
            }
        }

        private static NpcWorldSpec FindNpcSpec(string npcId)
        {
            foreach (var spec in NpcWorldSpecs)
            {
                if (string.Equals(spec.NpcId, npcId, StringComparison.Ordinal))
                {
                    return spec;
                }
            }

            throw new InvalidOperationException($"Unknown NPC world identity '{npcId}'.");
        }

        private static SpriteRenderer ConfigureProductionRenderer(
            GameObject target,
            Sprite sprite,
            int sortingOrder)
        {
            var visual = target.transform.Find("Visual");
            if (visual == null)
            {
                var visualObject = new GameObject("Visual");
                visualObject.transform.SetParent(target.transform, false);
                visual = visualObject.transform;
            }

            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;

            var renderer = GetOrAdd<SpriteRenderer>(visual.gameObject);
            renderer.sprite = sprite;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(UrpSpriteLitMaterialPath)
                ?? throw new FileNotFoundException(
                    "URP 2D Sprite-Lit-Default material was not found.",
                    UrpSpriteLitMaterialPath);
            renderer.color = Color.white;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.sortingOrder = sortingOrder;
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            return renderer;
        }

        private static void ConfigurePlayerAnimation(GameObject player, SpriteRenderer renderer)
        {
            var movement = player.GetComponent<PlayerMovement2D>()
                ?? throw new InvalidOperationException("Player is missing PlayerMovement2D.");
            var body = player.GetComponent<Rigidbody2D>()
                ?? throw new InvalidOperationException("Player is missing Rigidbody2D.");
            var animator = GetOrAdd<CozyTownPlayerSpriteAnimator>(player);
            animator.Configure(
                renderer,
                movement,
                body,
                new[]
                {
                    LoadSprite(PlayerPath, "chr_player_idle_down"),
                    LoadSprite(PlayerPath, "chr_player_idle_left"),
                    LoadSprite(PlayerPath, "chr_player_idle_right"),
                    LoadSprite(PlayerPath, "chr_player_idle_up")
                },
                new[]
                {
                    LoadSprite(PlayerPath, "chr_player_walk_down_00"),
                    LoadSprite(PlayerPath, "chr_player_walk_down_01"),
                    LoadSprite(PlayerPath, "chr_player_walk_left_00"),
                    LoadSprite(PlayerPath, "chr_player_walk_left_01"),
                    LoadSprite(PlayerPath, "chr_player_walk_right_00"),
                    LoadSprite(PlayerPath, "chr_player_walk_right_01"),
                    LoadSprite(PlayerPath, "chr_player_walk_up_00"),
                    LoadSprite(PlayerPath, "chr_player_walk_up_01")
                });
        }

        private static void ConfigureFarmWorldView(Scene scene, GameObject world)
        {
            var farmPoint = Array.Find(
                world.GetComponentsInChildren<TownInteractionPoint2D>(true),
                point => point.Kind == TownInteractionKind.Farm)
                ?? throw new InvalidOperationException("Development scene is missing the Farm point.");

            var statesTransform = GetOrCreateChild(farmPoint.transform, "Farm States");
            var soilRenderers = new SpriteRenderer[6];
            var cropRenderers = new SpriteRenderer[6];
            var drySoil = LoadSprite(FarmStatesPath, "farm_plot_soil_dry");
            var wateredSoil = LoadSprite(FarmStatesPath, "farm_plot_soil_watered");
            var positions = new[]
            {
                new Vector2(-1.75f, 1.15f),
                new Vector2(0f, 1.15f),
                new Vector2(1.75f, 1.15f),
                new Vector2(-1.75f, 2.55f),
                new Vector2(0f, 2.55f),
                new Vector2(1.75f, 2.55f)
            };

            for (var index = 0; index < positions.Length; index++)
            {
                var plot = GetOrCreateChild(statesTransform, $"Plot {index + 1:00}");
                plot.localPosition = positions[index];

                var soil = GetOrCreateChild(plot, "Soil");
                soilRenderers[index] = GetOrAdd<SpriteRenderer>(soil.gameObject);
                soilRenderers[index].sprite = drySoil;
                soilRenderers[index].sortingOrder = 6;

                var crop = GetOrCreateChild(plot, "Crop");
                cropRenderers[index] = GetOrAdd<SpriteRenderer>(crop.gameObject);
                cropRenderers[index].sprite = null;
                cropRenderers[index].sortingOrder = 7;
            }

            var worldView = GetOrAdd<CozyTownFarmWorldView>(statesTransform.gameObject);
            worldView.Configure(
                soilRenderers,
                cropRenderers,
                drySoil,
                wateredSoil,
                LoadSprites(FarmStatesPath,
                    "crop_potato_stage_00", "crop_potato_stage_01", "crop_potato_stage_02"),
                LoadSprites(FarmStatesPath,
                    "crop_carrot_stage_00", "crop_carrot_stage_01",
                    "crop_carrot_stage_02", "crop_carrot_stage_03"),
                LoadSprites(FarmStatesPath,
                    "crop_tomato_stage_00", "crop_tomato_stage_01", "crop_tomato_stage_02",
                    "crop_tomato_stage_03", "crop_tomato_stage_04"));

            var farmPresenter = RequireRoot(scene, "Debug HUD")
                .GetComponent<CozyTownFarmDebugPresenter>()
                ?? throw new InvalidOperationException("Development scene is missing its Farm presenter.");
            farmPresenter.ConfigureWorldView(worldView);
        }

        private static void ConfigureCoopWorldView(Scene scene, GameObject world)
        {
            var coopPoint = Array.Find(
                world.GetComponentsInChildren<TownInteractionPoint2D>(true),
                point => point.Kind == TownInteractionKind.Coop)
                ?? throw new InvalidOperationException("Development scene is missing the Coop point.");

            var stateTransform = GetOrCreateChild(coopPoint.transform, "Hen State");
            stateTransform.localPosition = new Vector3(0.9f, 0.5f, 0f);
            var renderer = GetOrAdd<SpriteRenderer>(stateTransform.gameObject);
            var idleSprite = LoadSprite(HenStatesPath, "animal_hen_idle");
            renderer.sprite = idleSprite;
            renderer.sortingOrder = 7;

            var worldView = GetOrAdd<CozyTownCoopWorldView>(stateTransform.gameObject);
            worldView.Configure(
                renderer,
                idleSprite,
                LoadSprite(HenStatesPath, "animal_hen_fed"),
                LoadSprite(HenStatesPath, "animal_hen_product_ready"));

            var coopPresenter = RequireRoot(scene, "Debug HUD")
                .GetComponent<CozyTownCoopDebugPresenter>()
                ?? throw new InvalidOperationException("Development scene is missing its Coop presenter.");
            coopPresenter.ConfigureWorldView(worldView);
        }

        private static Tile GetOrCreateTile(string spriteName)
        {
            var assetPath = $"{SceneTileFolder}/{spriteName}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(assetPath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, assetPath);
            }

            tile.name = spriteName;
            tile.sprite = LoadSprite(TownTilesPath, spriteName);
            tile.color = Color.white;
            tile.transform = Matrix4x4.identity;
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            return tile;
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

        private static Sprite[] LoadSprites(string assetPath, params string[] spriteNames)
        {
            var sprites = new Sprite[spriteNames.Length];
            for (var index = 0; index < spriteNames.Length; index++)
            {
                sprites[index] = LoadSprite(assetPath, spriteNames[index]);
            }

            return sprites;
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            var root = Array.Find(
                scene.GetRootGameObjects(),
                candidate => candidate.name == name);
            return root != null
                ? root
                : throw new InvalidOperationException($"Root object '{name}' was not found.");
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static Transform GetOrCreateChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
            {
                child.localPosition = Vector3.zero;
                child.localRotation = Quaternion.identity;
                child.localScale = Vector3.one;
                return child;
            }

            var childObject = new GameObject(name);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }

        private readonly struct NpcWorldSpec
        {
            public NpcWorldSpec(
                string objectName,
                string npcId,
                string spriteName,
                Vector2 position)
            {
                ObjectName = objectName;
                NpcId = npcId;
                SpriteName = spriteName;
                Position = position;
            }

            public string ObjectName { get; }
            public string NpcId { get; }
            public string SpriteName { get; }
            public Vector2 Position { get; }
        }
    }
}
