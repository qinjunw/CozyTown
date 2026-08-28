using System;
using CozyTown.Unity.Core;
using CozyTown.Unity.Hud;
using CozyTown.Unity.Input;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Player;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class DevelopmentSceneEditModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        [Test]
        public void DevelopmentScene_ContainsTheM2WalkingSlice()
        {
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                var bootstrap = RequireRoot(scene, "CozyTown");
                Assert.That(bootstrap.GetComponent<CozyTownBootstrap>(), Is.Not.Null);

                var player = RequireRoot(scene, "Player");
                Assert.That(player.GetComponent<Rigidbody2D>(), Is.Not.Null);
                Assert.That(player.GetComponent<Collider2D>(), Is.Not.Null);
                Assert.That(player.GetComponent<InputSystemPlayerInputSource>(), Is.Not.Null);
                Assert.That(player.GetComponent<PlayerMovement2D>(), Is.Not.Null);
                Assert.That(player.GetComponent<PlayerInteractor2D>(), Is.Not.Null);
                Assert.That(player.GetComponentInChildren<SpriteRenderer>(true), Is.Not.Null);

                var world = RequireRoot(scene, "World");
                var boundaries = world.transform.Find("Boundaries");
                Assert.That(boundaries, Is.Not.Null);
                Assert.That(
                    boundaries.GetComponentsInChildren<BoxCollider2D>(true),
                    Has.Length.EqualTo(4));

                var points = world.GetComponentsInChildren<TownInteractionPoint2D>(true);
                Assert.That(points, Has.Length.EqualTo(4));
                foreach (var point in points)
                {
                    Assert.That(point.PromptText, Is.Not.Empty);
                }

                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        TownInteractionKind.Shop,
                        TownInteractionKind.Npc,
                        TownInteractionKind.Bed,
                        TownInteractionKind.Farm
                    },
                    Array.ConvertAll(points, point => point.Kind));

                var hud = RequireRoot(scene, "Debug HUD");
                Assert.That(hud.GetComponent<CozyTownHudPresenter>(), Is.Not.Null);
                Assert.That(hud.GetComponent<CozyTownInteractionDebugView>(), Is.Not.Null);

                var camera = RequireRoot(scene, "Main Camera");
                Assert.That(camera.GetComponent<Camera>()?.orthographic, Is.True);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene);
                }
            }
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            var root = Array.Find(
                scene.GetRootGameObjects(),
                candidate => candidate.name == name);
            Assert.That(root, Is.Not.Null, $"Root object '{name}' was not found.");
            return root;
        }
    }
}
