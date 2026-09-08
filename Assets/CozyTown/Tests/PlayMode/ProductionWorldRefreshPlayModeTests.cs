#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Core;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Save;
using CozyTown.Unity.Shop;
using CozyTown.Unity.Time;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CozyTown.Tests.PlayMode
{
    public sealed class ProductionWorldRefreshPlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private Scene _scene;
        private GameObject _hud;
        private GameObject _world;
        private GameObject _player;
        private DaytimeClockDriver _clock;

        [UnityTest]
        public IEnumerator NaturalMorningSettlement_UpdatesProductionSpritesWithPanelsClosed()
        {
            yield return LoadScene();
            PrepareProduction();

            _clock.SetApplicationFocus(true);
            _clock.AdvanceFrame(0);
            _clock.AdvanceFrame(689.5);
            AssertProductionSprites("farm_plot_soil_watered", "crop_carrot_stage_00", "animal_hen_fed");

            _clock.AdvanceFrame(0.5);
            _clock.SetApplicationFocus(false);

            Assert.That(_hud.GetComponent<CozyTownFarmDebugView>().IsVisible, Is.False);
            Assert.That(_hud.GetComponent<CozyTownCoopDebugView>().IsVisible, Is.False);
            AssertProductionSprites("farm_plot_soil_dry", "crop_carrot_stage_01", "animal_hen_product_ready");
        }

        [UnityTest]
        public IEnumerator SameCycleLoad_RestoresProductionSpritesWithPanelsClosed()
        {
            Assert.That(Application.isBatchMode, Is.True, "Use the batch-mode in-memory save slot.");
            yield return LoadScene();
            var gear = _hud.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "Gear Button");
            var save = _hud.GetComponent<CozyTownSaveDebugView>();
            gear.onClick.Invoke();
            save.RequestSave();
            Assert.That(save.HasSave, Is.True);
            gear.onClick.Invoke();

            PrepareProduction();

            gear.onClick.Invoke();
            save.RequestLoad();
            Assert.That(save.Feedback, Is.EqualTo("Game loaded."));
            gear.onClick.Invoke();

            Assert.That(_hud.GetComponent<CozyTownFarmDebugView>().IsVisible, Is.False);
            Assert.That(_hud.GetComponent<CozyTownCoopDebugView>().IsVisible, Is.False);
            AssertProductionSprites("farm_plot_soil_dry", null, "animal_hen_idle");
        }

        [UnityTest]
        public IEnumerator DisabledPresenters_SleepAcrossMorningKeepsWorldSpritesCurrent()
        {
            yield return LoadScene();
            PrepareProduction();
            var farm = _hud.GetComponent<CozyTownFarmDebugPresenter>();
            var coop = _hud.GetComponent<CozyTownCoopDebugPresenter>();
            farm.enabled = false;
            coop.enabled = false;

            Open(TownInteractionKind.Bed);
            var bed = _hud.GetComponent<CozyTownBedDebugView>();
            bed.RequestSleep();
            bed.RequestSleep();
            bed.RequestSleep();
            bed.RequestClose();

            AssertProductionSprites("farm_plot_soil_dry", "crop_carrot_stage_01", "animal_hen_product_ready");
            farm.enabled = true;
            coop.enabled = true;
            Assert.That(_hud.GetComponent<CozyTownFarmDebugView>().IsVisible, Is.False);
            Assert.That(_hud.GetComponent<CozyTownCoopDebugView>().IsVisible, Is.False);
            AssertProductionSprites("farm_plot_soil_dry", "crop_carrot_stage_01", "animal_hen_product_ready");
        }

        [UnityTest]
        public IEnumerator FailedLoad_PreservesVisibleProductionState()
        {
            yield return LoadScene();
            var services = CozyTownCompositionRoot.Create(
                DefaultMvpContent.CreateConfiguration(), npcDialogue: null, saveStorage: new ReadFailingStorage());
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            PrepareProductionState(services);
            BindProduction(services);
            AssertProductionSprites("farm_plot_soil_watered", "crop_carrot_stage_00", "animal_hen_fed");

            var result = services.GameSave.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.read_failed"));
            AssertProductionSprites("farm_plot_soil_watered", "crop_carrot_stage_00", "animal_hen_fed");
        }

        [UnityTest]
        public IEnumerator LateRegistration_ShowsCurrentProductionAndFollowsTheSessionClock()
        {
            yield return LoadScene();
            var services = CozyTownCompositionRoot.CreateDefault();
            PrepareProductionState(services);
            var bootstrapObject = new GameObject("Late production bootstrap");
            bootstrapObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(bootstrapObject, _scene);
            var bootstrap = bootstrapObject.AddComponent<CozyTownBootstrap>();
            bootstrap.Initialize(services);
            bootstrapObject.SetActive(true);

            bootstrap.RegisterFarmPresenter(_hud.GetComponent<CozyTownFarmDebugPresenter>());
            bootstrap.RegisterCoopPresenter(_hud.GetComponent<CozyTownCoopDebugPresenter>());
            AssertProductionSprites("farm_plot_soil_watered", "crop_carrot_stage_00", "animal_hen_fed");

            SleepOneDay(services);

            AssertProductionSprites("farm_plot_soil_dry", "crop_carrot_stage_01", "animal_hen_product_ready");
        }

        [UnityTest]
        public IEnumerator ReboundPresenters_FollowCurrentSessionAndStopRefreshingAfterDestruction()
        {
            yield return LoadScene();
            var services = CozyTownCompositionRoot.CreateDefault();
            PrepareProductionState(services);
            BindProduction(services);
            BindProduction(services);
            SleepOneDay(services);
            AssertProductionSprites("farm_plot_soil_dry", "crop_carrot_stage_01", "animal_hen_product_ready");

            Open(TownInteractionKind.Farm);
            var farmView = _hud.GetComponent<CozyTownFarmDebugView>();
            farmView.RequestWater("plot.01");
            farmView.RequestClose();
            Open(TownInteractionKind.Coop);
            var coopView = _hud.GetComponent<CozyTownCoopDebugView>();
            coopView.RequestCollect(DefaultMvpIds.Livestock.Hen);
            coopView.RequestFeed(DefaultMvpIds.Livestock.Hen);
            coopView.RequestClose();
            AssertProductionSprites("farm_plot_soil_watered", "crop_carrot_stage_01", "animal_hen_fed");

            Object.Destroy(_hud.GetComponent<CozyTownFarmDebugPresenter>());
            Object.Destroy(_hud.GetComponent<CozyTownCoopDebugPresenter>());
            yield return null;
            SleepOneDay(services);

            AssertProductionSprites("farm_plot_soil_watered", "crop_carrot_stage_01", "animal_hen_fed");
        }

        private void BindProduction(CozyTownServices services)
        {
            _hud.GetComponent<CozyTownFarmDebugPresenter>().Bind(services.FarmGameplay, services.WorldTimeFlow);
            _hud.GetComponent<CozyTownCoopDebugPresenter>().Bind(services.LivestockGameplay, services.WorldTimeFlow);
        }

        private static void PrepareProductionState(CozyTownServices services)
        {
            Assert.That(services.Inventory.Add(DefaultMvpIds.Items.CarrotSeed, 1).IsSuccess, Is.True);
            Assert.That(services.Inventory.Add(DefaultMvpIds.Items.ChickenFeed, 2).IsSuccess, Is.True);
            Assert.That(services.FarmGameplay.Plant("plot.01", DefaultMvpIds.Items.CarrotSeed).IsSuccess, Is.True);
            Assert.That(services.FarmGameplay.Water("plot.01").IsSuccess, Is.True);
            Assert.That(services.LivestockGameplay.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess, Is.True);
        }

        private static void SleepOneDay(CozyTownServices services)
        {
            Assert.That(services.Sleep.SleepForMinutes(720).IsSuccess, Is.True);
            Assert.That(services.Sleep.SleepForMinutes(720).IsSuccess, Is.True);
        }

        private IEnumerator LoadScene()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
            _hud = Root("Debug HUD");
            _world = Root("World");
            _player = Root("Player");
            _clock = Root("CozyTown").GetComponent<DaytimeClockDriver>();
            _clock.SetApplicationFocus(false);
        }

        private void PrepareProduction()
        {
            Open(TownInteractionKind.Shop);
            var shop = _hud.GetComponent<CozyTownShopDebugView>();
            Assert.That(shop.State.PurchaseItems.Any(item => item.ItemId == DefaultMvpIds.Items.CarrotSeed), Is.True);
            shop.RequestBuy(DefaultMvpIds.Items.CarrotSeed);
            shop.RequestBuy(DefaultMvpIds.Items.ChickenFeed);
            shop.RequestClose();

            Open(TownInteractionKind.Farm);
            var farm = _hud.GetComponent<CozyTownFarmDebugView>();
            farm.RequestPlant("plot.01", DefaultMvpIds.Items.CarrotSeed);
            farm.RequestWater("plot.01");
            farm.RequestClose();

            Open(TownInteractionKind.Coop);
            var coop = _hud.GetComponent<CozyTownCoopDebugView>();
            coop.RequestFeed(DefaultMvpIds.Livestock.Hen);
            coop.RequestClose();

            AssertProductionSprites("farm_plot_soil_watered", "crop_carrot_stage_00", "animal_hen_fed");
        }

        private void AssertProductionSprites(string soil, string crop, string hen)
        {
            var farm = Point(TownInteractionKind.Farm).transform;
            Assert.That(farm.Find("Farm States/Plot 01/Soil").GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo(soil));
            Assert.That(farm.Find("Farm States/Plot 01/Crop").GetComponent<SpriteRenderer>().sprite?.name, Is.EqualTo(crop));
            Assert.That(_world.GetComponentsInChildren<SpriteRenderer>(true)
                .Single(renderer => renderer.name == "Hen State").sprite.name, Is.EqualTo(hen));
        }

        private GameObject Root(string name) => _scene.GetRootGameObjects().Single(root => root.name == name);

        private TownInteractionPoint2D Point(TownInteractionKind kind) =>
            _world.GetComponentsInChildren<TownInteractionPoint2D>(true).Single(point => point.Kind == kind);

        private void Open(TownInteractionKind kind) => Point(kind).Interact(new InteractionContext(_player));

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        private sealed class ReadFailingStorage : ISaveStorage
        {
            private readonly InMemorySaveStorage _storage = new InMemorySaveStorage();

            public bool Exists(string slotId) => _storage.Exists(slotId);

            public OperationResult Save(string slotId, GameSaveSnapshot snapshot) =>
                _storage.Save(slotId, snapshot);

            public OperationResult<GameSaveSnapshot> Load(string slotId) =>
                OperationResult<GameSaveSnapshot>.Failure("save.read_failed");
        }
    }
}
#endif
