using System;
using CozyTown.Unity.Interaction;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownWorldCollisionSceneUpgrader
    {
        public const string ObstaclesRootName = "Obstacles";

        private static readonly Vector2[] FarmOutline =
        {
            new Vector2(-2.2f, 0.05f),
            new Vector2(2.2f, 0.05f),
            new Vector2(2.5f, 0.25f),
            new Vector2(2.65f, 0.7f),
            new Vector2(2.65f, 3.3f),
            new Vector2(2.5f, 3.75f),
            new Vector2(2.15f, 3.95f),
            new Vector2(-2.15f, 3.95f),
            new Vector2(-2.5f, 3.75f),
            new Vector2(-2.65f, 3.3f),
            new Vector2(-2.65f, 0.7f),
            new Vector2(-2.5f, 0.25f)
        };

        private static readonly Vector2[] PondOutline =
        {
            new Vector2(-0.45f, 0.05f),
            new Vector2(-1.6f, 0.25f),
            new Vector2(-2.05f, 0.55f),
            new Vector2(-2.35f, 1f),
            new Vector2(-2.5f, 1.55f),
            new Vector2(-2.45f, 2.45f),
            new Vector2(-2.15f, 3.1f),
            new Vector2(-1.55f, 3.55f),
            new Vector2(-0.8f, 3.85f),
            new Vector2(0.5f, 3.9f),
            new Vector2(1.45f, 3.55f),
            new Vector2(2.05f, 3f),
            new Vector2(2.35f, 2.35f),
            new Vector2(2.4f, 1.5f),
            new Vector2(2.15f, 0.9f),
            new Vector2(1.75f, 0.5f),
            new Vector2(1f, 0.2f)
        };

        public static void ConfigureWorld(GameObject world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var obstacles = GetOrCreateChild(world.transform, ObstaclesRootName);
            ResetLocalTransform(obstacles);

            ConfigureBuilding(
                world,
                obstacles,
                TownInteractionKind.Shop,
                "Shop Obstacle",
                doorCenterX: 0f,
                doorWidth: 1f,
                doorDepth: 0.6f,
                triggerCenterY: 0.55f,
                triggerHeight: 0.55f);
            ConfigureBuilding(
                world,
                obstacles,
                TownInteractionKind.Coop,
                "Coop Obstacle",
                doorCenterX: 0f,
                doorWidth: 1.25f,
                doorDepth: 0.6f,
                triggerCenterY: 0.6f,
                triggerHeight: 0.6f);
            ConfigureBuilding(
                world,
                obstacles,
                TownInteractionKind.Kitchen,
                "Kitchen Obstacle",
                doorCenterX: 0.75f,
                doorWidth: 1f,
                doorDepth: 0.6f,
                triggerCenterY: 0.55f,
                triggerHeight: 0.55f);
            ConfigureBuilding(
                world,
                obstacles,
                TownInteractionKind.Bed,
                "Home Obstacle",
                doorCenterX: 0.35f,
                doorWidth: 1f,
                doorDepth: 0.6f,
                triggerCenterY: 0.55f,
                triggerHeight: 0.55f);

            ConfigureStandaloneObstacle(
                obstacles,
                FindUniquePoint(world, TownInteractionKind.Farm),
                "Farm Obstacle",
                FarmOutline);

            var pondPoint = FindUniquePoint(world, TownInteractionKind.Pond);
            ConfigureStandaloneObstacle(
                obstacles,
                pondPoint,
                "Pond Obstacle",
                PondOutline);
            ConfigurePolygonTrigger(pondPoint.gameObject, PondOutline);

            EditorUtility.SetDirty(world);
            EditorUtility.SetDirty(obstacles.gameObject);
        }

        private static void ConfigureBuilding(
            GameObject world,
            Transform obstacles,
            TownInteractionKind kind,
            string obstacleName,
            float doorCenterX,
            float doorWidth,
            float doorDepth,
            float triggerCenterY,
            float triggerHeight)
        {
            var point = FindUniquePoint(world, kind);
            ConfigureStandaloneObstacle(
                obstacles,
                point,
                obstacleName,
                CreateBuildingOutline(doorCenterX, doorWidth, doorDepth));
            ConfigureBoxTrigger(
                point.gameObject,
                new Vector2(doorCenterX, triggerCenterY),
                new Vector2(doorWidth - 0.15f, triggerHeight));
        }

        private static Vector2[] CreateBuildingOutline(
            float doorCenterX,
            float doorWidth,
            float doorDepth)
        {
            const float left = -1.9f;
            const float right = 1.9f;
            const float bottom = 0f;
            const float top = 2.4f;
            var halfDoor = doorWidth * 0.5f;
            var doorLeft = doorCenterX - halfDoor;
            var doorRight = doorCenterX + halfDoor;
            return new[]
            {
                new Vector2(left, bottom),
                new Vector2(doorLeft, bottom),
                new Vector2(doorLeft, doorDepth),
                new Vector2(doorRight, doorDepth),
                new Vector2(doorRight, bottom),
                new Vector2(right, bottom),
                new Vector2(right, top),
                new Vector2(left, top)
            };
        }

        private static void ConfigureStandaloneObstacle(
            Transform obstacles,
            TownInteractionPoint2D landmark,
            string obstacleName,
            Vector2[] path)
        {
            var obstacle = GetOrCreateChild(obstacles, obstacleName);
            obstacle.position = landmark.transform.position;
            obstacle.rotation = landmark.transform.rotation;
            obstacle.localScale = Vector3.one;
            obstacle.gameObject.layer = landmark.gameObject.layer;

            RemoveRigidbodies(obstacle.gameObject);
            var collider = RequireSingleCollider<PolygonCollider2D>(obstacle.gameObject);
            ConfigurePolygon(collider, path, isTrigger: false);
        }

        private static void ConfigureBoxTrigger(
            GameObject point,
            Vector2 offset,
            Vector2 size)
        {
            var collider = RequireSingleCollider<BoxCollider2D>(point);
            collider.enabled = true;
            collider.isTrigger = true;
            collider.offset = offset;
            collider.size = size;
            collider.edgeRadius = 0f;
            EditorUtility.SetDirty(collider);
        }

        private static void ConfigurePolygonTrigger(GameObject point, Vector2[] path)
        {
            var collider = RequireSingleCollider<PolygonCollider2D>(point);
            ConfigurePolygon(collider, path, isTrigger: true);
        }

        private static void ConfigurePolygon(
            PolygonCollider2D collider,
            Vector2[] path,
            bool isTrigger)
        {
            collider.enabled = true;
            collider.isTrigger = isTrigger;
            collider.offset = Vector2.zero;
            collider.pathCount = 1;
            collider.SetPath(0, path);
            EditorUtility.SetDirty(collider);
        }

        private static T RequireSingleCollider<T>(GameObject target)
            where T : Collider2D
        {
            var colliders = target.GetComponents<Collider2D>();
            T retained = null;
            foreach (var collider in colliders)
            {
                if (retained == null && collider is T requested)
                {
                    retained = requested;
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(collider);
            }

            if (retained == null)
            {
                retained = target.AddComponent<T>();
            }

            return retained;
        }

        private static void RemoveRigidbodies(GameObject target)
        {
            foreach (var body in target.GetComponents<Rigidbody2D>())
            {
                UnityEngine.Object.DestroyImmediate(body);
            }
        }

        private static TownInteractionPoint2D FindUniquePoint(
            GameObject world,
            TownInteractionKind kind)
        {
            TownInteractionPoint2D match = null;
            foreach (var point in world.GetComponentsInChildren<TownInteractionPoint2D>(true))
            {
                if (point.Kind != kind)
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"World contains more than one {kind} interaction point.");
                }

                match = point;
            }

            return match ?? throw new InvalidOperationException(
                $"World is missing its {kind} interaction point.");
        }

        private static Transform GetOrCreateChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
            {
                return child;
            }

            var childObject = new GameObject(name);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }

        private static void ResetLocalTransform(Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }
    }
}
