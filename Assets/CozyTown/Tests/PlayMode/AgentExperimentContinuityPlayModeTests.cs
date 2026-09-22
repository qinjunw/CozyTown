#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Core;
using CozyTown.Unity.Experiments;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class AgentExperimentContinuityPlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private const int ContinuousSteps = 11520;
        private const double StepSeconds = 1.0 / 64;
        private Scene _scene;
        private string _previousProxyEndpoint;
        private string _outputRoot;

        [SetUp]
        public void SetUp()
        {
            _previousProxyEndpoint = Environment.GetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable);
            Environment.SetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable, null);
            _outputRoot = Path.GetFullPath(Path.Combine("Logs", "agent-motion", "playmode-packages", Guid.NewGuid().ToString("N")));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            Environment.SetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable, _previousProxyEndpoint);
        }

        [UnityTest]
        public IEnumerator ContinuousSteps_RecordFractionalWalkingCheckpointAndReplayWithinDecisionLimits()
        {
            yield return LoadFreshScene();
            var source = Attach("available");
            source.ConfigureIdentity("motion-continuity-test-fixture");
            double virtualRealSeconds = 0;
            source.Start(NpcSpeechMode.StructuredFacts, "fixed", new FixedExperimentDecisionClient(), () => virtualRealSeconds);
            var residents = _scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NpcWorldResident2D>(true))
                .OrderBy(resident => resident.NpcId, StringComparer.Ordinal).ToArray();
            Assert.That(residents.Length, Is.EqualTo(4));
            GameSaveSnapshot checkpoint = null;
            NpcWorldResident2D savedResident = null;
            int savedAtStep = -1;
            bool loaded = false;
            int peakRequestsInMinute = 0, peakActiveRequests = 0;
            var recordingCost = Stopwatch.StartNew();

            for (int step = 1; step <= ContinuousSteps; step++)
            {
                virtualRealSeconds = step * StepSeconds + (step % 3) * 0.0001;
                source.Step(StepSeconds, virtualRealSeconds);
                AssertDecisionLimits(source);
                peakRequestsInMinute = Math.Max(peakRequestsInMinute, source.Controller.DecisionRequestsInLastMinute);
                peakActiveRequests = Math.Max(peakActiveRequests, source.Controller.ActiveDecisionRequests);

                double fraction = source.Services.WorldTimeFlow.Current.FractionalMinute;
                if (checkpoint == null && fraction > 1e-6 && fraction < 1 - 1e-6)
                {
                    savedResident = residents.FirstOrDefault(resident => resident.Status == TownRouteStatus.Travelling);
                    if (savedResident != null)
                    {
                        source.Save("walking-fraction");
                        var saved = source.Services.SaveStorage.Load("main");
                        Assert.That(saved.IsSuccess, Is.True, saved.ErrorCode);
                        checkpoint = saved.Value;
                        savedAtStep = step;
                        Assert.That(checkpoint.FractionalMinute, Is.GreaterThan(0).And.LessThan(1));
                        Assert.That(checkpoint.CompleteWorld.Residents.Single(body => body.NpcId == savedResident.NpcId).Route.Status,
                            Is.EqualTo((int)TownRouteStatus.Travelling));
                    }
                }

                if (checkpoint != null && !loaded && step == savedAtStep + 15)
                {
                    long requests = source.Controller.DecisionRequestsStarted;
                    Guid previousWorld = source.Controller.GetAgentState(savedResident.NpcId).WorldRunId;
                    Assert.That(source.Controller.GameTotalMinutes, Is.GreaterThan(checkpoint.CompleteWorld.World.TotalMinutes));

                    source.Load("walking-fraction");

                    var body = checkpoint.CompleteWorld.Residents.Single(item => item.NpcId == savedResident.NpcId);
                    Assert.That(source.Controller.GameTotalMinutes, Is.EqualTo(checkpoint.CompleteWorld.World.TotalMinutes));
                    Assert.That(source.Services.WorldTimeFlow.Current.FractionalMinute, Is.EqualTo(checkpoint.FractionalMinute));
                    Assert.That(savedResident.Position.x, Is.EqualTo(body.Route.Position.X));
                    Assert.That(savedResident.Position.y, Is.EqualTo(body.Route.Position.Y));
                    Assert.That(savedResident.Status, Is.EqualTo(TownRouteStatus.Travelling));
                    Assert.That(savedResident.TargetLocationId, Is.EqualTo(body.Route.TargetLocationId));
                    Assert.That(source.Controller.GetAgentState(savedResident.NpcId).WorldRunId, Is.Not.EqualTo(previousWorld));
                    Assert.That(source.Controller.DecisionRequestsStarted, Is.EqualTo(requests));
                    Assert.That(source.RealSeconds, Is.EqualTo(virtualRealSeconds));
                    Assert.That(source.Tick, Is.EqualTo(step));
                    AssertDecisionLimits(source);
                    loaded = true;
                }
            }

            Assert.That(checkpoint, Is.Not.Null, "The run must save a resident while walking at a fractional game minute.");
            Assert.That(loaded, Is.True);
            Assert.That(source.Tick, Is.EqualTo(ContinuousSteps));
            Assert.That(source.Controller.DecisionRequestsStarted, Is.GreaterThan(0));
            Assert.That(source.Controller.ActiveDecisionRequests, Is.Zero, "The fixed run must settle before completion.");
            source.Complete();
            recordingCost.Stop();

            string directory = Path.Combine(_outputRoot, "continuous-structured");
            var exportCost = Stopwatch.StartNew();
            source.Export(directory);
            exportCost.Stop();
            var package = AgentExperimentPackage.Read(directory);
            Assert.That(package.IsSuccess, Is.True, package.ErrorCode);
            var manifest = package.Value.Manifest;
            var recordedTrace = source.TraceClient.Trace;
            for (int i = 0; i < manifest.trace.ticks.Count; i++)
                Assert.That(manifest.trace.ticks[i].gameMinutes, Is.EqualTo(recordedTrace.ticks[i].gameMinutes),
                    $"Recorded world time must round-trip exactly at tick {i}.");
            Assert.That(manifest.schemaVersion, Is.EqualTo(1));
            Assert.That(manifest.inputs.Count(input => input.kind == "advance"), Is.EqualTo(ContinuousSteps));
            Assert.That(manifest.inputs.Where(input => input.kind == "advance").Select(input => input.elapsedGameSeconds),
                Is.All.EqualTo(StepSeconds));
            Assert.That(manifest.trace.ticks.Count, Is.EqualTo(ContinuousSteps));
            Assert.That(manifest.trace.calls.Count, Is.EqualTo(source.Controller.DecisionRequestsStarted));
            Assert.That(manifest.inputs.Count(input => input.kind == "save"), Is.EqualTo(1));
            Assert.That(manifest.inputs.Count(input => input.kind == "load"), Is.EqualTo(1));
            Assert.That(manifest.inputs.Last().kind, Is.EqualTo("complete"));
            var expectedCodes = source.Outcomes.Select(outcome => outcome.Code).ToArray();

            yield return LoadFreshScene();
            var replay = Attach("available");
            replay.StartReplay(directory);
            var replayCost = Stopwatch.StartNew();
            int replayedInputs = ReplayAll(replay, manifest.inputs.Count);
            replayCost.Stop();

            Assert.That(replay.Completed, Is.True);
            Assert.That(replay.Divergence, Is.Null);
            Assert.That(replay.Tick, Is.EqualTo(ContinuousSteps));
            Assert.That(replay.Outcomes.Select(outcome => outcome.Code), Is.EqualTo(expectedCodes));
            TestContext.WriteLine($"continuous execution cost: steps={ContinuousSteps}, inputs={replayedInputs}, "
                + $"calls={manifest.trace.calls.Count}, bytes={PackageBytes(directory)}, "
                + $"recordElapsedMs={recordingCost.Elapsed.TotalMilliseconds:F3}, exportElapsedMs={exportCost.Elapsed.TotalMilliseconds:F3}, "
                + $"replayElapsedMs={replayCost.Elapsed.TotalMilliseconds:F3}, peakRequestsInMinute={peakRequestsInMinute}, "
                + $"peakActiveRequests={peakActiveRequests}");
            TestContext.WriteLine("Recorded package: " + directory);
        }

        [UnityTest]
        public IEnumerator ExistingStructuredCandidatePackage_ReplaysSavedInputsWithoutSubdividingSteps()
        {
            yield return ReplayExistingPackage("e5a85eec41ea4d6d86b1f26c0545bc95", "S", 360, 16);
        }

        [UnityTest]
        public IEnumerator ExistingFreeTextCandidatePackage_ReplaysSleepInputsWithoutSubdividingSteps()
        {
            yield return ReplayExistingPackage("341acffc9d504736be8818a1e9a44e8f", "F", 160, 12);
        }

        private IEnumerator ReplayExistingPackage(string packageName, string speechMode, int expectedSteps, int expectedCalls)
        {
            string directory = ExtractExistingPackage(packageName);
            var package = AgentExperimentPackage.Read(directory);
            Assert.That(package.IsSuccess, Is.True, package.ErrorCode);
            var manifest = package.Value.Manifest;
            Assert.That(manifest.schemaVersion, Is.EqualTo(1));
            Assert.That(manifest.runMode, Is.EqualTo("fixed"));
            Assert.That(manifest.speechMode, Is.EqualTo(speechMode));
            Assert.That(manifest.trace.ticks.Count, Is.EqualTo(expectedSteps));
            Assert.That(manifest.trace.calls.Count, Is.EqualTo(expectedCalls));
            Assert.That(manifest.inputs.Where(input => input.kind == "advance").Select(input => input.elapsedGameSeconds),
                Is.All.EqualTo(0.5));

            yield return LoadFreshScene();
            var replay = Attach(manifest.scenarioId);
            replay.StartReplay(directory);
            var replayCost = Stopwatch.StartNew();
            int replayedInputs = ReplayAll(replay, manifest.inputs.Count);
            replayCost.Stop();

            Assert.That(replay.Completed, Is.True);
            Assert.That(replay.Divergence, Is.Null);
            Assert.That(replay.Tick, Is.EqualTo(expectedSteps));
            Assert.That(replay.Controller.DecisionRequestsStarted, Is.EqualTo(expectedCalls));
            TestContext.WriteLine($"legacy {speechMode} execution cost: steps={replay.Tick}, inputs={replayedInputs}, "
                + $"calls={expectedCalls}, bytes={PackageBytes(directory)}, replayElapsedMs={replayCost.Elapsed.TotalMilliseconds:F3}");
        }

        private string ExtractExistingPackage(string packageName)
        {
            string archivePath = Path.GetFullPath(Path.Combine("docs", "verification", "agent-platform-candidate-evidence",
                "candidate-entry-packages.zip"));
            Assert.That(File.Exists(archivePath), Is.True, archivePath);
            string directory = Path.Combine(_outputRoot, packageName);
            Directory.CreateDirectory(directory);
            using var archiveFile = File.OpenRead(archivePath);
            using var archive = new ZipArchive(archiveFile, ZipArchiveMode.Read);
            string prefix = packageName + "/";
            var entries = archive.Entries.Where(entry => entry.FullName.StartsWith(prefix, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(entry.Name)).ToArray();
            Assert.That(entries.Any(entry => entry.FullName == prefix + "manifest.json"), Is.True);
            foreach (var entry in entries)
            {
                Assert.That(entry.FullName, Is.EqualTo(prefix + entry.Name));
                using var input = entry.Open();
                using var output = File.Create(Path.Combine(directory, entry.Name));
                input.CopyTo(output);
            }
            return directory;
        }

        private IEnumerator LoadFreshScene()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
        }

        private AgentExperimentSession Attach(string scenarioId)
            => AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration(scenarioId)), scenarioId);

        private static int ReplayAll(AgentExperimentSession replay, int expectedInputs)
        {
            int inputs = 0;
            try
            {
                while (replay.ReplayNext())
                {
                    Assert.That(++inputs, Is.LessThanOrEqualTo(expectedInputs));
                    AssertDecisionLimits(replay);
                }
            }
            catch (Exception)
            {
                var expected = replay.TraceClient.Trace.ticks[Math.Max(0, replay.Tick - 1)];
                TestContext.WriteLine($"Replay failure after {inputs} inputs: tick={replay.Tick}, expected real={expected.realSeconds:R} game={expected.gameMinutes:R}, actual real={replay.RealSeconds:R} game={replay.Controller.GameTotalMinutes:R}");
                throw;
            }
            Assert.That(inputs, Is.EqualTo(expectedInputs));
            return inputs;
        }

        private static void AssertDecisionLimits(AgentExperimentSession session)
        {
            Assert.That(session.Controller.DecisionRequestsInLastMinute, Is.LessThanOrEqualTo(16));
            Assert.That(session.Controller.ActiveDecisionRequests, Is.LessThanOrEqualTo(2));
        }

        private static long PackageBytes(string directory)
            => Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
    }
}
#endif
