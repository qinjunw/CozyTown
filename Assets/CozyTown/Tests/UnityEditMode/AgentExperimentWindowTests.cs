using System;
using CozyTown.Unity.Core;
using CozyTown.Unity.Editor;
using CozyTown.Unity.Experiments;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class AgentExperimentWindowTests
    {
        [Test]
        public void Install_RequiresExplicitOptionsThenAddsOneLauncherWithoutCreatingAWorldInEditMode()
        {
            var scene = SceneManager.GetActiveScene();
            var root = new GameObject("Experiment bootstrap");
            try
            {
                SceneManager.MoveGameObjectToScene(root, scene);
                var bootstrap = root.AddComponent<CozyTownBootstrap>();
                Assert.Throws<ArgumentException>(() => AgentExperimentWindow.Install(scene, new AgentExperimentLaunchOptions()));
                Assert.That(root.GetComponent<AgentExperimentLauncher>(), Is.Null);

                var options = new AgentExperimentLaunchOptions { arm = AgentExperimentArm.S, runMode = AgentExperimentRunMode.Fixed };
                var launcher = AgentExperimentWindow.Install(scene, options);

                Assert.That(launcher.gameObject, Is.SameAs(root));
                Assert.That(launcher.Services, Is.Null);
                Assert.That(bootstrap.IsInitialized, Is.False);
                Assert.Throws<InvalidOperationException>(() => AgentExperimentWindow.Install(scene, options));
                Assert.That(root.GetComponents<AgentExperimentLauncher>().Length, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
