using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class ProjectConfigurationEditModeTests
    {
        private const string DevelopmentScenePath =
            "Assets/CozyTown/Scenes/CozyTown_Dev.unity";

        [Test]
        public void BuildSettings_EnableOnlyCozyTownDevelopmentScene()
        {
            string[] enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            Assert.That(enabledScenes, Is.EqualTo(new[] { DevelopmentScenePath }));
        }

        [Test]
        public void PlayerSettings_UseKaiisaApplicationIdentity()
        {
            Assert.That(PlayerSettings.companyName, Is.EqualTo("Kaiisa"));
            Assert.That(PlayerSettings.productName, Is.EqualTo("KaiisaTown"));
            Assert.That(
                PlayerSettings.GetApplicationIdentifier(
                    NamedBuildTarget.Standalone),
                Is.EqualTo("com.Kaiisa.KaiisaTown"));
        }
    }
}
