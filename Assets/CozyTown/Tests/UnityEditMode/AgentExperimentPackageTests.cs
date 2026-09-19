using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Experiments;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class AgentExperimentPackageTests
    {
        private string _directory;

        [SetUp]
        public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "CozyTown.Package.Tests", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CozyTown.Package.Tests")) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(_directory), Does.StartWith(root));
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void CompletedPackage_RoundTripsManifestTraceInputsResultsAndCompleteSnapshots()
        {
            var initial = Snapshot(720);
            var final = Snapshot(725, 7);
            var manifest = Manifest();
            manifest.evaluationPlanReference = "docs/verification/frozen-expression-plan.md@candidate-sha";
            manifest.inputs.Add(new AgentExperimentInput { tick = 0, kind = "advance", elapsedGameSeconds = 2.5,
                realSeconds = 5, worldBefore = "old-run", worldAfter = "old-run" });
            manifest.inputs.Add(new AgentExperimentInput { tick = 1, kind = "complete", realSeconds = 6,
                worldBefore = "old-run", worldAfter = "old-run" });
            manifest.results.Add(new AgentExperimentResult { npcId = DefaultMvpIds.Npcs.Shopkeeper,
                worldRunId = "old-run", decisionId = "old-decision", code = "agent.decision_wait", calls = 1,
                startedRealSeconds = 0, finishedRealSeconds = 5, requestJson = "{\"step\":1}",
                replyJson = "{\"operation\":\"wait\"}", executionObservationJson = "{\"region\":\"main-road\"}" });
            manifest.measurementsJson = "{\"requestsStarted\":1}";
            manifest.trace.ticks.Add(new DecisionTraceTick { tick = 0, realSeconds = 5, gameMinutes = 725 });

            var exported = AgentExperimentPackage.Export(_directory, manifest, initial, final,
                new Dictionary<string, GameSaveSnapshot> { { "before-load_1", initial } });

            Assert.That(exported.IsSuccess, Is.True, exported.ErrorCode);
            CollectionAssert.AreEquivalent(new[] { "manifest.json", "initial.json", "final.json", "checkpoint-before-load_1.json" },
                Directory.GetFiles(_directory).Select(Path.GetFileName));
            var loaded = AgentExperimentPackage.Read(_directory);
            Assert.That(loaded.IsSuccess, Is.True, loaded.ErrorCode);
            Assert.That(loaded.Value.Manifest.schemaVersion, Is.EqualTo(1));
            Assert.That(loaded.Value.Manifest.speechMode, Is.EqualTo("S"));
            Assert.That(loaded.Value.Manifest.expressionContractVersion, Is.EqualTo("1.0"));
            Assert.That(loaded.Value.Manifest.evaluationPlanReference,
                Is.EqualTo("docs/verification/frozen-expression-plan.md@candidate-sha"));
            Assert.That(loaded.Value.Manifest.scenarioId, Is.EqualTo("available"));
            Assert.That(loaded.Value.Manifest.armOrder, Is.EqualTo(new[] { "F", "S" }));
            Assert.That(loaded.Value.Manifest.hasFinalSnapshot, Is.True);
            Assert.That(loaded.Value.Manifest.checkpointLabels, Is.EqualTo(new[] { "before-load_1" }));
            Assert.That(loaded.Value.Manifest.trace.ticks.Single().gameMinutes, Is.EqualTo(725));
            Assert.That(loaded.Value.Manifest.inputs.First().worldBefore, Is.EqualTo("old-run"));
            Assert.That(loaded.Value.Manifest.inputs.First().elapsedGameSeconds, Is.EqualTo(2.5));
            Assert.That(loaded.Value.Manifest.inputs.Last().kind, Is.EqualTo("complete"));
            Assert.That(loaded.Value.Manifest.inputs.Last().realSeconds, Is.EqualTo(6));
            Assert.That(loaded.Value.Manifest.results.Single().code, Is.EqualTo("agent.decision_wait"));
            Assert.That(loaded.Value.Manifest.results.Single().requestJson, Is.EqualTo("{\"step\":1}"));
            Assert.That(loaded.Value.Manifest.measurementsJson, Is.EqualTo("{\"requestsStarted\":1}"));
            Assert.That(loaded.Value.Initial.SchemaVersion, Is.EqualTo(4));
            Assert.That(loaded.Value.Initial.CompleteWorld.Residents.Count, Is.EqualTo(4));
            Assert.That(loaded.Value.Final.Clock.MinuteOfDay, Is.EqualTo(725));
            Assert.That(loaded.Value.Final.Characters.Single(item => item.CharacterId == DefaultMvpIds.Characters.Player)
                .Wallet.Balance, Is.EqualTo(307));
            Assert.That(loaded.Value.Checkpoints["before-load_1"].Clock.MinuteOfDay, Is.EqualTo(720));
            Assert.That(new JsonFileSaveStorage(Path.Combine(_directory, "initial.json")).Load("main").IsSuccess, Is.True);
        }

        [TestCase("expressionContractVersion", "missing")]
        [TestCase("expressionContractVersion", "null")]
        [TestCase("expressionContractVersion", "unsupported")]
        [TestCase("expressionContractVersion", "number")]
        [TestCase("evaluationPlanReference", "missing")]
        [TestCase("evaluationPlanReference", "null")]
        [TestCase("evaluationPlanReference", "blank")]
        [TestCase("evaluationPlanReference", "too-long")]
        public void Read_RequiresAnExplicitSupportedExpressionContractAndEvaluationPlan(string field, string damage)
        {
            Assert.That(AgentExperimentPackage.Export(_directory, Manifest(), Snapshot(720), Snapshot(725)).IsSuccess, Is.True);
            string path = Path.Combine(_directory, "manifest.json");
            string json = File.ReadAllText(path);
            string pattern = "\\\"" + field + "\\\"\\s*:\\s*\\\"[^\\\"]*\\\"";
            string value = damage switch {
                "null" => "null",
                "unsupported" => "\"2.0\"",
                "number" => "1.0",
                "blank" => "\" \"",
                "too-long" => "\"" + new string('x', 4097) + "\"",
                _ => null };
            string changed = damage == "missing"
                ? new Regex(pattern + "\\s*,").Replace(json, "", 1)
                : new Regex(pattern).Replace(json, "\"" + field + "\":" + value, 1);
            Assert.That(changed, Is.Not.EqualTo(json));
            File.WriteAllText(path, changed);

            Assert.That(AgentExperimentPackage.Read(_directory).IsSuccess, Is.False, field + ": " + damage);
            Assert.That(AgentExperimentPackage.Read(_directory, forReplay: false).IsSuccess, Is.False,
                "Evidence reads must not invent an expression contract or evaluation plan.");
        }

        [TestCase("contract-null")]
        [TestCase("contract-unsupported")]
        [TestCase("plan-null")]
        [TestCase("plan-blank")]
        [TestCase("plan-too-long")]
        public void Export_RejectsInvalidExpressionContractOrEvaluationPlanBeforeWritingFiles(string damage)
        {
            var manifest = Manifest();
            if (damage == "contract-null") manifest.expressionContractVersion = null;
            if (damage == "contract-unsupported") manifest.expressionContractVersion = "2.0";
            if (damage == "plan-null") manifest.evaluationPlanReference = null;
            if (damage == "plan-blank") manifest.evaluationPlanReference = " ";
            if (damage == "plan-too-long") manifest.evaluationPlanReference = new string('x', 4097);

            Assert.That(AgentExperimentPackage.Export(_directory, manifest, Snapshot(720), Snapshot(725)).IsSuccess, Is.False);
            Assert.That(Directory.Exists(_directory), Is.False);
        }

        [Test]
        public void UnfinishedPackage_PreservesEvidenceButCannotStartReplay()
        {
            var manifest = Manifest();
            manifest.completed = false;
            manifest.trace.completed = false;
            var saved = AgentExperimentPackage.Export(_directory, manifest, Snapshot(720), null);
            Assert.That(saved.IsSuccess, Is.True, saved.ErrorCode);
            Assert.That(File.Exists(Path.Combine(_directory, "final.json")), Is.False);

            var evidence = AgentExperimentPackage.Read(_directory, forReplay: false);
            Assert.That(evidence.IsSuccess, Is.True, evidence.ErrorCode);
            Assert.That(evidence.Value.Manifest.hasFinalSnapshot, Is.False);
            Assert.That(evidence.Value.Final, Is.Null);
            var replay = AgentExperimentPackage.Read(_directory);
            Assert.That(replay.IsSuccess, Is.False);
            Assert.That(replay.ErrorCode, Is.EqualTo("experiment.incomplete"));
        }

        [TestCase("missing-schema")]
        [TestCase("future-schema")]
        [TestCase("missing-trace-schema")]
        [TestCase("future-trace-schema")]
        [TestCase("trace")]
        [TestCase("ticks")]
        [TestCase("calls")]
        [TestCase("inputs")]
        [TestCase("results")]
        public void Read_RejectsMissingVersionsAndRequiredCollections(string damage)
        {
            var saved = AgentExperimentPackage.Export(_directory, Manifest(), Snapshot(720), Snapshot(725));
            Assert.That(saved.IsSuccess, Is.True, saved.ErrorCode);
            string path = Path.Combine(_directory, "manifest.json");
            string json = File.ReadAllText(path);
            var manifest = JsonUtility.FromJson<AgentExperimentManifest>(json);
            if (damage == "missing-schema") json = new Regex("\\\"schemaVersion\\\"\\s*:\\s*1\\s*,").Replace(json, "", 1);
            else if (damage == "missing-trace-schema")
            {
                int start = json.IndexOf("\"trace\"", StringComparison.Ordinal);
                json = json.Substring(0, start) + new Regex("\\\"schemaVersion\\\"\\s*:\\s*1\\s*,")
                    .Replace(json.Substring(start), "", 1);
            }
            else
            {
                if (damage == "future-schema") manifest.schemaVersion = 2;
                else if (damage == "future-trace-schema") manifest.trace.schemaVersion = 2;
                if (damage.StartsWith("future-", StringComparison.Ordinal)) json = JsonUtility.ToJson(manifest);
                else
                {
                    string pattern = "\\\"" + damage + "\\\"\\s*:\\s*" +
                        (damage == "trace" ? "\\{[^{}]*\\}" : "\\[\\s*\\]");
                    string damaged = new Regex(pattern).Replace(json, "\"" + damage + "\":null", 1);
                    Assert.That(damaged, Is.Not.EqualTo(json));
                    json = damaged;
                }
            }
            File.WriteAllText(path, json);

            var replay = AgentExperimentPackage.Read(_directory);
            Assert.That(replay.IsSuccess, Is.False, damage);
        }

        [TestCase("speech")]
        [TestCase("client")]
        [TestCase("manifest-configuration")]
        [TestCase("content")]
        [TestCase("body")]
        [TestCase("schedule")]
        [TestCase("settings")]
        [TestCase("profile")]
        [TestCase("plan")]
        [TestCase("seed")]
        public void Read_RejectsAFrameOrManifestWithDifferentExperimentConfiguration(string damage)
        {
            var saved = AgentExperimentPackage.Export(_directory, Manifest(), Snapshot(720), Snapshot(725),
                new Dictionary<string, GameSaveSnapshot> { { "middle", Snapshot(722) } });
            Assert.That(saved.IsSuccess, Is.True, saved.ErrorCode);
            if (damage == "speech" || damage == "client" || damage == "manifest-configuration")
            {
                string path = Path.Combine(_directory, "manifest.json");
                var manifest = JsonUtility.FromJson<AgentExperimentManifest>(File.ReadAllText(path));
                if (damage == "speech") manifest.speechMode = "F";
                if (damage == "client") manifest.trace.clientConfiguration = "other-client-v1";
                if (damage == "manifest-configuration") manifest.configuration = "other-experiment-v1";
                File.WriteAllText(path, JsonUtility.ToJson(manifest));
            }
            else
            {
                string path = Path.Combine(_directory, damage == "content" ? "final.json" : "checkpoint-middle.json");
                string original = File.ReadAllText(path);
                string changed = damage switch {
                    "content" => original.Replace("package-content-v1", "package-content-v2"),
                    "body" => original.Replace("package-body-v1", "package-body-v2"),
                    "schedule" => original.Replace("\"afternoonStartMinute\":780", "\"afternoonStartMinute\":790"),
                    "settings" => original.Replace("\"maxRequestsPerMinute\":16", "\"maxRequestsPerMinute\":8"),
                    "profile" => original.Replace("A patient farmer who explains crops in simple terms.", "A farmer with another persona."),
                    "plan" => original.Replace("\"maxTurns\":4", "\"maxTurns\":6"),
                    "seed" => original.Replace("\"worldSeed\":12345", "\"worldSeed\":12346"),
                    _ => throw new ArgumentOutOfRangeException(nameof(damage)) };
                Assert.That(changed, Is.Not.EqualTo(original));
                File.WriteAllText(path, changed);
                Assert.That(new JsonFileSaveStorage(path).Load("main").IsSuccess, Is.True,
                    "The foreign frame remains a structurally valid save.");
            }

            var loaded = AgentExperimentPackage.Read(_directory);
            Assert.That(loaded.IsSuccess, Is.False, damage);
            Assert.That(loaded.ErrorCode, Is.EqualTo("experiment.configuration_mismatch"));
        }

        [TestCase("schema")]
        [TestCase("trace-schema")]
        [TestCase("speech")]
        [TestCase("source-revision")]
        [TestCase("scenario")]
        [TestCase("run-mode")]
        [TestCase("experiment-id")]
        [TestCase("replay-origin")]
        [TestCase("final")]
        [TestCase("trace-incomplete")]
        [TestCase("legacy-initial")]
        [TestCase("checkpoint-configuration")]
        public void Export_RejectsUnusableRecordingBeforeCreatingFiles(string damage)
        {
            var manifest = Manifest();
            var initial = Snapshot(720);
            var final = Snapshot(725);
            var checkpoint = Snapshot(722);
            if (damage == "schema") manifest.schemaVersion = 2;
            if (damage == "trace-schema") manifest.trace.schemaVersion = 2;
            if (damage == "speech") manifest.speechMode = "F";
            if (damage == "source-revision") manifest.sourceRevision = "";
            if (damage == "scenario") manifest.scenarioId = "unregistered";
            if (damage == "run-mode") manifest.runMode = "unregistered";
            if (damage == "experiment-id") manifest.experimentId = "";
            if (damage == "replay-origin") manifest.runMode = "replay";
            if (damage == "final") final = null;
            if (damage == "trace-incomplete") manifest.trace.completed = false;
            if (damage == "legacy-initial") initial = new GameSaveSnapshot(3, initial.WorldSeed, initial.Clock,
                initial.Characters, initial.Shops, initial.Farm, initial.Livestock);
            if (damage == "checkpoint-configuration")
            {
                var world = checkpoint.CompleteWorld;
                var changed = new CompleteWorldSnapshot("other-content", world.BodyConfiguration, world.World,
                    world.MeetingsEnabled, world.Meetings, world.DecisionsEnabled, world.Decisions, world.Residents, world.Player);
                checkpoint = new GameSaveSnapshot(4, checkpoint.WorldSeed, checkpoint.Clock, checkpoint.Characters,
                    checkpoint.Shops, checkpoint.Farm, checkpoint.Livestock, checkpoint.FractionalMinute, changed);
            }

            var saved = AgentExperimentPackage.Export(_directory, manifest, initial, final,
                new Dictionary<string, GameSaveSnapshot> { { "middle", checkpoint } });
            Assert.That(saved.IsSuccess, Is.False, damage);
            Assert.That(Directory.Exists(_directory), Is.False, "Rejected recording must not publish files.");
        }

        [TestCase("sourceRevision", "")]
        [TestCase("scenarioId", "unknown")]
        [TestCase("runMode", "unknown")]
        [TestCase("experimentId", "")]
        [TestCase("speechMode", "StructuredFacts")]
        public void Read_RejectsInvalidRecordingIdentity(string field, string value)
        {
            Assert.That(AgentExperimentPackage.Export(_directory, Manifest(), Snapshot(720), Snapshot(725)).IsSuccess, Is.True);
            string path = Path.Combine(_directory, "manifest.json");
            string json = File.ReadAllText(path);
            string damaged = new Regex("\\\"" + field + "\\\"\\s*:\\s*\\\"[^\\\"]*\\\"")
                .Replace(json, "\"" + field + "\":\"" + value + "\"", 1);
            Assert.That(damaged, Is.Not.EqualTo(json));
            File.WriteAllText(path, damaged);
            Assert.That(AgentExperimentPackage.Read(_directory).IsSuccess, Is.False);
        }

        [TestCase("../outside")]
        [TestCase("..\\outside")]
        [TestCase("C:/outside")]
        [TestCase("A:B")]
        public void CheckpointLabels_CannotResolveFilesOutsideThePackage(string label)
        {
            var initial = Snapshot(720);
            var failed = AgentExperimentPackage.Export(_directory, Manifest(), initial, initial,
                new Dictionary<string, GameSaveSnapshot> { { label, initial } });
            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(Directory.Exists(_directory), Is.False);
            Assert.That(AgentExperimentPackage.Export(_directory, Manifest(), initial, initial).IsSuccess, Is.True);
            string path = Path.Combine(_directory, "manifest.json");
            var manifest = JsonUtility.FromJson<AgentExperimentManifest>(File.ReadAllText(path));
            manifest.checkpointLabels.Add(label);
            File.WriteAllText(path, JsonUtility.ToJson(manifest));
            var read = AgentExperimentPackage.Read(_directory);
            Assert.That(read.IsSuccess, Is.False);
            Assert.That(read.ErrorCode, Is.EqualTo("experiment.checkpoint_invalid"));
        }

        [TestCase("inputs")]
        [TestCase("results")]
        [TestCase("ticks")]
        [TestCase("calls")]
        [TestCase("unknown-kind")]
        public void Read_RejectsNullRecordsAndUnknownInputsEvenForEvidence(string damage)
        {
            Assert.That(AgentExperimentPackage.Export(_directory, Manifest(), Snapshot(720), Snapshot(725)).IsSuccess, Is.True);
            string path = Path.Combine(_directory, "manifest.json");
            string json = File.ReadAllText(path);
            if (damage == "unknown-kind")
            {
                var manifest = JsonUtility.FromJson<AgentExperimentManifest>(json);
                manifest.inputs.Add(new AgentExperimentInput { kind = "execute_external_command" });
                File.WriteAllText(path, JsonUtility.ToJson(manifest));
            }
            else
            {
                string changed = new Regex("\\\"" + damage + "\\\"\\s*:\\s*\\[\\s*\\]")
                    .Replace(json, "\"" + damage + "\":[null]", 1);
                Assert.That(changed, Is.Not.EqualTo(json));
                File.WriteAllText(path, changed);
            }
            Assert.That(AgentExperimentPackage.Read(_directory, forReplay: false).IsSuccess, Is.False);
        }

        [Test]
        public void Export_ExistingPackagePreservesTheOriginalEvidence()
        {
            var snapshot = Snapshot(720);
            Assert.That(AgentExperimentPackage.Export(_directory, Manifest(), snapshot, snapshot).IsSuccess, Is.True);
            var files = Directory.GetFiles(_directory).ToDictionary(Path.GetFileName, File.ReadAllText);
            var another = Manifest();
            another.experimentId = "replacement";
            var saved = AgentExperimentPackage.Export(_directory, another, snapshot, Snapshot(725));
            Assert.That(saved.IsSuccess, Is.False);
            Assert.That(saved.ErrorCode, Is.EqualTo("experiment.directory_exists"));
            foreach (var file in files) Assert.That(File.ReadAllText(Path.Combine(_directory, file.Key)), Is.EqualTo(file.Value));
        }

        private static AgentExperimentManifest Manifest() => new AgentExperimentManifest {
            experimentId = "experiment-one", pairId = "pair-one", armOrder = new[] { "F", "S" },
            scenarioId = "available", speechMode = "S", runMode = "fixed", configuration = "package-fixture-v1",
            sourceRevision = "unknown", completed = true,
            trace = new DecisionTrace { configuration = "package-fixture-v1", clientConfiguration = "fixed-four-resident-experiment/v1", completed = true } };

        private static GameSaveSnapshot Snapshot(int minute, int playerCredit = 0)
        {
            var configuration = AgentExperimentContent.CreateConfiguration();
            var services = CozyTownCompositionRoot.Create(configuration);
            services.WorldTime.AdvanceMinutes(minute - 720);
            if (playerCredit > 0) services.Wallet.Credit(playerCredit);
            var ids = configuration.Npcs.Select(npc => npc.Id).ToArray();
            var world = new NpcAgentWorld(ids.Select(id => new NpcDailySchedule(id, id + ".home", id + ".outside",
                id + ".entry", id + ".work", id + ".rest", id + ".work", 360, 480, 720, 780, 1020, 1080)));
            world.Observe(services.WorldTimeFlow.Current);
            var meetings = new NpcMeetingBoard(world, new[] {
                new NpcMeetingPlan("package-social", ids[0], ids[1], "main-road", ids[0] + ".rest", ids[1] + ".rest",
                    720, 740, 745, 60) });
            using var scheduler = new NpcDecisionScheduler(world, configuration.Npcs, new FixedExperimentDecisionClient(),
                new NpcDecisionSettings(maxRequestsPerMinute: 16, maxConcurrentRequests: 2), meetings, speechMode: NpcSpeechMode.StructuredFacts);
            var bodies = ids.Select((id, index) => new NpcBodySnapshot(id,
                new TownRouteSnapshot(new Position2DSnapshot(index, 0), new Position2DSnapshot(0, 1), id + ".rest",
                    new[] { new Position2DSnapshot(index, 0) }, 1, 1, false), NpcActivity.Resting, false)).ToArray();
            var complete = new CompleteWorldSnapshot("package-content-v1", "package-body-v1", world.CaptureSnapshot(),
                true, meetings.CaptureSnapshot(), true, scheduler.CaptureSnapshot(0), bodies,
                new PlayerBodySnapshot(new Position2DSnapshot(-3, -3), new Position2DSnapshot(0, -1)));
            var economy = services.EconomyState.CaptureSnapshot();
            return new GameSaveSnapshot(4, services.WorldSeed.Value, services.Time.Current, economy.Characters,
                economy.Shops, services.Farm.CaptureSnapshot(), services.Livestock.CaptureSnapshot(), 0, complete);
        }
    }
}
