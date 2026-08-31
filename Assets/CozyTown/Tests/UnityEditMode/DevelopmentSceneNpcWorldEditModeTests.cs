#if UNITY_EDITOR
using System;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Npc;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class DevelopmentSceneNpcWorldEditModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        [Test]
        public void DevelopmentScene_ContainsFourIdentityBoundNpcWorldEntities()
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                GameObject world = RequireRoot(scene, "World");
                TownInteractionPoint2D[] allPoints =
                    world.GetComponentsInChildren<TownInteractionPoint2D>(true);
                Assert.That(allPoints, Has.Length.EqualTo(10));
                Assert.That(
                    allPoints.Select(point => point.Kind).Distinct().Count(),
                    Is.EqualTo(7));

                TownInteractionPoint2D[] npcPoints = allPoints
                    .Where(point => point.Kind == TownInteractionKind.Npc)
                    .ToArray();
                Assert.That(npcPoints, Has.Length.EqualTo(4));

                string[] expectedNpcIds =
                {
                    DefaultMvpIds.Npcs.Shopkeeper,
                    DefaultMvpIds.Npcs.Farmer,
                    DefaultMvpIds.Npcs.Fisher,
                    DefaultMvpIds.Npcs.Cook
                };
                string[] expectedSpriteNames =
                {
                    "npc_shopkeeper_mina_idle_down",
                    "npc_farmer_eli_idle_down",
                    "npc_fisher_ren_idle_down",
                    "npc_cook_sora_idle_down"
                };

                CollectionAssert.AreEquivalent(
                    expectedNpcIds,
                    npcPoints.Select(point =>
                        point.GetComponent<CozyTownNpcDebugPresenter>()?.NpcId));
                CollectionAssert.AreEquivalent(
                    expectedSpriteNames,
                    npcPoints.Select(point =>
                        point.transform.Find("Visual")
                            ?.GetComponent<SpriteRenderer>()
                            ?.sprite
                            ?.name));

                Vector2[] npcPositions = npcPoints
                    .Select(point => (Vector2)point.transform.position)
                    .ToArray();
                for (var first = 0; first < npcPositions.Length; first++)
                {
                    for (var second = first + 1; second < npcPositions.Length; second++)
                    {
                        Assert.That(
                            Vector2.Distance(npcPositions[first], npcPositions[second]),
                            Is.GreaterThanOrEqualTo(3f),
                            $"NPCs at {npcPositions[first]} and {npcPositions[second]} are visually clustered.");
                    }
                }
                foreach (TownInteractionPoint2D point in npcPoints)
                {
                    Assert.That(point.PromptText, Is.EqualTo("Press E to talk"));
                    Assert.That(point.GetComponent<Collider2D>()?.isTrigger, Is.True);
                    Assert.That(point.GetComponent<CozyTownNpcDebugPresenter>(), Is.Not.Null);
                    Assert.That(
                        Physics2D.OverlapPointAll(point.transform.position)
                            .Any(collider => !collider.isTrigger),
                        Is.False,
                        $"{point.name} overlaps a solid obstacle at {point.transform.position}.");
                }
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
            return Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == name)
                ?? throw new InvalidOperationException($"Root object '{name}' was not found.");
        }
    }
}
#endif
