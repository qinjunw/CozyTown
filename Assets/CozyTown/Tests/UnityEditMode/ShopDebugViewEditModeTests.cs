using CozyTown.Runtime.Application;
using CozyTown.Unity.Shop;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class ShopDebugViewEditModeTests
    {
        [Test]
        public void ShowAndRequests_ExposeStateAndFixedItemCommands()
        {
            var gameObject = new GameObject("Shop view");

            try
            {
                var view = gameObject.AddComponent<CozyTownShopDebugView>();
                var state = new ShopViewState(
                    300,
                    new[]
                    {
                        new ShopLineItem("seed.potato", "Potato Seed", 20, 0, 0)
                    });
                string buyItemId = null;
                string sellItemId = null;
                var closeCount = 0;
                view.BuyRequested += itemId => buyItemId = itemId;
                view.SellRequested += itemId => sellItemId = itemId;
                view.CloseRequested += () => closeCount++;

                view.Show(state, "Ready");
                view.RequestBuy("seed.potato");
                view.RequestSell("crop.potato");
                view.RequestClose();

                Assert.That(view.IsVisible, Is.True);
                Assert.That(view.State, Is.SameAs(state));
                Assert.That(view.Feedback, Is.EqualTo("Ready"));
                Assert.That(buyItemId, Is.EqualTo("seed.potato"));
                Assert.That(sellItemId, Is.EqualTo("crop.potato"));
                Assert.That(closeCount, Is.EqualTo(1));

                view.Hide();
                Assert.That(view.IsVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
