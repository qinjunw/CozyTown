#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
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
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class AgentIntegrationSnapshotPlayModeTests
    {
        private Scene _scene;
        private AgentExperimentSession _session;
        private string _previousProxyEndpoint;
        private double _realSeconds;

        [SetUp]
        public void SetUp()
        {
            _previousProxyEndpoint = Environment.GetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable);
            Environment.SetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable, null);
            _realSeconds = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            Environment.SetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable, _previousProxyEndpoint);
        }

        [UnityTest]
        public IEnumerator FourResidents_AfterMidnightAndMorningSettlementContinueOnTheSecondDay()
        {
            yield return LoadTown();
            AdvanceMinutes(240);
            var dayOneMeeting = _session.Controller.GetMeeting(DefaultMvpIds.Npcs.Shopkeeper);
            Assert.That(dayOneMeeting.State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(Residents().All(body => _session.Controller.GetAgentState(body.NpcId).ActiveActivity == null), Is.True);

            _session.Sleep(480);
            var midnight = Capture();
            Assert.That(midnight.Clock.Day, Is.EqualTo(2));
            Assert.That(midnight.Clock.MinuteOfDay, Is.Zero);
            AssertSettlementDay(midnight, 1);
            _session.Sleep(300);
            var morning = Capture();
            Assert.That(morning.Clock.MinuteOfDay, Is.EqualTo(300));
            AssertSettlementDay(morning, 2);
            _session.Save("morning");
            _session.Load("morning");
            _session.Load("morning");
            AssertSettlementDay(Capture(), 2);

            AdvanceMinutes(660);

            Assert.That(_session.Services.Time.Current.Day, Is.EqualTo(2));
            Assert.That(_session.Services.Time.Current.MinuteOfDay, Is.EqualTo(960));
            Assert.That(_session.Outcomes.Where(result => result.Context.GameTotalMinutes >= 1440)
                .Select(result => result.NpcId).Distinct(), Is.EquivalentTo(Residents().Select(body => body.NpcId)));
            var secondMeeting = _session.Controller.GetMeeting(DefaultMvpIds.Npcs.Shopkeeper);
            TestContext.WriteLine("Second-day outcomes:\n" + string.Join("\n", _session.Outcomes
                .Where(result => result.Context.GameTotalMinutes >= 1440).Select(result =>
                    result.NpcId + " game=" + result.Context.GameTotalMinutes + " real=" + result.StartedAtSeconds
                    + "->" + result.FinishedAtSeconds + " social=" + result.Context.Social?.Kind
                    + " reply=" + result.Reply?.Kind + " code=" + result.Code + " calls=" + result.Calls)));
            TestContext.WriteLine("Offered days: " + string.Join(",", Capture().CompleteWorld.Meetings.OfferedDays
                .Select(day => day.PlanId + "=" + day.DayStart)));
            var nextInvitation = _session.Outcomes.Single(result => result.NpcId == DefaultMvpIds.Npcs.Shopkeeper
                && result.Context.GameTotalMinutes >= 1440 && result.Context.Social?.Kind == NpcSocialContextKind.Opportunity);
            Assert.That(nextInvitation.Reply.Kind, Is.EqualTo(NpcDecisionKind.Invite));
            Assert.That(nextInvitation.Code, Is.EqualTo("agent.observation_stale"));
            Assert.That(nextInvitation.ExecutionObservation, Is.Not.Null);
            Assert.That(nextInvitation.ExecutionObservation.X != nextInvitation.Context.Observation.X
                || nextInvitation.ExecutionObservation.Y != nextInvitation.Context.Observation.Y, Is.True);
            Assert.That(Capture().CompleteWorld.Meetings.OfferedDays
                .Single(day => day.PlanId == "mina-eli-main-road-chat").DayStart, Is.EqualTo(1440));
            Assert.That(secondMeeting.Id, Is.EqualTo(dayOneMeeting.Id), "Rejecting the moving initiator's stale candidate must preserve the previous meeting result.");
            Assert.That(secondMeeting.State, Is.EqualTo(NpcMeetingState.Completed));
            foreach (var body in Residents())
            {
                var state = _session.Controller.GetAgentState(body.NpcId);
                Assert.That(state.ActiveActivity, Is.Null, body.NpcId);
                Assert.That(body.TargetLocationId, Is.EqualTo(state.Target.TargetLocationId), body.NpcId);
                Assert.That(body.Status, Is.EqualTo(TownRouteStatus.Arrived), body.NpcId);
            }
            var afternoon = Capture();
            AssertSettlementDay(afternoon, 2);
            Assert.That(afternoon.Shops.SelectMany(shop => shop.Stock.Items.Select(item => shop.ShopId + ":" + item.ItemId + ":" + item.Quantity)),
                Is.EqualTo(morning.Shops.SelectMany(shop => shop.Stock.Items.Select(item => shop.ShopId + ":" + item.ItemId + ":" + item.Quantity))));
            Assert.That(_session.Controller.GetMeetingMemories(DefaultMvpIds.Npcs.Cook)
                .Count(memory => memory.Kind == "resource.delivered"), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FourResidents_RepeatedPhaseLoadsRestoreCommitmentsAndDiscardFutureExperience()
        {
            yield return LoadTown();
            RoundTripPhase("idle");
            Reach("invited", () => ResourceMeeting?.State == NpcMeetingState.Invited);
            RoundTripPhase("invited");
            Reach("scheduled", () => ResourceMeeting?.State == NpcMeetingState.Scheduled);
            RoundTripPhase("scheduled");
            Reach("travelling", () => ResourceMeeting?.State == NpcMeetingState.Travelling);
            RoundTripPhase("travelling");
            Reach("before_delivery", () => ResourceMeeting?.State == NpcMeetingState.Talking && ResourceMeeting.DeliveryResultCode == null);
            var beforeDelivery = RoundTripPhase("before_delivery");
            AssertResources(2, 0, 0, 50);
            Reach("after_delivery", () => ResourceMeeting?.DeliveryResultCode == "resource.delivered" && ResourceMeeting.Transcript.Count == 0);
            RoundTripPhase("after_delivery");
            AssertResources(1, 1, 25, 25);
            Reach("talking", () => ResourceMeeting?.State == NpcMeetingState.Talking && ResourceMeeting.Transcript.Count > 0);
            var talking = RoundTripPhase("talking");
            AssertResources(1, 1, 25, 25);
            Reach("completed", () => ResourceMeeting?.State == NpcMeetingState.Completed);
            AssertResources(1, 1, 25, 25);
            Assert.That(ResourceMeeting.Transcript.Count, Is.GreaterThan(talking.CompleteWorld.Meetings.Meetings
                .Single(meeting => meeting.Id == ResourceMeeting.Id).Transcript.Count));
            Assert.That(_session.Controller.GetMeetingMemories(DefaultMvpIds.Npcs.Cook)
                .Count(memory => memory.Kind == "resource.delivered"), Is.EqualTo(1));

            _session.Load("before_delivery");
            AssertSavedWorld(beforeDelivery, Capture(), "before_delivery after future conversation");
            Assert.That(ResourceMeeting.Transcript, Is.Empty);
            Assert.That(ResourceMeeting.DeliveryResultCode, Is.Null);
            Assert.That(_session.Controller.GetMeetingMemories(DefaultMvpIds.Npcs.Cook)
                .Any(memory => memory.Kind == "resource.delivered" || memory.Kind == "meeting.spoken"), Is.False);
            AssertResources(2, 0, 0, 50);
            Reach("restored_delivery", () => ResourceMeeting?.DeliveryResultCode == "resource.delivered");
            AssertResources(1, 1, 25, 25);
            Assert.That(_session.Controller.GetMeetingMemories(DefaultMvpIds.Npcs.Cook)
                .Count(memory => memory.Kind == "resource.delivered"), Is.EqualTo(1));
            Reach("restored_completed", () => ResourceMeeting?.State == NpcMeetingState.Completed);
            AssertResources(1, 1, 25, 25);
            long chargedCalls = _session.Controller.DecisionRequestsStarted;
            _session.Load("idle");
            Assert.That(_session.Controller.DecisionRequestsStarted, Is.EqualTo(chargedCalls));
            Assert.That(Residents().All(body => _session.Controller.GetMeeting(body.NpcId) == null), Is.True);
            Assert.That(Residents().All(body => _session.Controller.GetMeetingMemories(body.NpcId).Count == 0), Is.True);
            AssertResources(2, 0, 0, 50);

            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-platform", "integration-phase-checkpoints", Guid.NewGuid().ToString("N")));
            _session.Export(directory);
            TestContext.WriteLine("Phase checkpoint audit package: " + directory);
        }

        private NpcMeetingSnapshot ResourceMeeting => _session.Controller.GetMeeting(DefaultMvpIds.Npcs.Cook);

        [UnityTest]
        public IEnumerator PendingCalls_AfterTimedSavesAndRepeatedLoadsRejectLateRepliesAndReplayTheCompletePackage()
        {
            var client = new DelayedClient();
            yield return LoadTown(client, useCompletionClock: true);
            _session.Step(0, 0);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests.Any(request => request.Social?.Kind == NpcSocialContextKind.Opportunity), Is.True);
            Guid oldWorld = _session.Controller.GetAgentState(DefaultMvpIds.Npcs.Cook).WorldRunId;
            _realSeconds = 1;
            _session.Save("pending");
            var savedDecisions = _session.Services.SaveStorage.Load("main").Value.CompleteWorld.Decisions;
            var savedCurrent = savedDecisions.Residents.Where(resident => resident.Current != null).ToArray();
            Assert.That(savedCurrent.Length, Is.EqualTo(2));
            foreach (var resident in savedCurrent)
            {
                Assert.That(resident.Current.Calls, Is.EqualTo(1));
                Assert.That(resident.Current.NextStep, Is.EqualTo(2));
                Assert.That(resident.Current.RemainingDecisionSeconds, Is.EqualTo(11));
                Assert.That(resident.RemainingCooldownSeconds, Is.EqualTo(29));
            }
            _realSeconds = 2;
            _session.Load("pending");
            _realSeconds = 3;
            _session.Load("pending");
            Guid restoredWorld = _session.Controller.GetAgentState(DefaultMvpIds.Npcs.Cook).WorldRunId;
            Assert.That(restoredWorld, Is.Not.EqualTo(oldWorld));
            Assert.That(_session.Controller.DecisionRequestsStarted, Is.EqualTo(2));
            Assert.That(_session.Controller.ActiveDecisionRequests, Is.EqualTo(2));
            Assert.That(client.Tokens.All(token => token.IsCancellationRequested), Is.True);
            var restoredDecisions = Capture().CompleteWorld.Decisions;
            Assert.That(restoredDecisions.NextResidentId, Is.EqualTo(savedDecisions.NextResidentId));
            Assert.That(restoredDecisions.Residents.Count(resident => resident.Current != null), Is.EqualTo(2));
            foreach (var saved in savedDecisions.Residents)
            {
                var restored = restoredDecisions.Residents.Single(resident => resident.NpcId == saved.NpcId);
                Assert.That(restored.RemainingCooldownSeconds, Is.EqualTo(saved.RemainingCooldownSeconds), saved.NpcId);
                if (saved.Current == null) { Assert.That(restored.Current, Is.Null, saved.NpcId); continue; }
                Assert.That(restored.Current.Calls, Is.EqualTo(1), saved.NpcId);
                Assert.That(restored.Current.NextStep, Is.EqualTo(2), saved.NpcId);
                Assert.That(restored.Current.RemainingDecisionSeconds, Is.EqualTo(11), saved.NpcId);
            }

            _realSeconds = 4;
            client.CompletePending();
            _session.Step(0, _realSeconds);

            Assert.That(_session.Controller.GetMeeting(DefaultMvpIds.Npcs.Shopkeeper), Is.Null,
                "The retired invitation must not create a meeting after the two loads.");
            Assert.That(_session.Controller.DecisionRequestsStarted, Is.EqualTo(4));
            Assert.That(client.Requests.Skip(2).Select(request => request.Self.WorldRunId), Is.All.EqualTo(restoredWorld));
            Assert.That(_session.TraceClient.Trace.calls.Take(2).All(call => call.hasCompletedRealSeconds
                && call.completedRealSeconds == 4 && call.cancellationTick == 0), Is.True);
            for (int i = 0; i < 16 && _session.Controller.ActiveDecisionRequests > 0; i++)
            {
                _realSeconds += 1;
                client.CompletePending();
                _session.Step(0, _realSeconds);
            }
            Assert.That(_session.Controller.ActiveDecisionRequests, Is.Zero);
            Assert.That(_session.Controller.GetMeeting(DefaultMvpIds.Npcs.Shopkeeper)?.State, Is.EqualTo(NpcMeetingState.Scheduled));
            _session.Complete();
            var source = _session;
            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-platform", "integration-async-replay", Guid.NewGuid().ToString("N")));
            source.Export(directory);
            int sourceCalls = client.Requests.Count;

            yield return SceneManager.UnloadSceneAsync(_scene);
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var replay = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            replay.StartReplay(directory);
            int inputs = 0;
            while (replay.ReplayNext()) Assert.That(++inputs, Is.LessThan(100));

            Assert.That(replay.Completed, Is.True);
            Assert.That(replay.Divergence, Is.Null);
            Assert.That(replay.Controller.GetAgentState(DefaultMvpIds.Npcs.Cook).WorldRunId, Is.Not.EqualTo(restoredWorld));
            Assert.That(replay.Outcomes.Select(outcome => outcome.Code), Is.EqualTo(source.Outcomes.Select(outcome => outcome.Code)));
            Assert.That(replay.Controller.DecisionRequestsStarted, Is.EqualTo(sourceCalls));
            Assert.That(client.Requests.Count, Is.EqualTo(sourceCalls), "Replay must not invoke the recorded provider.");
            Assert.That(replay.CaptureTraceReport().calls.Select(call => call.completedRealSeconds),
                Is.EqualTo(source.CaptureTraceReport().calls.Select(call => call.completedRealSeconds)));
            TestContext.WriteLine("Delayed completion replay package: " + directory);
        }

        private sealed class DelayedClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            private readonly FixedExperimentDecisionClient _fixed = new FixedExperimentDecisionClient();
            private readonly List<TaskCompletionSource<NpcDecisionReply>> _pending = new List<TaskCompletionSource<NpcDecisionReply>>();
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public string SnapshotConfiguration => "delayed-four-resident-integration/v1";

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                Tokens.Add(cancellationToken);
                var pending = new TaskCompletionSource<NpcDecisionReply>();
                _pending.Add(pending);
                return pending.Task;
            }

            internal void CompletePending()
            {
                for (int i = 0; i < _pending.Count; i++)
                    if (!_pending[i].Task.IsCompleted)
                        _pending[i].SetResult(_fixed.DecideAsync(Requests[i], CancellationToken.None).GetAwaiter().GetResult());
            }
        }

        private void Reach(string phase, Func<bool> reached)
        {
            for (int i = 0; i < 240 && !reached(); i++) AdvanceMinutes(1);
            Assert.That(reached(), Is.True, "Phase not reached: " + phase + "; meeting=" + ResourceMeeting?.State
                + "; game=" + _session.Controller.GameTotalMinutes + "\n" + string.Join("\n", _session.Outcomes
                    .Skip(Math.Max(0, _session.Outcomes.Count - 12)).Select(result => result.NpcId + ":" + result.Code)));
        }

        private GameSaveSnapshot RoundTripPhase(string label)
        {
            _session.Save(label);
            var saved = _session.Services.SaveStorage.Load("main").Value;
            long calls = _session.Controller.DecisionRequestsStarted;
            string restoredJson = null;
            for (int repeat = 0; repeat < 2; repeat++)
            {
                Guid oldRun = _session.Controller.GetAgentState(DefaultMvpIds.Npcs.Cook).WorldRunId;
                _session.Load(label);
                Assert.That(_session.Controller.GetAgentState(DefaultMvpIds.Npcs.Cook).WorldRunId, Is.Not.EqualTo(oldRun), label);
                Assert.That(_session.Controller.DecisionRequestsStarted, Is.EqualTo(calls), label);
                var restored = Capture();
                AssertSavedWorld(saved, restored, label);
                string json = JsonFileSaveStorage.SerializeSnapshot(restored);
                if (restoredJson != null) Assert.That(json, Is.EqualTo(restoredJson), label + " repeated restoration");
                restoredJson = json;
            }
            TestContext.WriteLine("Restored twice: " + label + " at " + saved.Clock.Day + "/" + saved.Clock.MinuteOfDay);
            return saved;
        }

        private static void AssertSavedWorld(GameSaveSnapshot expected, GameSaveSnapshot actual, string phase)
        {
            var before = SnapshotJson(expected);
            var after = SnapshotJson(actual);
            foreach (string path in new[] { "schemaVersion", "sourceSchemaVersion", "worldSeed", "clock", "fractionalMinute",
                "characters", "shops", "farm", "livestock", "completeWorld/contentConfiguration", "completeWorld/bodyConfiguration",
                "completeWorld/world", "completeWorld/meetingsEnabled", "completeWorld/meetings", "completeWorld/decisionsEnabled",
                "completeWorld/residents", "completeWorld/player" })
                Assert.That(after.SelectSingleNode("/root/" + path)?.OuterXml,
                    Is.EqualTo(before.SelectSingleNode("/root/" + path)?.OuterXml), phase + " " + path);
        }

        private static XmlDocument SnapshotJson(GameSaveSnapshot snapshot)
        {
            using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(JsonFileSaveStorage.SerializeSnapshot(snapshot)),
                XmlDictionaryReaderQuotas.Max);
            var document = new XmlDocument();
            document.Load(reader);
            return document;
        }

        private void AssertResources(int renFish, int soraFish, int renCoins, int soraCoins)
        {
            var characters = Capture().Characters;
            var ren = characters.Single(character => character.CharacterId == DefaultMvpIds.Npcs.Fisher);
            var sora = characters.Single(character => character.CharacterId == DefaultMvpIds.Npcs.Cook);
            Assert.That(ren.Backpack.Items.Where(item => item.ItemId == DefaultMvpIds.Items.Carp).Sum(item => item.Quantity), Is.EqualTo(renFish));
            Assert.That(sora.Backpack.Items.Where(item => item.ItemId == DefaultMvpIds.Items.Carp).Sum(item => item.Quantity), Is.EqualTo(soraFish));
            Assert.That(ren.Wallet.Balance, Is.EqualTo(renCoins));
            Assert.That(sora.Wallet.Balance, Is.EqualTo(soraCoins));
        }

        private IEnumerator LoadTown(INpcDecisionClient client = null, bool useCompletionClock = false)
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            _session = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            _session.Start(NpcSpeechMode.FreeText, "fixed", client ?? new FixedExperimentDecisionClient(),
                useCompletionClock ? () => _realSeconds : (Func<double>)null);
        }

        private void AdvanceMinutes(int minutes)
        {
            for (int i = 0; i < minutes; i++)
            {
                _session.Step(0.5, _realSeconds);
                _realSeconds += 0.5;
            }
        }

        private GameSaveSnapshot Capture()
        {
            var result = _session.Services.GameSave.Save();
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            return _session.Services.SaveStorage.Load("main").Value;
        }

        private NpcWorldResident2D[] Residents() => _scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<NpcWorldResident2D>(true)).OrderBy(body => body.NpcId).ToArray();

        private static void AssertSettlementDay(GameSaveSnapshot snapshot, int day)
        {
            Assert.That(snapshot.Farm.LastProcessedDay, Is.EqualTo(day));
            Assert.That(snapshot.Livestock.LastProcessedDay, Is.EqualTo(day));
            Assert.That(snapshot.Shops.Select(shop => shop.LastRestockedDay), Is.All.EqualTo(day));
        }
    }
}
#endif
