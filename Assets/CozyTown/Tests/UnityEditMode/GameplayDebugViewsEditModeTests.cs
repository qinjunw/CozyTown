using CozyTown.Runtime.Application;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Coop;
using CozyTown.Unity.Farm;
using CozyTown.Unity.Kitchen;
using CozyTown.Unity.Pond;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class GameplayDebugViewsEditModeTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("Views");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void Commands_AreRaisedOnlyWhileTheirViewIsVisible()
        {
            var farm = _root.AddComponent<CozyTownFarmDebugView>();
            var bed = _root.AddComponent<CozyTownBedDebugView>();
            var coop = _root.AddComponent<CozyTownCoopDebugView>();
            var pond = _root.AddComponent<CozyTownPondDebugView>();
            var kitchen = _root.AddComponent<CozyTownKitchenDebugView>();
            var calls = 0;
            farm.WaterRequested += _ => calls++;
            bed.SleepRequested += () => calls++;
            coop.FeedRequested += _ => calls++;
            pond.CatchRequested += () => calls++;
            kitchen.CookRequested += _ => calls++;

            farm.RequestWater("plot.01");
            bed.RequestSleep();
            coop.RequestFeed("animal.hen_01");
            pond.RequestCatch();
            kitchen.RequestCook("recipe.baked_potato");
            Assert.That(calls, Is.Zero);

            farm.Show(new FarmViewState(null, null), "");
            bed.Show("");
            coop.Show(new LivestockViewState(null), "");
            pond.Show(new FishingViewState(null), "");
            kitchen.Show(new CookingViewState(null), "");
            farm.RequestWater("plot.01");
            bed.RequestSleep();
            coop.RequestFeed("animal.hen_01");
            pond.RequestCatch();
            kitchen.RequestCook("recipe.baked_potato");
            Assert.That(calls, Is.EqualTo(5));
        }
    }
}
