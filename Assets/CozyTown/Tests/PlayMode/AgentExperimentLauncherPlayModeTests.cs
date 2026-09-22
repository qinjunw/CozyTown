using System.Collections;
using CozyTown.Runtime.Core;
using CozyTown.Unity.Core;
using CozyTown.Unity.Experiments;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class AgentExperimentLauncherPlayModeTests
    {
        [UnityTest]
        public IEnumerator Activation_InstallsTheExperimentBeforeBootstrapAwake()
        {
            var root = new GameObject("Experiment startup");
            root.SetActive(false);
            try
            {
                var bootstrap = root.AddComponent<CozyTownBootstrap>();
                var fallback = new CountingFactory();
                bootstrap.SetFactory(fallback);
                var launcher = root.AddComponent<AgentExperimentLauncher>();
                launcher.Configure(new AgentExperimentLaunchOptions {
                    arm = AgentExperimentArm.F, runMode = AgentExperimentRunMode.Fixed, scenarioId = "seller_empty" });

                root.SetActive(true);

                Assert.That(fallback.Calls, Is.Zero, "The launcher must replace the factory before Bootstrap creates services.");
                Assert.That(bootstrap.IsInitialized, Is.True);
                Assert.That(launcher.Services, Is.Not.Null);
                Assert.That(launcher.Services.WorldSnapshots.IsRequired, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            yield return null;
        }

        private sealed class CountingFactory : ICozyTownServicesFactory
        {
            public int Calls { get; private set; }
            public CozyTownServices Create()
            {
                Calls++;
                return CozyTownCompositionRoot.CreateDefault();
            }
        }
    }
}
