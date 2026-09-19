#if UNITY_EDITOR
using System.Collections;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Core;
using CozyTown.Unity.Experiments;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class AgentExperimentSessionPlayModeTests
    {
        private Scene _scene;
        private string _previousProxyEndpoint;

        [SetUp]
        public void SetUp()
        {
            _previousProxyEndpoint = Environment.GetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable);
            Environment.SetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable, null);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            Environment.SetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable, _previousProxyEndpoint);
        }

        [UnityTest]
        public IEnumerator FailedInitialSnapshot_StopsTheExperimentBeforeAnyDispatch()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var session = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration(), null, new FailSecondSaveStorage()), "available");

            Assert.Throws<InvalidOperationException>(() => session.Start(NpcSpeechMode.FreeText, "fixed", new FixedExperimentDecisionClient()));

            Assert.That(session.Divergence, Is.Not.Null);
            Assert.That(session.Divergence, Does.Contain("save.write_failed"));
            Assert.Throws<InvalidOperationException>(() => session.Step(0.5, 0));
            Assert.Throws<InvalidOperationException>(() => session.Complete());
            Assert.That(session.Controller.DecisionRequestsStarted, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RejectedTimeAdvance_DoesNotContaminateTheCompletedReplay()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var source = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            source.Start(NpcSpeechMode.FreeText, "fixed", new FixedExperimentDecisionClient());

            Assert.Throws<InvalidOperationException>(() => source.Step(double.MaxValue, 1));

            Assert.That(source.Tick, Is.Zero);
            Assert.That(source.Controller.GameTotalMinutes, Is.EqualTo(720));
            for (int tick = 0; tick < 160; tick++) source.Step(0.5, 1 + tick * 0.5);
            source.Complete();
            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-platform", "packages", Guid.NewGuid().ToString("N")));
            source.Export(directory);
            var package = AgentExperimentPackage.Read(directory);
            Assert.That(package.IsSuccess, Is.True, package.ErrorCode);
            Assert.That(package.Value.Manifest.inputs.Count(input => input.kind == "advance"), Is.EqualTo(160));
            Assert.That(package.Value.Manifest.inputs.Where(input => input.kind == "advance").Select(input => input.elapsedGameSeconds), Is.All.EqualTo(0.5));
            yield return SceneManager.UnloadSceneAsync(_scene);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var replay = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            replay.StartReplay(directory);
            while (replay.ReplayNext()) { }
            Assert.That(replay.Completed, Is.True);
        }

        private sealed class FailSecondSaveStorage : ISaveStorage
        {
            private readonly InMemorySaveStorage _inner = new InMemorySaveStorage();
            private int _saves;
            public bool Exists(string slotId) => _inner.Exists(slotId);
            public OperationResult<GameSaveSnapshot> Load(string slotId) => _inner.Load(slotId);
            public OperationResult Save(string slotId, GameSaveSnapshot snapshot) => ++_saves == 2
                ? OperationResult.Failure("save.write_failed") : _inner.Save(slotId, snapshot);
        }

        [UnityTest]
        public IEnumerator Telemetry_ExportsMatchedAnnotationsWithoutChangingAuthoritativeReplay()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var session = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            session.Start(NpcSpeechMode.FreeText, "live", new FixedExperimentDecisionClient());
            for (int tick = 0; tick < 160; tick++) session.Step(0.5, tick * 0.5);
            session.Complete();
            var call = session.TraceClient.Trace.calls[0];
            var request = UnityEngine.JsonUtility.FromJson<RequestOwner>(call.requestJson);
            using var http = new HttpClient(new MeasurementsHandler(request));
            var capture = session.RefreshMeasurementsAsync("http://127.0.0.1:8765/decide", http);
            while (!capture.IsCompleted) yield return null;
            Assert.That(capture.IsFaulted, Is.False, capture.Exception?.ToString());
            Assert.That(session.MeasurementsJson, Does.Contain("proxy_shared"));
            var report = session.CaptureTraceReport();
            Assert.That(report.calls[0].rawResponseJson, Is.EqualTo("recorded candidate"));
            Assert.That(session.TraceClient.Trace.calls[0].rawResponseJson, Is.Null);
            report.calls[0].rawResponseJson = "mutated report";
            Assert.That(session.CaptureTraceReport().calls[0].rawResponseJson, Is.EqualTo("recorded candidate"));
            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-platform", "packages", Guid.NewGuid().ToString("N")));
            session.Export(directory);
            var package = AgentExperimentPackage.Read(directory);
            Assert.That(package.IsSuccess, Is.True, package.ErrorCode);
            Assert.That(package.Value.Manifest.measurementsJson, Is.EqualTo(session.MeasurementsJson));
            Assert.That(package.Value.Manifest.trace.calls[0].rawResponseJson, Is.EqualTo("recorded candidate"));
            Assert.That(package.Value.Manifest.trace.calls[0].responseJson, Is.EqualTo(call.responseJson));
        }

        [Serializable] private sealed class RequestOwner { public string decisionId, npcId; public int step; }
        private sealed class MeasurementsHandler : HttpMessageHandler
        {
            private readonly RequestOwner _request;
            public MeasurementsHandler(RequestOwner request) => _request = request;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
                string body = request.RequestUri.AbsolutePath == "/status"
                    ? "{\"requestedModel\":\"deepseek-v4-flash\",\"attemptedProviderCalls\":1,\"remainingCalls\":0,\"inflight\":0}"
                    : "{\"measurements\":[{\"call\":1,\"npcId\":\"" + _request.npcId + "\",\"decisionId\":\"" + _request.decisionId
                      + "\",\"step\":" + _request.step + ",\"status\":\"passed\",\"requestedModel\":\"deepseek-v4-flash\","
                      + "\"rawCandidate\":\"recorded candidate\",\"rawCandidateTruncated\":false,\"elapsedMilliseconds\":12}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            }
        }

        [UnityTest]
        public IEnumerator FreshScene_PreparesFourBodiesAndAssetsWithoutDispatchBeforeExplicitSelection()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var services = CozyTownCompositionRoot.CreateDefault();

            var session = AgentExperimentSession.Attach(_scene, services, "available");

            Assert.That(session.InitialSnapshot.SchemaVersion, Is.EqualTo(4));
            Assert.That(session.InitialSnapshot.CompleteWorld.Residents.Select(body => body.NpcId),
                Is.EquivalentTo(DefaultMvpContent.CreateConfiguration().Npcs.Select(profile => profile.Id)));
            Assert.That(session.Controller.DecisionsEnabled, Is.False);
            Assert.That(session.Controller.DecisionRequestsStarted, Is.Zero);
            Assert.That(session.InitialSnapshot.CompleteWorld.MeetingsEnabled, Is.False);
            Assert.That(session.InitialSnapshot.Characters.Count, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator SleepInput_CrossesTheDayBoundaryAndReplaysTheSameSettledWorld()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var source = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            source.Start(NpcSpeechMode.FreeText, "fixed", new FixedExperimentDecisionClient());
            for (int tick = 0; tick < 160; tick++) source.Step(0.5, tick * 0.5);

            source.Sleep(720);
            source.Sleep(720);
            source.Complete();

            Assert.That(source.Services.Time.Current.Day, Is.EqualTo(2));
            Assert.That(source.Services.Time.Current.MinuteOfDay, Is.EqualTo(880));
            Assert.That(source.Services.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-platform", "packages", Guid.NewGuid().ToString("N")));
            source.Export(directory);
            yield return SceneManager.UnloadSceneAsync(_scene);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var replay = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            replay.StartReplay(directory);
            while (replay.ReplayNext()) { }
            Assert.That(replay.Completed, Is.True);
            Assert.That(replay.Services.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Replay_StopsWhenCheckpointAssetsDifferFromTheRecordedSaveInput()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var source = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            source.Start(NpcSpeechMode.FreeText, "fixed", new FixedExperimentDecisionClient());
            for (int tick = 0; tick < 160; tick++)
            {
                source.Step(0.5, tick * 0.5);
                if (tick == 49) source.Save("before-delivery");
            }
            source.Complete();
            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-platform", "packages", Guid.NewGuid().ToString("N")));
            source.Export(directory);
            var checkpointStorage = new CozyTown.Runtime.Save.JsonFileSaveStorage(Path.Combine(directory, "checkpoint-before-delivery.json"));
            var saved = checkpointStorage.Load("main").Value;
            var changedCharacters = saved.Characters.Select(character => character.CharacterId != DefaultMvpIds.Npcs.Cook ? character
                : new CozyTown.Runtime.Economy.CharacterEconomySnapshot(character.CharacterId, character.Backpack,
                    new CozyTown.Runtime.Economy.WalletSnapshot(character.Wallet.Balance + 1))).ToArray();
            var changed = new CozyTown.Runtime.Save.GameSaveSnapshot(saved.SchemaVersion, saved.WorldSeed, saved.Clock,
                changedCharacters, saved.Shops, saved.Farm, saved.Livestock, saved.FractionalMinute, saved.CompleteWorld, saved.SourceSchemaVersion);
            Assert.That(checkpointStorage.Save("main", changed).IsSuccess, Is.True);
            yield return SceneManager.UnloadSceneAsync(_scene);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var replay = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            replay.StartReplay(directory);

            Assert.Throws<InvalidOperationException>(() => { while (replay.ReplayNext()) { } });

            Assert.That(replay.Completed, Is.False);
            Assert.That(replay.Divergence, Does.Contain("checkpoint"));
        }

        [UnityTest]
        public IEnumerator RecordedStructuredWorld_ReplaysSavedInputsAndDeliveryInAFreshScene()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var source = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            source.ConfigureIdentity("test-build", evaluationPlanReference: "structured-replay-plan@1");
            source.Start(NpcSpeechMode.StructuredFacts, "fixed", new FixedExperimentDecisionClient());
            for (int tick = 0; tick < 360; tick++)
            {
                source.Step(0.5, tick * 0.5);
                if (tick == 49) source.Save("before-delivery");
                if (tick == 139) source.Load("before-delivery");
            }
            source.Complete();
            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-platform", "packages", Guid.NewGuid().ToString("N")));
            source.Export(directory);
            var codes = source.Outcomes.Select(result => result.Code).ToArray();
            var oldWorld = source.Controller.GetAgentState(DefaultMvpIds.Npcs.Cook).WorldRunId;
            yield return SceneManager.UnloadSceneAsync(_scene);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var replay = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");

            replay.StartReplay(directory);
            int inputs = 0;
            while (replay.ReplayNext()) Assert.That(++inputs, Is.LessThan(1000));

            Assert.That(replay.Completed, Is.True);
            Assert.That(replay.RunMode, Is.EqualTo("replay"));
            Assert.That(replay.Controller.GetAgentState(DefaultMvpIds.Npcs.Cook).WorldRunId, Is.Not.EqualTo(oldWorld));
            Assert.That(replay.Outcomes.Select(result => result.Code), Is.EqualTo(codes));
            replay.Services.EconomyState.TryGetCharacter(DefaultMvpIds.Npcs.Cook, out var sora);
            Assert.That(sora.Wallet.Balance, Is.EqualTo(25));
            Assert.That(sora.Backpack.Items.Single(item => item.ItemId == DefaultMvpIds.Items.Carp).Quantity, Is.EqualTo(1));
            string replayDirectory = directory + "-replay";
            replay.Export(replayDirectory);
            var replayPackage = AgentExperimentPackage.Read(replayDirectory);
            Assert.That(replayPackage.IsSuccess, Is.True, replayPackage.ErrorCode);
            Assert.That(replayPackage.Value.Manifest.evaluationPlanReference, Is.EqualTo("structured-replay-plan@1"));
            Assert.That(replayPackage.Value.Manifest.expressionContractVersion, Is.EqualTo("1.0"));
        }

        [UnityTest]
        public IEnumerator RecordedExperiment_ExportsFullStateTimelineAndObservedExecution()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var session = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            session.ConfigureIdentity("test-build", evaluationPlanReference: "evaluation-plan-fixture@1");
            session.Start(NpcSpeechMode.FreeText, "fixed", new FixedExperimentDecisionClient());
            for (int tick = 0; tick < 360; tick++) session.Step(0.5, tick * 0.5);
            session.Complete();
            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-platform", "packages", Guid.NewGuid().ToString("N")));

            session.Export(directory);

            var loaded = AgentExperimentPackage.Read(directory);
            Assert.That(loaded.IsSuccess, Is.True, loaded.ErrorCode);
            var manifest = loaded.Value.Manifest;
            Assert.That(manifest.experimentId, Is.EqualTo(session.ExperimentId));
            Assert.That(manifest.expressionContractVersion, Is.EqualTo("1.0"));
            Assert.That(manifest.evaluationPlanReference, Is.EqualTo("evaluation-plan-fixture@1"));
            Assert.That(manifest.speechMode, Is.EqualTo("F"));
            Assert.That(manifest.runMode, Is.EqualTo("fixed"));
            Assert.That(manifest.inputs.Count(input => input.kind == "advance"), Is.EqualTo(360));
            Assert.That(manifest.inputs.Last().kind, Is.EqualTo("complete"));
            Assert.That(manifest.trace.ticks.Count, Is.EqualTo(360));
            Assert.That(manifest.trace.calls.Count, Is.EqualTo(session.Controller.DecisionRequestsStarted));
            Assert.That(manifest.results.Count, Is.EqualTo(session.Outcomes.Count));
            var delivery = manifest.results.Single(result => result.code == "meeting.deliver");
            Assert.That(delivery.requestJson, Does.Contain("\"decisionId\":\"" + delivery.decisionId + "\""));
            Assert.That(delivery.executionObservationJson, Does.Contain("observedAtGameTotalMinutes"));
            Assert.That(loaded.Value.Initial.Clock.MinuteOfDay, Is.EqualTo(720));
            Assert.That(loaded.Value.Final.Clock.MinuteOfDay, Is.EqualTo(1080));
            Assert.Throws<InvalidOperationException>(() => session.Step(0.5, 180));
            TestContext.WriteLine("Experiment package: " + directory);
        }

        [UnityTest]
        public IEnumerator SavedExperiment_RestoresEarlierWorldWithoutRefundingRequestsOrKeepingFutureReceipts()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var services = CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration());
            var session = AgentExperimentSession.Attach(_scene, services, "available");
            session.Start(NpcSpeechMode.FreeText, "fixed", new FixedExperimentDecisionClient());
            for (int tick = 0; tick < 50; tick++) session.Step(0.5, tick * 0.5);
            var before = session.Controller.GetAgentState(DefaultMvpIds.Npcs.Cook).WorldRunId;
            session.Save("before-delivery");
            for (int tick = 50; tick < 140; tick++) session.Step(0.5, tick * 0.5);
            long requests = session.Controller.DecisionRequestsStarted;
            Assert.That(session.Controller.GetMeeting(DefaultMvpIds.Npcs.Cook).DeliveryResultCode, Is.EqualTo("resource.delivered"));

            session.Load("before-delivery");

            Assert.That(session.Controller.GameTotalMinutes, Is.EqualTo(770));
            Assert.That(session.Controller.GetAgentState(DefaultMvpIds.Npcs.Cook).WorldRunId, Is.Not.EqualTo(before));
            Assert.That(session.Controller.DecisionRequestsStarted, Is.EqualTo(requests));
            Assert.That(session.RealSeconds, Is.EqualTo(69.5));
            Assert.That(session.Controller.GetMeeting(DefaultMvpIds.Npcs.Cook).DeliveryResultCode, Is.Null);
            services.EconomyState.TryGetCharacter(DefaultMvpIds.Npcs.Cook, out var sora);
            Assert.That(sora.Wallet.Balance, Is.EqualTo(50));
            Assert.That(sora.Backpack.Items.Any(item => item.ItemId == DefaultMvpIds.Items.Carp), Is.False);
        }

        [UnityTest]
        public IEnumerator FixedRun_CompletesBothMeetingsAndRecordsAllFourResidents()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var services = CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration());
            var session = AgentExperimentSession.Attach(_scene, services, "available");

            session.Start(NpcSpeechMode.FreeText, "fixed", new FixedExperimentDecisionClient());
            for (int tick = 0; tick < 360; tick++) session.Step(0.5, tick * 0.5);

            var ids = DefaultMvpContent.CreateConfiguration().Npcs.Select(profile => profile.Id).ToArray();
            Assert.That(ids.Select(id => session.Controller.GetMeeting(id)?.State),
                Is.All.EqualTo(NpcMeetingState.Completed), string.Join("\n", session.Outcomes.Select(result =>
                    result.NpcId + " " + result.Context?.GameTotalMinutes + " " + result.Code)));
            Assert.That(session.Outcomes.Select(result => result.NpcId).Distinct(), Is.EquivalentTo(ids));
            Assert.That(ids.All(id => session.Controller.GetAgentState(id).ActiveActivity == null), Is.True);
            Assert.That(ids.All(id => session.Controller.GetMeetingMemories(id).Count > 0), Is.True);
            services.EconomyState.TryGetCharacter(DefaultMvpIds.Npcs.Fisher, out var ren);
            services.EconomyState.TryGetCharacter(DefaultMvpIds.Npcs.Cook, out var sora);
            Assert.That(ren.Wallet.Balance, Is.EqualTo(25));
            Assert.That(sora.Wallet.Balance, Is.EqualTo(25));
            Assert.That(ren.Backpack.Items.Single(item => item.ItemId == DefaultMvpIds.Items.Carp).Quantity, Is.EqualTo(1));
            Assert.That(sora.Backpack.Items.Single(item => item.ItemId == DefaultMvpIds.Items.Carp).Quantity, Is.EqualTo(1));
        }
    }
}
#endif
