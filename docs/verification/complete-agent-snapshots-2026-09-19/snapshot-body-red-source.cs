using System;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class BodySnapshotPlayModeTests
    {
        private GameObject _world;

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_world);

        [Test]
        public void CapturedJourney_RestoresRoadCursorAndContinuesFromSamePosition()
        {
            var map = CreateMap();
            var original = new TownRouteFollower2D(map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);
            original.SetDestination("destination");
            original.Advance(3f);

            var saved = original.CaptureSnapshot();
            original.Advance(100f);
            var restored = TownRouteFollower2D.RestoreSnapshot(map, saved, 0.3f, Vector2.zero, _world.transform);

            Assert.That(restored.Position, Is.EqualTo(new Vector2(2f, 1f)));
            Assert.That(restored.FacingDirection, Is.EqualTo(Vector2.up));
            Assert.That(restored.Status, Is.EqualTo(TownRouteStatus.Travelling));
            restored.Advance(1f);
            Assert.That(restored.Position, Is.EqualTo(new Vector2(2f, 2f)));
            Assert.That(restored.Status, Is.EqualTo(TownRouteStatus.Travelling));
            restored.Advance(1f);
            Assert.That(restored.Position, Is.EqualTo(new Vector2(2f, 3f)));
            Assert.That(restored.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(saved.Position.Y, Is.EqualTo(1f));
        }

        private TownMap2D CreateMap()
        {
            _world = new GameObject("Snapshot town");
            var map = _world.AddComponent<TownMap2D>();
            map.Configure(Array.Empty<TownHome>(), new[] {
                new TownLocation("start", Vector2.zero),
                new TownLocation("corner", new Vector2(2f, 0f)),
                new TownLocation("destination", new Vector2(2f, 3f)) },
                new[] { new TownRoad("start", "corner"), new TownRoad("corner", "destination") });
            return map;
        }
    }
}
