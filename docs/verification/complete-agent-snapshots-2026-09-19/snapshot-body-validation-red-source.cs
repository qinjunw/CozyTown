using System;
using CozyTown.Runtime.Save;
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

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void InvalidSavedJourney_RejectsBeforeChangingCurrentFollower(int corruption)
        {
            var map = CreateMap();
            var original = new TownRouteFollower2D(map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);
            original.SetDestination("destination");
            original.Advance(3f);
            var saved = original.CaptureSnapshot();
            var points = saved.Waypoints;
            if (corruption == 1) points[1] = new Position2DSnapshot(3f, 0f);
            var invalid = new TownRouteSnapshot(
                corruption == 2 ? new Position2DSnapshot(1f, 2f) : saved.Position,
                saved.Facing, saved.TargetLocationId, points,
                corruption == 0 ? points.Length + 1 : saved.WaypointIndex, saved.Status, saved.HasReplanned);

            Assert.Throws<ArgumentException>(() => TownRouteFollower2D.RestoreSnapshot(
                map, invalid, 0.3f, Vector2.zero, _world.transform));
            Assert.That(original.Position, Is.EqualTo(new Vector2(2f, 1f)));
            original.Advance(1f);
            Assert.That(original.Position, Is.EqualTo(new Vector2(2f, 2f)));
        }

        [Test]
        public void BlockedSavedJourney_RemainsBlockedAfterObstacleRemovedUntilExplicitRetry()
        {
            var map = CreateMap();
            var original = new TownRouteFollower2D(map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);
            original.SetDestination("destination");
            var obstacle = new GameObject("Road obstacle");
            obstacle.transform.SetParent(_world.transform);
            obstacle.transform.position = new Vector2(2f, 1.5f);
            obstacle.AddComponent<BoxCollider2D>().size = new Vector2(1f, 0.5f);
            original.Advance(100f);
            Assert.That(original.Status, Is.EqualTo(TownRouteStatus.Blocked));
            var saved = original.CaptureSnapshot();
            Assert.That(saved.HasReplanned, Is.True);
            UnityEngine.Object.DestroyImmediate(obstacle);

            var restored = TownRouteFollower2D.RestoreSnapshot(map, saved, 0.3f, Vector2.zero, _world.transform);
            restored.SetDestination("destination");
            restored.Advance(100f);
            Assert.That(restored.Status, Is.EqualTo(TownRouteStatus.Blocked));
            Assert.That(restored.Position, Is.EqualTo(original.Position));
            Assert.That(restored.CaptureSnapshot().HasReplanned, Is.True);
            restored.Retry();
            restored.Advance(100f);
            Assert.That(restored.Status, Is.EqualTo(TownRouteStatus.Arrived));
        }

        [Test]
        public void NoLegalPositionSnapshot_PreservesFiniteOffRoadPositionAndRequiresBlockedState()
        {
            var map = CreateMap();
            var point = new Position2DSnapshot(50f, 50f);
            var facing = new Position2DSnapshot(0f, -1f);
            var saved = new TownRouteSnapshot(point, facing, "destination",
                Array.Empty<Position2DSnapshot>(), 1, (int)TownRouteStatus.Blocked, false);
            var restored = TownRouteFollower2D.RestoreSnapshot(map, saved,
                0.3f, Vector2.zero, _world.transform, noLegalPosition: true);
            restored.Advance(100f);
            Assert.That(restored.Position, Is.EqualTo(new Vector2(50f, 50f)));
            Assert.That(restored.Status, Is.EqualTo(TownRouteStatus.Blocked));

            var inconsistent = new TownRouteSnapshot(point, facing, "destination",
                new[] { point }, 1, (int)TownRouteStatus.Arrived, false);
            Assert.Throws<ArgumentException>(() => TownRouteFollower2D.RestoreSnapshot(map, inconsistent,
                0.3f, Vector2.zero, _world.transform, noLegalPosition: true));
        }
    }
}
