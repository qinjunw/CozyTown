#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Core;
using CozyTown.Unity.Experiments;
using CozyTown.Unity.Npc;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class AgentIntegrationMatrixPlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private static readonly string[] Scenarios = { "available", "seller_empty", "buyer_poor", "need_satisfied" };
        private Scene _scene;
        private string _previousProxy;
        private readonly Dictionary<string, InitialArm> _initialArms = new Dictionary<string, InitialArm>();
        private readonly HashSet<Guid> _worldIds = new HashSet<Guid>();
        private sealed class InitialArm
        {
            internal GameSaveSnapshot Snapshot;
            internal string Mode, WorldId;
            internal DecisionTrace Trace;
        }

        [Serializable]
        private sealed class ActiveWorld { public int schemaVersion, ordinal; public string optionsPath, state; }
        [Serializable]
        private sealed class WorldOptions
        {
            public int schemaVersion = 1, ordinal, repetition = 1, maxProviderCalls = 32;
            public double maxRealSeconds = 600;
            public string studyId, worldId, pairId, scenarioId, speechMode, runMode, stage, sourceRevision,
                evaluationPlanReference, proxyEndpoint, clientConfigurationJson, outputDirectory;
            public string scriptVersion = "cross-day-v1";
            public string[] armOrder;
        }
        [Serializable]
        private sealed class Initialization
        {
            public int schemaVersion = 1;
            public string worldId, initialSnapshotPath, reason;
            public bool initialized;
        }
        [Serializable]
        private sealed class WorldResult
        {
            public int schemaVersion = 1;
            public string worldId, status = "initialization_failed", packageDirectory, reason, telemetryError;
            public string startedAtUtc, cleanupStartedAtUtc, finishedAtUtc;
            public bool initialized, replayPassed;
            public double completedGameMinutes, realSeconds;
            public long hostRequests;
            public List<string> hostViolations = new List<string>();
        }
        [Serializable]
        private sealed class Progress
        {
            public int tick, inFlight, waitingResidents;
            public string worldId, worldRunId, recordedAtUtc;
            public double gameMinutes, realSeconds;
            public long requests;
            public ResidentProgress[] residents;
        }
        [Serializable]
        private sealed class ResidentProgress
        {
            public string npcId, lastResult, meetingState, target, bodyStatus, activity;
            public bool hasCurrent, hasPending, hasWaitingTurn, hasActivity;
            public double remainingCooldown, activityExpires;
            public string[] currentTriggers, pendingTriggers;
        }
        [Serializable]
        private sealed class SpeechReport
        {
            public int schemaVersion = 1, acceptedSpeechOutcomes;
            public string experimentId;
            public List<AgentExperimentSpeechEvidence> rows = new List<AgentExperimentSpeechEvidence>();
        }
        [Serializable]
        private sealed class PairContextReport
        {
            public int schemaVersion = 1;
            public string pairId, freeTextWorldId, structuredFactsWorldId;
            public bool comparedRecordedPrefix;
            public AgentExperimentContextDivergence firstDivergence;
        }

        [SetUp]
        public void SetUp()
        {
            _previousProxy = Environment.GetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable);
            Environment.SetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable, null);
            _initialArms.Clear();
            _worldIds.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            Environment.SetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable, _previousProxy);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator FixedScript_RunsBothArmsOfAllResourceCasesAcrossDaysAndReplaysEachWorld()
        {
            string root = Path.GetFullPath(Path.Combine("Logs", "agent-platform", "integration-fixed-" + Guid.NewGuid().ToString("N")));
            var results = new List<WorldResult>();
            int ordinal = 0;
            foreach (string scenario in Scenarios)
            foreach (string arm in new[] { "F", "S" })
            {
                var options = new WorldOptions { studyId = "fixed-fixture", ordinal = ++ordinal,
                    worldId = scenario + "-" + arm, pairId = scenario, scenarioId = scenario, speechMode = arm,
                    armOrder = new[] { "F", "S" }, runMode = "fixed", stage = "fixed", sourceRevision = "fixed-fixture",
                    evaluationPlanReference = "fixture:cross-day-v1", outputDirectory = Path.Combine(root, scenario + "-" + arm) };
                yield return RunWorld(options, false, result => results.Add(result));
                Assert.That(results.Last().status, Is.EqualTo("completed"), results.Last().reason);
                Assert.That(results.Last().hostViolations, Is.Empty);
                Assert.That(results.Last().replayPassed, Is.True, results.Last().reason);
                Assert.That(results.Last().completedGameMinutes, Is.EqualTo(2400));
            }
            Assert.That(results.Count, Is.EqualTo(8));
            Assert.That(_worldIds.Count, Is.EqualTo(8));
            TestContext.WriteLine("Fixed integration evidence: " + root);
        }

        [UnityTest]
        [Category("ExternalProvider")]
        [Timeout(12800000)]
        public IEnumerator CoordinatedMatrix_ExecutesOnlyRegisteredFreshWorlds()
        {
            string study = Environment.GetEnvironmentVariable("COZYTOWN_INTEGRATION_STUDY_DIRECTORY");
            if (Environment.GetEnvironmentVariable("COZYTOWN_RUN_FOUR_AGENT_INTEGRATION") != "1" || string.IsNullOrWhiteSpace(study))
                Assert.Ignore("Requires an explicit registered integration study and its capped local coordinator.");
            string activePath = Path.Combine(Path.GetFullPath(study), "active-world.json");
            string previous = null;
            int worlds = 0;
            var idle = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                Assert.That(idle.Elapsed.TotalSeconds, Is.LessThan(180), "The coordinator did not publish the next registered world.");
                if (!File.Exists(activePath)) { yield return null; continue; }
                string activeJson = ReadShared(activePath);
                if (activeJson == null) { yield return null; continue; }
                var active = JsonUtility.FromJson<ActiveWorld>(activeJson);
                Assert.That(active.schemaVersion, Is.EqualTo(1));
                if (active.state == "finished") break;
                Assert.That(active.state, Is.Not.EqualTo("aborted"), "The coordinator aborted the study; retain its ledger and results.");
                if (active.state != "ready" || active.optionsPath == previous) { yield return null; continue; }
                var options = JsonUtility.FromJson<WorldOptions>(File.ReadAllText(active.optionsPath));
                Assert.That(options.ordinal, Is.EqualTo(++worlds));
                Assert.That(active.ordinal, Is.EqualTo(worlds));
                Assert.That(worlds, Is.LessThanOrEqualTo(16));
                previous = active.optionsPath;
                WorldResult result = null;
                yield return RunWorld(options, true, value => result = value);
                Assert.That(result.status, Is.Not.EqualTo("initialization_failed").And.Not.EqualTo("host_failure"), result.reason);
                idle.Restart();
            }
            Assert.That(worlds, Is.EqualTo(16));
        }

        private IEnumerator RunWorld(WorldOptions options, bool coordinated, Action<WorldResult> completed)
        {
            Directory.CreateDirectory(options.outputDirectory);
            var result = new WorldResult { worldId = options.worldId };
            var clock = new System.Diagnostics.Stopwatch();
            AgentExperimentSession session = null;
            var speech = new SpeechReport();
            double fixedSeconds = 0;
            bool live = options.stage == "live";
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                _scene = SceneManager.GetSceneByPath(ScenePath);
                var initialization = new Initialization { worldId = options.worldId,
                    initialSnapshotPath = Path.Combine(options.outputDirectory, "initial.json") };
                try
                {
                    if (options.schemaVersion != 1 || options.scriptVersion != "cross-day-v1"
                        || !Scenarios.Contains(options.scenarioId) || (options.speechMode != "F" && options.speechMode != "S")
                        || (options.stage != "fixed" && !live) || options.runMode != options.stage
                        || options.maxRealSeconds != 600 || options.maxProviderCalls != 32)
                        throw new InvalidOperationException("The options do not match the registered integration contract.");
                    session = AgentExperimentSession.Attach(_scene,
                        CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration(options.scenarioId)), options.scenarioId);
                    session.ConfigureIdentity(options.sourceRevision, options.pairId, options.armOrder, options.evaluationPlanReference);
                    INpcDecisionClient client = live
                        ? new ProxyNpcDecisionClient(options.proxyEndpoint, snapshotConfiguration: options.clientConfigurationJson)
                        : new FixedExperimentDecisionClient();
                    session.Start(options.speechMode == "F" ? NpcSpeechMode.FreeText : NpcSpeechMode.StructuredFacts,
                        options.runMode, client, () => live ? clock.Elapsed.TotalSeconds : fixedSeconds);
                    speech.experimentId = session.ExperimentId;
                    VerifyInitial(options, session);
                    var saved = new JsonFileSaveStorage(initialization.initialSnapshotPath).Save("main", session.InitialSnapshot);
                    if (!saved.IsSuccess) throw new InvalidOperationException(saved.ErrorCode);
                    initialization.initialized = result.initialized = true;
                }
                catch (Exception exception) { initialization.reason = result.reason = exception.Message; }
                WriteNew(Path.Combine(options.outputDirectory, "initialized.json"), initialization);
                if (!initialization.initialized) yield break;
                if (coordinated)
                {
                    var handshake = System.Diagnostics.Stopwatch.StartNew();
                    while (!File.Exists(Path.Combine(options.outputDirectory, "start.json")))
                    {
                        if (File.Exists(Path.Combine(options.outputDirectory, "stop.json")) || handshake.Elapsed.TotalSeconds > 120)
                        { result.reason = "The coordinator did not authorize the first tick."; yield break; }
                        yield return null;
                    }
                }
                result.status = "incomplete";
                result.startedAtUtc = DateTime.UtcNow.ToString("O");
                clock.Start();
                session.Save("origin");
                RecordProgress(options, session);
                int phase = 0;
                double nextTick = 0.5;
                long previousCalls = 0;
                int coins = TotalNpcCoins(session), fish = TotalNpcFish(session);
                while (true)
                {
                    double now = live ? clock.Elapsed.TotalSeconds : fixedSeconds;
                    if (now >= options.maxRealSeconds || (coordinated && File.Exists(Path.Combine(options.outputDirectory, "stop.json"))))
                    { result.reason = "The registered deadline stopped world advancement."; break; }
                    if (live && now < nextTick) { yield return null; continue; }
                    if (!live) fixedSeconds += 0.5;
                    now = live ? clock.Elapsed.TotalSeconds : fixedSeconds;
                    nextTick = now + 0.5;
                    try
                    {
                        if (phase == 0 && session.Controller.GameTotalMinutes == 960)
                        {
                            session.Save("post_day1");
                            StepAndCapture(session, speech, 0.5, live ? clock.Elapsed.TotalSeconds : now);
                            session.Load("post_day1");
                            session.Load("post_day1");
                            session.Sleep(720);
                            session.Sleep(60);
                            if (session.Services.Time.Current.Day != 2 || session.Services.Time.Current.MinuteOfDay != 300
                                || session.Services.Farm.CaptureSnapshot().LastProcessedDay != 2
                                || session.Services.Livestock.CaptureSnapshot().LastProcessedDay != 2)
                                throw new InvalidOperationException("Crossing midnight and morning did not settle the recorded world.");
                            phase = 1;
                        }
                        else if (session.Controller.GameTotalMinutes < 2400) StepAndCapture(session, speech, 0.5, now);
                        else if (session.Controller.ActiveDecisionRequests > 0) StepAndCapture(session, speech, 0, now);
                        VerifyHost(session, coins, fish, ref previousCalls);
                        RecordProgress(options, session);
                        if (phase == 1 && session.Controller.GameTotalMinutes == 2400 && session.Controller.ActiveDecisionRequests == 0)
                        {
                            session.Complete();
                            result.status = "completed";
                            break;
                        }
                    }
                    catch (Exception exception)
                    {
                        result.status = "host_failure";
                        result.reason = exception.Message;
                        result.hostViolations.Add(exception.Message);
                        break;
                    }
                    if (live) yield return null;
                }
                result.completedGameMinutes = session.Controller.GameTotalMinutes;
                result.cleanupStartedAtUtc = DateTime.UtcNow.ToString("O");
                result.realSeconds = live ? clock.Elapsed.TotalSeconds : fixedSeconds;
                result.hostRequests = session.Controller.DecisionRequestsStarted;
                speech.acceptedSpeechOutcomes = session.Outcomes.Count(outcome => outcome.Code == "meeting.say");
                if (speech.rows.Count != speech.acceptedSpeechOutcomes)
                {
                    result.status = "host_failure";
                    result.reason = "Accepted speech outcomes do not match the retained evidence rows.";
                    result.hostViolations.Add(result.reason);
                }
                WriteNew(Path.Combine(options.outputDirectory, "speech.json"), speech);
                if (live)
                {
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var capture = session.RefreshMeasurementsAsync(options.proxyEndpoint, cancellationToken: cancellation.Token);
                    while (!capture.IsCompleted) yield return null;
                    if (capture.IsFaulted || capture.IsCanceled) result.telemetryError = capture.Exception?.GetBaseException().Message ?? "Measurement capture was canceled.";
                }
                result.packageDirectory = Path.Combine(options.outputDirectory, "package");
                try
                {
                    session.Export(result.packageDirectory);
                    RecordPairContext(options, session);
                }
                catch (Exception exception)
                { result.status = "host_failure"; result.reason = exception.Message; result.hostViolations.Add("Export: " + exception.Message); }
                yield return SceneManager.UnloadSceneAsync(_scene);
                if (session.Completed && result.status == "completed")
                {
                    yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                    _scene = SceneManager.GetSceneByPath(ScenePath);
                    try
                    {
                        var replay = AgentExperimentSession.Attach(_scene,
                            CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration(options.scenarioId)), options.scenarioId);
                        replay.ConfigureIdentity(options.sourceRevision);
                        replay.StartReplay(result.packageDirectory);
                        while (replay.ReplayNext()) { }
                        replay.Export(Path.Combine(options.outputDirectory, "replay"));
                        result.replayPassed = replay.Completed;
                    }
                    catch (Exception exception)
                    { result.status = "host_failure"; result.reason = exception.Message; result.hostViolations.Add("Replay: " + exception.Message); }
                    yield return SceneManager.UnloadSceneAsync(_scene);
                }
            }
            finally
            {
                if (result.reason == null && result.status != "completed")
                {
                    result.reason = "World execution was interrupted; inspect the Unity test result.";
                    if (result.initialized) { result.status = "host_failure"; result.hostViolations.Add(result.reason); }
                }
                result.finishedAtUtc = DateTime.UtcNow.ToString("O");
                WriteNew(Path.Combine(options.outputDirectory, "result.json"), result);
                completed(result);
            }
        }

        private void VerifyInitial(WorldOptions options, AgentExperimentSession session)
        {
            var ids = AgentExperimentContent.CreateConfiguration(options.scenarioId).Npcs.Select(profile => profile.Id).ToArray();
            var bodies = _scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NpcWorldResident2D>(true)).ToArray();
            if (bodies.Length != 4 || !bodies.Select(body => body.NpcId).OrderBy(id => id).SequenceEqual(ids.OrderBy(id => id))
                || session.Services.Time.Current.Day != 1 || session.Services.Time.Current.MinuteOfDay != 720
                || session.Controller.DecisionRequestsStarted != 0 || session.Controller.ActiveDecisionRequests != 0 || session.RealSeconds != 0)
                throw new InvalidOperationException("The fresh world did not initialize all four residents and its clocks.");
            if (ids.Any(id => session.Controller.GetMeeting(id) != null || session.Controller.GetMeetingMemories(id).Count != 0
                || session.Controller.GetAgentState(id).ActiveActivity != null))
                throw new InvalidOperationException("A fresh world contains earlier meetings, activities or memories.");
            if (!_worldIds.Add(session.Controller.GetAgentState(ids[0]).WorldRunId))
                throw new InvalidOperationException("A world reused an earlier runtime identity.");
            var expected = CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration(options.scenarioId)).EconomyState.CaptureSnapshot();
            foreach (var character in expected.Characters)
            {
                if (!session.Services.EconomyState.TryGetCharacter(character.CharacterId, out var actual)
                    || actual.Wallet.Balance != character.Wallet.Balance
                    || !actual.Backpack.Items.Select(item => item.ItemId + ":" + item.Quantity)
                        .SequenceEqual(character.Backpack.Items.Select(item => item.ItemId + ":" + item.Quantity)))
                    throw new InvalidOperationException("Initial assets differ from the registered resource scenario.");
            }
            if (_initialArms.TryGetValue(options.pairId, out var first))
            {
                if (first.Mode == options.speechMode) throw new InvalidOperationException("The paired world repeats the same expression arm.");
                string difference = first.Mode == "F"
                    ? AgentExperimentPairComparison.CompareInitial(first.Snapshot, session.InitialSnapshot, 0, session.RealSeconds)
                    : AgentExperimentPairComparison.CompareInitial(session.InitialSnapshot, first.Snapshot, session.RealSeconds, 0);
                if (difference != null) throw new InvalidOperationException("Paired initialization differs: " + difference);
            }
            else _initialArms.Add(options.pairId, new InitialArm { Mode = options.speechMode,
                WorldId = options.worldId, Snapshot = session.InitialSnapshot });
        }

        private void RecordPairContext(WorldOptions options, AgentExperimentSession session)
        {
            var first = _initialArms[options.pairId];
            var current = session.CaptureTraceReport();
            if (first.Trace == null) { first.Trace = current; return; }
            bool firstIsFree = first.Mode == "F";
            var report = new PairContextReport { pairId = options.pairId, comparedRecordedPrefix = true,
                freeTextWorldId = firstIsFree ? first.WorldId : options.worldId,
                structuredFactsWorldId = firstIsFree ? options.worldId : first.WorldId,
                firstDivergence = AgentExperimentPairComparison.FirstContextDivergence(
                    firstIsFree ? first.Trace : current, firstIsFree ? current : first.Trace) };
            WriteNew(Path.Combine(options.outputDirectory, "pair-context.json"), report);
        }

        private void VerifyHost(AgentExperimentSession session, int coins, int fish, ref long previousCalls)
        {
            var controller = session.Controller;
            if (controller.DecisionRequestsStarted < previousCalls || controller.DecisionRequestsInLastMinute > 16
                || controller.ActiveDecisionRequests > 2 || TotalNpcCoins(session) != coins || TotalNpcFish(session) != fish)
                throw new InvalidOperationException("Resource conservation or the shared request budget was violated.");
            var bodies = _scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NpcWorldResident2D>(true)).ToArray();
            if (bodies.Length != 4 || bodies.Select(body => body.NpcId).Distinct().Count() != 4)
                throw new InvalidOperationException("The four-resident body set changed.");
            previousCalls = controller.DecisionRequestsStarted;
        }

        private void RecordProgress(WorldOptions options, AgentExperimentSession session)
        {
            var saved = session.Services.GameSave.Save();
            if (!saved.IsSuccess) throw new InvalidOperationException("Progress snapshot: " + saved.ErrorCode);
            var snapshot = session.Services.SaveStorage.Load("main").Value;
            var bodies = _scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NpcWorldResident2D>(true)).ToDictionary(body => body.NpcId);
            var progress = new Progress { worldId = options.worldId, tick = session.Tick,
                recordedAtUtc = DateTime.UtcNow.ToString("O"),
                worldRunId = session.Controller.GetAgentState(DefaultMvpIds.Npcs.Cook).WorldRunId.ToString("N"),
                gameMinutes = session.Controller.GameTotalMinutes, realSeconds = session.RealSeconds,
                requests = session.Controller.DecisionRequestsStarted, inFlight = session.Controller.ActiveDecisionRequests,
                waitingResidents = session.Controller.WaitingDecisionResidents,
                residents = snapshot.CompleteWorld.Decisions.Residents.Select(resident => {
                    var state = session.Controller.GetAgentState(resident.NpcId);
                    return new ResidentProgress { npcId = resident.NpcId, lastResult = resident.LastResultCode,
                        hasCurrent = resident.Current != null, hasPending = resident.Pending != null, hasWaitingTurn = resident.WaitingTurn != null,
                        remainingCooldown = resident.RemainingCooldownSeconds, target = state.Target.TargetLocationId,
                        hasActivity = state.ActiveActivity != null, activity = state.ActiveActivity?.Activity.ToString(),
                        activityExpires = state.ActiveActivity?.ExpiresAtTotalMinutes ?? 0,
                        meetingState = session.Controller.GetMeeting(resident.NpcId)?.State.ToString(), bodyStatus = bodies[resident.NpcId].Status.ToString(),
                        currentTriggers = resident.Current?.Triggers?.Select(trigger => trigger.Kind.ToString()).ToArray() ?? Array.Empty<string>(),
                        pendingTriggers = resident.Pending?.Triggers?.Select(trigger => trigger.Kind.ToString()).ToArray() ?? Array.Empty<string>() };
                }).ToArray() };
            string json = JsonUtility.ToJson(progress);
            File.AppendAllText(Path.Combine(options.outputDirectory, "timeline.jsonl"), json + "\n");
            string path = Path.Combine(options.outputDirectory, "progress.json"), temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    break;
                }
                catch (IOException) when (attempt < 9) { Thread.Sleep(10); }
            }
        }

        private static void StepAndCapture(AgentExperimentSession session, SpeechReport speech, double gameSeconds, double realSeconds)
        {
            int first = session.Outcomes.Count;
            try { session.Step(gameSeconds, realSeconds); }
            finally { speech.rows.AddRange(AgentExperimentSpeechEvidenceCollector.Capture(session, first)); }
        }

        private static string ReadShared(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) == 32 || (exception.HResult & 0xffff) == 33 || (exception.HResult & 0xffff) == 2)
            { return null; }
        }

        private static int TotalNpcCoins(AgentExperimentSession session)
            => session.Services.EconomyState.CaptureSnapshot().Characters.Where(character => character.CharacterId.StartsWith("npc.", StringComparison.Ordinal))
                .Sum(character => character.Wallet.Balance);
        private static int TotalNpcFish(AgentExperimentSession session)
            => session.Services.EconomyState.CaptureSnapshot().Characters.Where(character => character.CharacterId.StartsWith("npc.", StringComparison.Ordinal))
                .SelectMany(character => character.Backpack.Items).Where(item => item.ItemId == DefaultMvpIds.Items.Carp).Sum(item => item.Quantity);

        private static void WriteNew(string path, object value)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(value, true));
            File.Move(temporary, path);
        }
    }
}
#endif
