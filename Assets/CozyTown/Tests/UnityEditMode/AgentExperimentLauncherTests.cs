using System;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Unity.Experiments;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class AgentExperimentLauncherTests
    {
        private const string LiveConfiguration = "{\"model\":\"test-model\",\"promptVersion\":\"test-prompt\",\"protocolVersion\":4,\"maxTokens\":512,\"thinking\":\"disabled\",\"responseFormat\":\"json_object\",\"stream\":false}";

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t ")]
        public void LaunchOptions_RequireAnEvaluationPlanReferenceOrExplicitNone(string reference)
        {
            var options = new AgentExperimentLaunchOptions { arm = AgentExperimentArm.F,
                runMode = AgentExperimentRunMode.Fixed, evaluationPlanReference = reference };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Test]
        public void PairedOptions_FreezePreservesTheRegisteredEvaluationPlanReference()
        {
            var options = new AgentExperimentLaunchOptions { arm = AgentExperimentArm.Paired,
                runMode = AgentExperimentRunMode.Fixed };
            Assert.That(options.evaluationPlanReference, Is.EqualTo("none"));
            options.evaluationPlanReference = "experiment-plan:paired-resource-study-v1";

            var frozen = options.Freeze();
            options.evaluationPlanReference = "none";

            Assert.That(frozen.evaluationPlanReference, Is.EqualTo("experiment-plan:paired-resource-study-v1"));
            CollectionAssert.AreEqual(new[] { NpcSpeechMode.FreeText, NpcSpeechMode.StructuredFacts }, frozen.CreateArms());
        }

        [Test]
        public void LiveOptions_RequireHttpLoopbackForDecisionAndMeasurementEndpoints()
        {
            var options = new AgentExperimentLaunchOptions { arm = AgentExperimentArm.F,
                runMode = AgentExperimentRunMode.Live, clientConfigurationJson = LiveConfiguration };
            Assert.DoesNotThrow(() => options.Validate());

            options.proxyEndpoint = "https://127.0.0.1:8765/decide";

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Test]
        public void Launcher_ExposesTheFrozenLiveEndpointWithoutCreatingAWorld()
        {
            var root = new GameObject("Configured live experiment");
            root.SetActive(false);
            try
            {
                var launcher = root.AddComponent<AgentExperimentLauncher>();
                var options = new AgentExperimentLaunchOptions { arm = AgentExperimentArm.S,
                    runMode = AgentExperimentRunMode.Live, clientConfigurationJson = LiveConfiguration,
                    proxyEndpoint = "http://127.0.0.1:9876/decide" };
                launcher.Configure(options);
                options.proxyEndpoint = "http://127.0.0.1:8765/decide";

                Assert.That(launcher.ProxyEndpoint, Is.EqualTo("http://127.0.0.1:9876/decide"));
                Assert.That(launcher.Services, Is.Null);
                Assert.That(launcher.Session, Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PairedLaunchers_CreateIndependentWorldsFromTheFrozenResourceCase()
        {
            var options = new AgentExperimentLaunchOptions { arm = AgentExperimentArm.Paired,
                runMode = AgentExperimentRunMode.Fixed, scenarioId = "seller_empty", pairId = "paired-test" };
            CollectionAssert.AreEqual(new[] { NpcSpeechMode.FreeText, NpcSpeechMode.StructuredFacts }, options.CreateArms());
            var firstObject = new GameObject("First experiment");
            var secondObject = new GameObject("Second experiment");
            firstObject.SetActive(false);
            secondObject.SetActive(false);
            try
            {
                var first = firstObject.AddComponent<AgentExperimentLauncher>();
                var second = secondObject.AddComponent<AgentExperimentLauncher>();
                first.Configure(options, 0);
                second.Configure(options, 1);
                options.scenarioId = "available";

                var firstServices = first.Create();
                var secondServices = second.Create();

                Assert.That(firstServices, Is.Not.SameAs(secondServices));
                foreach (var services in new[] { firstServices, secondServices })
                {
                    Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(720));
                    var characters = services.EconomyState.CaptureSnapshot().Characters;
                    Assert.That(characters.Length, Is.EqualTo(5));
                    Assert.That(characters.Single(item => item.CharacterId == DefaultMvpIds.Npcs.Fisher)
                        .Backpack.Items.Any(item => item.ItemId == DefaultMvpIds.Items.Carp), Is.False);
                    Assert.That(services.WorldSnapshots.IsRequired, Is.True);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstObject);
                UnityEngine.Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void LaunchOptions_RequireExplicitArmAndRunModeBeforeCreatingAWorld()
        {
            var options = new AgentExperimentLaunchOptions();
            Assert.Throws<ArgumentException>(() => options.Validate());
            options.arm = AgentExperimentArm.F;
            Assert.Throws<ArgumentException>(() => options.Validate());
            options.runMode = AgentExperimentRunMode.Fixed;
            Assert.DoesNotThrow(() => options.Validate());
            CollectionAssert.AreEqual(new[] { NpcSpeechMode.FreeText }, options.CreateArms());
        }
    }
}
