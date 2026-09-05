using System.Linq;
using CozyTown.Unity.Editor;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Npc;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class TownPromptAnchorSceneEditModeTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void WorldAndUiUpgradeOrders_KeepEveryNpcAnchorAboveTheFullCharacterFrame(bool worldFirst)
        {
            const string sourcePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            var fixturePath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/CozyTown/Tests/UnityEditMode/TownPromptAnchorFixture.unity");
            var previous = SceneManager.GetActiveScene();
            var scene = default(Scene);
            Assert.That(AssetDatabase.CopyAsset(sourcePath, fixturePath), Is.True);
            try
            {
                scene = EditorSceneManager.OpenScene(fixturePath, OpenSceneMode.Additive);
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    if (worldFirst)
                    {
                        CozyTownDevSceneMenu.UpgradeTownWorld(scene);
                        CozyTownProductionUiSceneUpgrader.UpgradeProductionUi(scene);
                    }
                    else
                    {
                        CozyTownProductionUiSceneUpgrader.UpgradeProductionUi(scene);
                        CozyTownDevSceneMenu.UpgradeTownWorld(scene);
                    }

                    var world = scene.GetRootGameObjects().Single(root => root.name == "World");
                    var npcs = world.GetComponentsInChildren<CozyTownNpcDebugPresenter>(true);
                    Assert.That(npcs, Has.Length.EqualTo(4));
                    foreach (var npc in npcs)
                    {
                        var point = npc.GetComponent<TownInteractionPoint2D>();
                        Assert.That(point.PromptAnchor.parent, Is.EqualTo(npc.transform), npc.NpcId);
                        Assert.That(point.PromptAnchor.localPosition,
                            Is.EqualTo(new Vector3(0f, 2.25f, 0f)), npc.NpcId);
                        Assert.That(npc.GetComponentsInChildren<Transform>(true)
                            .Count(child => child.name == "Prompt Anchor"), Is.EqualTo(1), npc.NpcId);
                    }
                }
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(fixturePath);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
    }
}
