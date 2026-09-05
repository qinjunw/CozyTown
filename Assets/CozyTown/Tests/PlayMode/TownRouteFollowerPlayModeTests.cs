using System;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class TownRouteFollowerPlayModeTests
    {
        private GameObject _world;

        [Test]
        public void Advance_ConsumesDistanceAlongRoadAndStopsAtDestination()
        {
            var map = CreateCornerMap();
            var follower = new TownRouteFollower2D(
                map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);
            follower.SetDestination("destination");

            follower.Advance(3f);

            Assert.That(follower.Position, Is.EqualTo(new Vector2(2f, 1f)));
            Assert.That(follower.TargetLocationId, Is.EqualTo("destination"));
            Assert.That(follower.Status, Is.EqualTo(TownRouteStatus.Travelling));
            Assert.That(follower.FacingDirection, Is.EqualTo(Vector2.up));

            follower.Advance(100f);

            Assert.That(follower.Position, Is.EqualTo(new Vector2(2f, 3f)));
            Assert.That(follower.Status, Is.EqualTo(TownRouteStatus.Arrived));
        }

        [Test]
        public void ClonedJourney_ChangesTargetFromActualRoadPositionWithoutChangingOriginal()
        {
            var map = CreateCornerMap();
            var original = new TownRouteFollower2D(
                map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);
            original.SetDestination("destination");
            original.Advance(1f);
            var candidate = original.Clone();

            candidate.SetDestination("start");

            Assert.That(candidate.Position, Is.EqualTo(new Vector2(1f, 0f)));
            candidate.Advance(0.5f);
            Assert.That(candidate.Position, Is.EqualTo(new Vector2(0.5f, 0f)));
            Assert.That(candidate.Status, Is.EqualTo(TownRouteStatus.Travelling));
            Assert.That(original.Position, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(original.TargetLocationId, Is.EqualTo("destination"));

            original.Advance(0.5f);
            Assert.That(original.Position, Is.EqualTo(new Vector2(1.5f, 0f)));
            candidate.Advance(0.5f);
            Assert.That(candidate.Position, Is.EqualTo(Vector2.zero));
            Assert.That(candidate.Status, Is.EqualTo(TownRouteStatus.Arrived));
        }

        [Test]
        public void AddedWorldObstacle_StopsAtClearFootPositionUntilExplicitRetry()
        {
            var map = CreateCornerMap();
            var follower = new TownRouteFollower2D(
                map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);
            follower.SetDestination("destination");
            follower.Advance(1f);
            var obstacle = new GameObject("New road obstacle");
            obstacle.transform.SetParent(_world.transform);
            obstacle.transform.position = new Vector2(2f, 1.5f);
            var wall = obstacle.AddComponent<BoxCollider2D>();
            wall.size = new Vector2(1f, 0.5f);
            Physics2D.SyncTransforms();

            follower.Advance(5f);

            Assert.That(follower.Status, Is.EqualTo(TownRouteStatus.Blocked));
            Assert.That(follower.Position.x, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(follower.Position.y, Is.GreaterThan(0f));
            Assert.That(Vector2.Distance(follower.Position, wall.ClosestPoint(follower.Position)),
                Is.InRange(0.3f, 0.3f + 2f * Physics2D.defaultContactOffset));
            Assert.That(Physics2D.OverlapCircle(follower.Position, 0.3f), Is.Null);
            var stopped = follower.Position;
            UnityEngine.Object.DestroyImmediate(obstacle);
            follower.SetDestination("destination");
            follower.Advance(100f);
            Assert.That(follower.Position, Is.EqualTo(stopped));
            Assert.That(follower.Status, Is.EqualTo(TownRouteStatus.Blocked));

            follower.Retry();
            follower.Advance(100f);
            Assert.That(follower.Position, Is.EqualTo(new Vector2(2f, 3f)));
            Assert.That(follower.Status, Is.EqualTo(TownRouteStatus.Arrived));
        }

        [Test]
        public void Advance_PartitionedDistanceMatchesOneRequestAcrossRoadCorner()
        {
            var map = CreateCornerMap();
            var whole = new TownRouteFollower2D(
                map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);
            whole.SetDestination("destination");
            var partitioned = whole.Clone();

            whole.Advance(3f);
            for (var index = 0; index < 100; index++)
            {
                partitioned.Advance(0.03f);
            }

            Assert.That(Vector2.Distance(whole.Position, partitioned.Position), Is.LessThan(0.0001f));
            Assert.That(partitioned.Status, Is.EqualTo(whole.Status));
            Assert.That(partitioned.TargetLocationId, Is.EqualTo(whole.TargetLocationId));
            Assert.That(partitioned.FacingDirection, Is.EqualTo(whole.FacingDirection));
        }

        [TestCase(20f)]
        [TestCase(2f)]
        public void RoundedFinalStep_ReportsArrivalAndKeepsFiniteFacingOnLaterAdvance(float routeLength)
        {
            var map = CreateCornerMap();
            map.Configure(Array.Empty<TownHome>(), new[]
            {
                new TownLocation("start", Vector2.zero),
                new TownLocation("destination", new Vector2(routeLength, 0f))
            }, new[] { new TownRoad("start", "destination") });
            var whole = new TownRouteFollower2D(
                map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);
            whole.SetDestination("destination");
            var partitioned = whole.Clone();

            whole.Advance(routeLength);
            for (int step = 0; step < 60; step++) partitioned.Advance(routeLength / 60f);
            Vector2 positionAtBudget = partitioned.Position;
            var statusAtBudget = partitioned.Status;
            partitioned.Advance(0.1f);

            Assert.That(whole.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(whole.FacingDirection, Is.EqualTo(Vector2.right));
            foreach (float component in new[] { partitioned.Position.x, partitioned.Position.y,
                partitioned.FacingDirection.x, partitioned.FacingDirection.y })
                Assert.That(float.IsNaN(component) || float.IsInfinity(component), Is.False);
            Assert.That(positionAtBudget, Is.EqualTo(new Vector2(routeLength, 0f)));
            Assert.That(statusAtBudget, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(partitioned.Position, Is.EqualTo(whole.Position));
            Assert.That(partitioned.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(partitioned.FacingDirection, Is.EqualTo(Vector2.right));
        }

        [Test]
        public void Collision_UsesFootOffsetAndIgnoresTriggersAndMovingCharacterBodies()
        {
            var map = CreateCornerMap();
            map.Configure(Array.Empty<TownHome>(), new[]
            {
                new TownLocation("start", Vector2.zero),
                new TownLocation("destination", new Vector2(4f, 0f))
            }, new[] { new TownRoad("start", "destination") });
            var follower = new TownRouteFollower2D(
                map, Vector2.zero, 0.3f, new Vector2(0f, 0.5f), _world.transform);
            follower.SetDestination("destination");
            AddBox("Interaction trigger", new Vector2(0.75f, 0.5f)).isTrigger = true;
            AddBox("Kinematic character", new Vector2(1.25f, 0.5f))
                .gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            AddBox("Dynamic character", new Vector2(1.75f, 0.5f))
                .gameObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var wall = AddBox("World wall", new Vector2(3f, 0.5f));

            follower.Advance(100f);

            Assert.That(follower.Status, Is.EqualTo(TownRouteStatus.Blocked));
            Assert.That(follower.Position.x, Is.GreaterThan(2f));
            Assert.That(follower.Position.y, Is.EqualTo(0f));
            var footPosition = follower.Position + new Vector2(0f, 0.5f);
            Assert.That(Vector2.Distance(footPosition, wall.ClosestPoint(footPosition)),
                Is.InRange(0.3f, 0.3f + 2f * Physics2D.defaultContactOffset));
            Assert.That(Physics2D.OverlapCircle(footPosition, 0.3f), Is.Null);
        }

        [Test]
        public void AddedObstacle_ReplansFromActualPositionUsingAnotherConnectedRoad()
        {
            var map = CreateCornerMap();
            map.Configure(Array.Empty<TownHome>(), new[]
            {
                new TownLocation("start", Vector2.zero),
                new TownLocation("bottom", new Vector2(4f, 0f)),
                new TownLocation("top", new Vector2(0f, 4f)),
                new TownLocation("destination", new Vector2(4f, 4f))
            }, new[]
            {
                new TownRoad("start", "bottom"), new TownRoad("bottom", "destination"),
                new TownRoad("start", "top"), new TownRoad("top", "destination")
            });
            var follower = new TownRouteFollower2D(
                map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);
            follower.SetDestination("destination");
            follower.Advance(1f);
            Assert.That(follower.Position, Is.EqualTo(new Vector2(1f, 0f)));
            AddBox("Blocked lower road", new Vector2(2f, 0f));

            follower.Advance(100f);

            Assert.That(follower.Position, Is.EqualTo(new Vector2(4f, 4f)));
            Assert.That(follower.Status, Is.EqualTo(TownRouteStatus.Arrived));
        }

        [Test]
        public void ObstructedJourney_PartitionedDistanceMatchesWholeRequestAtClearStop()
        {
            var map = CreateCornerMap();
            var whole = new TownRouteFollower2D(
                map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);
            whole.SetDestination("destination");
            whole.Advance(1f);
            var partitioned = whole.Clone();
            var wall = AddBox("Added road wall", new Vector2(2f, 1.5f));
            wall.size = new Vector2(1f, 0.5f);

            whole.Advance(5f);
            for (var index = 0; index < 500; index++)
            {
                partitioned.Advance(0.01f);
            }

            Assert.That(whole.Status, Is.EqualTo(TownRouteStatus.Blocked));
            Assert.That(partitioned.Status, Is.EqualTo(whole.Status));
            Assert.That(Vector2.Distance(whole.Position, partitioned.Position), Is.LessThan(0.0001f));
            Assert.That(Physics2D.OverlapCircle(whole.Position, 0.3f), Is.Null);
            Assert.That(Physics2D.OverlapCircle(partitioned.Position, 0.3f), Is.Null);
        }

        [Test]
        public void CoincidentLocations_DoNotCreateZeroLengthMovementOrInvalidFacing()
        {
            var map = CreateCornerMap();
            map.Configure(Array.Empty<TownHome>(), new[]
            {
                new TownLocation("start", Vector2.zero),
                new TownLocation("same.position", Vector2.zero),
                new TownLocation("destination", new Vector2(2f, 0f))
            }, new[]
            {
                new TownRoad("start", "same.position"),
                new TownRoad("same.position", "destination")
            });
            var follower = new TownRouteFollower2D(
                map, Vector2.zero, 0.3f, Vector2.zero, _world.transform);

            follower.SetDestination("destination");
            follower.Advance(1f);

            Assert.That(follower.Position, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(follower.FacingDirection, Is.EqualTo(Vector2.right));
            follower.Advance(1f);
            Assert.That(follower.Position, Is.EqualTo(new Vector2(2f, 0f)));
            Assert.That(follower.Status, Is.EqualTo(TownRouteStatus.Arrived));
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null)
            {
                UnityEngine.Object.DestroyImmediate(_world);
            }
        }

        private TownMap2D CreateCornerMap()
        {
            _world = new GameObject("Follower world");
            var map = _world.AddComponent<TownMap2D>();
            map.Configure(Array.Empty<TownHome>(), new[]
            {
                new TownLocation("start", Vector2.zero),
                new TownLocation("corner", new Vector2(2f, 0f)),
                new TownLocation("destination", new Vector2(2f, 3f))
            }, new[]
            {
                new TownRoad("start", "corner"),
                new TownRoad("corner", "destination")
            });
            return map;
        }

        private BoxCollider2D AddBox(string name, Vector2 position)
        {
            var obstacle = new GameObject(name);
            obstacle.transform.SetParent(_world.transform);
            obstacle.transform.position = position;
            var box = obstacle.AddComponent<BoxCollider2D>();
            box.size = new Vector2(0.5f, 0.5f);
            return box;
        }
    }
}
