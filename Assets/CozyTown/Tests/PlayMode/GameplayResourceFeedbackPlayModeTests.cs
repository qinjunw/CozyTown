using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Player;
using CozyTown.Unity.Pond;
using CozyTown.Unity.Shop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class GameplayResourceFeedbackPlayModeTests
    {
        private GameObject _root;
        private GameObject _actor;
        private TownInteractionPoint2D _point;
        private CozyTownServices _services;

        public enum ShopConstraint
        {
            PlayerFunds,
            ShopStock,
            BackpackSpace,
            OwnedItems,
            ShopFunds
        }

        [TestCase(ShopConstraint.PlayerFunds, "You need more coins to buy this item.")]
        [TestCase(ShopConstraint.ShopStock, "This item is sold out. Check the shop after the next morning restock.")]
        [TestCase(ShopConstraint.BackpackSpace, "Your backpack is full. Sell or use some items, then try again.")]
        [TestCase(ShopConstraint.OwnedItems, "You no longer have enough of this item to sell.")]
        [TestCase(ShopConstraint.ShopFunds, "The shop does not have enough coins to buy this item.")]
        public void TradeRejectedByResources_PublicRequestExplainsTheNextAction(
            ShopConstraint constraint,
            string expectedFeedback)
        {
            CreateFixture(TownInteractionKind.Shop);
            bool selling = constraint == ShopConstraint.OwnedItems || constraint == ShopConstraint.ShopFunds;
            string itemId = selling ? DefaultMvpIds.Items.Carp : DefaultMvpIds.Items.ChickenFeed;
            switch (constraint)
            {
                case ShopConstraint.PlayerFunds:
                    Assert.That(_services.Wallet.Debit(_services.Wallet.Balance).IsSuccess, Is.True);
                    break;
                case ShopConstraint.ShopStock:
                    var stock = _services.ShopTrading.GetCurrentState(
                        DefaultMvpIds.Shops.TownGeneral, DefaultMvpIds.Characters.Player).Value;
                    int quantity = stock.PurchaseItems.Single(item => item.ItemId == itemId).Quantity;
                    Assert.That(_services.ShopTrading.Buy(
                        DefaultMvpIds.Shops.TownGeneral, DefaultMvpIds.Characters.Player, itemId, quantity).IsSuccess, Is.True);
                    break;
                case ShopConstraint.BackpackSpace:
                    FillBackpack();
                    break;
                case ShopConstraint.ShopFunds:
                    Assert.That(_services.Inventory.Add(itemId, 401).IsSuccess, Is.True);
                    Assert.That(_services.ShopTrading.Sell(
                        DefaultMvpIds.Shops.TownGeneral, DefaultMvpIds.Characters.Player, itemId, 400).IsSuccess, Is.True);
                    break;
            }

            var view = _root.AddComponent<CozyTownShopDebugView>();
            var presenter = _root.AddComponent<CozyTownShopDebugPresenter>();
            presenter.Configure(_point, view);
            presenter.Bind(_services.ShopTrading, DefaultMvpIds.Shops.TownGeneral, DefaultMvpIds.Characters.Player);
            Open();
            Assert.That(view.IsVisible, Is.True);
            int balanceBefore = view.State.CharacterBalance;
            int itemsBefore = _services.Inventory.Count(itemId);

            if (selling) view.RequestSell(itemId);
            else view.RequestBuy(itemId);

            Assert.That(view.Feedback, Is.EqualTo(expectedFeedback));
            Assert.That(view.State.CharacterBalance, Is.EqualTo(balanceBefore));
            Assert.That(_services.Inventory.Count(itemId), Is.EqualTo(itemsBefore));
        }

        [Test]
        public void PlantWithoutSeeds_PublicRequestExplainsWhereToGetSeeds()
        {
            CreateFixture(TownInteractionKind.Farm);
            var view = _root.AddComponent<CozyTownFarmDebugView>();
            var presenter = _root.AddComponent<CozyTownFarmDebugPresenter>();
            presenter.Configure(_point, view);
            presenter.Bind(_services.FarmGameplay);
            Open();
            Assert.That(view.IsVisible, Is.True);

            view.RequestPlant(view.State.Plots[0].PlotId, DefaultMvpIds.Items.PotatoSeed);

            Assert.That(view.Feedback, Is.EqualTo("You need this seed in your backpack. Buy it at the shop first."));
        }

        [Test]
        public void BuyUnknownOffer_PublicRequestShowsReadableFallbackAndLogsTheDiagnostic()
        {
            CreateFixture(TownInteractionKind.Shop);
            var view = _root.AddComponent<CozyTownShopDebugView>();
            var presenter = _root.AddComponent<CozyTownShopDebugPresenter>();
            presenter.Configure(_point, view);
            presenter.Bind(_services.ShopTrading, DefaultMvpIds.Shops.TownGeneral, DefaultMvpIds.Characters.Player);
            Open();
            Assert.That(view.IsVisible, Is.True);
            int balanceBefore = view.State.CharacterBalance;
            LogAssert.Expect(LogType.Warning, "Buy failed: shop.offer_missing");

            view.RequestBuy("item.without_a_shop_offer");

            Assert.That(view.Feedback, Is.EqualTo("This action is unavailable right now. Close this panel and try again."));
            Assert.That(view.State.CharacterBalance, Is.EqualTo(balanceBefore));
        }

        [TestCase(false, "You need chicken feed in your backpack. Buy it at the shop first.")]
        [TestCase(true, "Collect the waiting product before feeding this animal again.")]
        public void FeedRejected_PublicRequestExplainsTheAnimalRequirement(bool productPending, string expectedFeedback)
        {
            CreateFixture(TownInteractionKind.Coop);
            if (productPending)
            {
                Assert.That(_services.Inventory.Add(DefaultMvpIds.Items.ChickenFeed, 2).IsSuccess, Is.True);
                Assert.That(_services.LivestockGameplay.Feed(DefaultMvpIds.Livestock.Hen).IsSuccess, Is.True);
                Assert.That(_services.Sleep.SleepForMinutes(12 * 60).IsSuccess, Is.True);
                Assert.That(_services.Sleep.SleepForMinutes(12 * 60).IsSuccess, Is.True);
            }
            var view = OpenCoop();
            Assert.That(view.State.Animals[0].ProductReady, Is.EqualTo(productPending));

            view.RequestFeed(DefaultMvpIds.Livestock.Hen);

            Assert.That(view.Feedback, Is.EqualTo(expectedFeedback));
        }

        [Test]
        public void CollectBeforeProduction_PublicRequestExplainsTheMorningRequirement()
        {
            CreateFixture(TownInteractionKind.Coop);
            var view = OpenCoop();

            view.RequestCollect(DefaultMvpIds.Livestock.Hen);

            Assert.That(view.Feedback, Is.EqualTo("No product is ready. Feed the animal and wait until the next morning."));
        }

        [Test]
        public void CatchWithFullBackpack_PublicRequestExplainsHowToMakeSpace()
        {
            CreateFixture(TownInteractionKind.Pond);
            FillBackpack();
            var view = _root.AddComponent<CozyTownPondDebugView>();
            var presenter = _root.AddComponent<CozyTownPondDebugPresenter>();
            presenter.Configure(_point, view);
            presenter.Bind(_services.FishingGameplay);
            presenter.SetRollSource(new CarpRollSource());
            Open();
            Assert.That(view.IsVisible, Is.True);

            view.RequestCatch();

            Assert.That(view.Feedback, Is.EqualTo("Your backpack is full. Sell or use some items, then try again."));
            Assert.That(view.State.Entries.Single(entry => entry.ItemId == DefaultMvpIds.Items.Carp).OwnedQuantity, Is.Zero);
        }

        [Test]
        public void CookWithoutIngredients_PublicRequestExplainsTheMissingResources()
        {
            CreateFixture(TownInteractionKind.Kitchen);
            var view = OpenKitchen();

            view.RequestCook(DefaultMvpIds.Recipes.BakedPotato);

            Assert.That(view.Feedback, Is.EqualTo("You do not have all the ingredients. Gather the missing ingredients and try again."));
        }

        [Test]
        public void CookRecipe_PublicRequestShowsTheFoodDisplayName()
        {
            CreateFixture(TownInteractionKind.Kitchen);
            Assert.That(_services.Inventory.Add(DefaultMvpIds.Items.Potato, 1).IsSuccess, Is.True);
            Assert.That(_services.Inventory.Add(DefaultMvpIds.Items.Salt, 1).IsSuccess, Is.True);
            var view = OpenKitchen();

            view.RequestCook(DefaultMvpIds.Recipes.BakedPotato);

            Assert.That(view.Feedback, Is.EqualTo("Cooked Baked Potato x1."));
            Assert.That(_services.Inventory.Count(DefaultMvpIds.Items.BakedPotato), Is.EqualTo(1));
        }

        private CozyTownCoopDebugView OpenCoop()
        {
            var view = _root.AddComponent<CozyTownCoopDebugView>();
            var presenter = _root.AddComponent<CozyTownCoopDebugPresenter>();
            presenter.Configure(_point, view);
            presenter.Bind(_services.LivestockGameplay);
            Open();
            Assert.That(view.IsVisible, Is.True);
            return view;
        }

        private CozyTownKitchenDebugView OpenKitchen()
        {
            var view = _root.AddComponent<CozyTownKitchenDebugView>();
            var presenter = _root.AddComponent<CozyTownKitchenDebugPresenter>();
            presenter.Configure(_point, view);
            presenter.Bind(_services.CookingGameplay);
            Open();
            Assert.That(view.IsVisible, Is.True);
            return view;
        }

        private void FillBackpack() => Assert.That(
            _services.Inventory.Add(DefaultMvpIds.Items.PotatoSeed, 24 * 99).IsSuccess, Is.True);

        private void CreateFixture(TownInteractionKind kind)
        {
            _services = CozyTownCompositionRoot.CreateDefault();
            _root = new GameObject("Gameplay Resource Feedback Test");
            _root.SetActive(false);
            _actor = new GameObject("Actor");
            _actor.transform.SetParent(_root.transform, false);
            _actor.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var input = _actor.AddComponent<PlayModePlayerInputSource>();
            _actor.AddComponent<PlayerMovement2D>().SetInputSource(input);
            var probe = _actor.AddComponent<InteractionProbe2D>();
            _actor.AddComponent<PlayerInteractor2D>().Configure(input, probe);
            _actor.AddComponent<PlayerModalInputGate2D>();
            _point = _root.AddComponent<TownInteractionPoint2D>();
            _point.Configure(kind, "Open");
        }

        private void Open()
        {
            _root.SetActive(true);
            _point.Interact(new InteractionContext(_actor));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private sealed class CarpRollSource : IFishingRollSource
        {
            public int NextRoll() => 0;
        }
    }
}
