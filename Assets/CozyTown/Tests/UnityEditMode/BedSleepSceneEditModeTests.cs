using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class BedSleepSceneEditModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        [Test]
        public void DevelopmentScene_BedContainsOneHourSelectorAndExplicitSleepConfirmation()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject hud = scene.GetRootGameObjects().Single(root => root.name == "Debug HUD");
                Transform panel = hud.transform.Find("Production UI/Bed Panel");
                Assert.That(panel, Is.Not.Null);
                Text[] texts = panel.GetComponentsInChildren<Text>(true);
                Text[] hours = texts.Where(text => text.name == "Sleep Hours Text").ToArray();
                Assert.That(hours, Has.Length.EqualTo(1));
                Assert.That(hours[0].text, Is.EqualTo("8 hours"));
                Button[] buttons = panel.GetComponentsInChildren<Button>(true);
                foreach (string name in new[]
                {
                    "Decrease Sleep Button", "Increase Sleep Button", "Sleep Button", "Close Button"
                })
                {
                    Assert.That(buttons.Count(button => button.name == name), Is.EqualTo(1), name);
                }

                Assert.That(texts.Any(text => text.text.Contains("until tomorrow")), Is.False);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }
    }
}
