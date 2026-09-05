using System;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class TownNavigationTests
    {
        private GameObject _world;

        [Test]
        public void FindRoute_ChoosesShorterWalkingDistanceOverFewerRoadSegments()
        {
            _world = new GameObject("Town navigation fixture");
            var map = _world.AddComponent<TownMap2D>();
            map.Configure(Array.Empty<TownHome>(), new[]
            {
                new TownLocation("start", new Vector2(0f, 0f)),
                new TownLocation("detour", new Vector2(0f, 10f)),
                new TownLocation("short.one", new Vector2(1f, 0f)),
                new TownLocation("short.two", new Vector2(2f, 0f)),
                new TownLocation("destination", new Vector2(3f, 0f))
            }, new[]
            {
                new TownRoad("start", "detour"),
                new TownRoad("detour", "destination"),
                new TownRoad("start", "short.one"),
                new TownRoad("short.one", "short.two"),
                new TownRoad("short.two", "destination")
            });

            Assert.That(map.TryFindRoute("start", "destination", out var route), Is.True);
            CollectionAssert.AreEqual(new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(2f, 0f), new Vector2(3f, 0f)
            }, route);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null)
            {
                UnityEngine.Object.DestroyImmediate(_world);
            }
        }
    }
}
