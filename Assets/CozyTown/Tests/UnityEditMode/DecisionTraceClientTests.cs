using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Experiments;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class DecisionTraceClientTests
    {
        [Test]
        public void CompletedTrace_WithoutTicksOrCallsCanReplayAnUnchangedWorld()
        {
            var recording = DecisionTraceClient.Record(new WaitClient(), "test-model-v1");
            recording.Complete();
            Assert.That(recording.Trace.completed, Is.True);
            Assert.That(recording.Trace.ticks, Is.Empty);
            Assert.That(recording.Trace.calls, Is.Empty);
            var replay = DecisionTraceClient.Replay(recording.Trace, "test-model-v1");
            replay.Complete();
            Assert.That(replay.IsStopped, Is.False);
            Assert.That(replay.Trace.calls, Is.Empty);
            Assert.Throws<InvalidOperationException>(() => replay.Pump(0, 0, 720));
        }

        [Test]
        public void Recording_ExportMutationCannotChangeTheRunningTrace()
        {
            var recording = DecisionTraceClient.Record(new WaitClient(), "test-model-v1");
            recording.Pump(0, 0, 720);
            var response = recording.DecideAsync(Request("Pond"), CancellationToken.None);
            var exported = recording.Trace;
            exported.ticks[0].gameMinutes = 999;
            exported.calls[0].requestJson = "{}";
            exported.calls.Clear();
            Assert.That(recording.Trace.ticks[0].gameMinutes, Is.EqualTo(720));
            Assert.That(recording.Trace.calls.Count, Is.EqualTo(1));
            Assert.That(recording.Trace.calls[0].requestJson, Does.Contain("Pond"));
            recording.Pump(1, 0.1, 720.2);
            Assert.That(response.Result.Kind, Is.EqualTo(NpcDecisionKind.Wait));
            recording.Complete();
            Assert.That(recording.Trace.completed, Is.True);
        }

        [Test]
        public void Replay_InputAndExportMutationCannotReplaceTheRecordedResponse()
        {
            var recording = DecisionTraceClient.Record(new WaitClient(), "test-model-v1");
            recording.Pump(0, 0, 720);
            recording.DecideAsync(Request("Pond"), CancellationToken.None);
            recording.Pump(1, 0.1, 720.2);
            recording.Complete();
            var input = recording.Trace;
            var replay = DecisionTraceClient.Replay(input, "test-model-v1");
            input.ticks[0].gameMinutes = 999;
            input.calls[0].responseJson = "{}";
            input.calls.Clear();
            var exported = replay.Trace;
            Assert.That(exported.calls.Count, Is.EqualTo(1));
            exported.calls[0].responseJson = "{}";
            exported.ticks[0].realSeconds = 999;

            Assert.DoesNotThrow(() => replay.Pump(0, 0, 720));
            var response = replay.DecideAsync(Request("Pond"), CancellationToken.None);
            replay.Pump(1, 0.1, 720.2);
            Assert.That(response.Result.Kind, Is.EqualTo(NpcDecisionKind.Wait));
            replay.Complete();
        }

        [Test]
        public void Replay_ExportsBothSourceAndCurrentWorldIdentifiers()
        {
            var original = ConversationRequest();
            var recording = DecisionTraceClient.Record(new ReceiptClient(), "structured-test-v1");
            recording.Pump(0, 0, 750);
            recording.DecideAsync(original, CancellationToken.None);
            recording.Pump(1, 0.1, 750.2);
            recording.Complete();
            var source = recording.Trace.calls[0];
            Assert.That(source.replayRequestJson, Is.Null.Or.Empty);
            Assert.That(source.replayResponseJson, Is.Null.Or.Empty);

            var previous = recording.Trace;
            for (int generation = 0; generation < 2; generation++)
            {
                var fresh = ConversationRequest();
                var replay = DecisionTraceClient.Replay(previous, "structured-test-v1");
                replay.Pump(0, 0, 750);
                replay.DecideAsync(fresh, CancellationToken.None);
                replay.Pump(1, 0.1, 750.2);
                replay.Complete();
                var exported = replay.Trace.calls[0];
                Assert.That(exported.requestJson, Is.EqualTo(source.requestJson));
                Assert.That(exported.responseJson, Is.EqualTo(source.responseJson));
                Assert.That(exported.replayRequestJson, Does.Contain(fresh.Self.WorldRunId.ToString("N")));
                Assert.That(exported.replayRequestJson, Does.Contain(fresh.DecisionId.ToString("N")));
                Assert.That(exported.replayResponseJson, Does.Contain(fresh.Social.MeetingId.ToString("N")));
                Assert.That(exported.replayResponseJson, Does.Contain(
                    fresh.Observation.Facts.Single(fact => fact.Predicate == "delivered_quantity").FactId));
                previous = replay.Trace;
            }
        }

        [Test]
        public void Replay_PreservesAnUnknownMeetingReferenceForTheHostToReject()
        {
            var request = ConversationRequest();
            var recording = DecisionTraceClient.Record(new UnknownFactClient(unknownMeeting: true), "structured-test-v1");
            recording.Pump(0, 0, 750);
            var original = recording.DecideAsync(request, CancellationToken.None);
            recording.Pump(1, 0.1, 750.2);
            Assert.That(original.Status, Is.EqualTo(TaskStatus.RanToCompletion), "The typed client boundary must preserve a host-rejected reply.");
            Assert.That(original.Result.MeetingId, Is.Not.EqualTo(request.Social.MeetingId));
            recording.Complete();

            var fresh = ConversationRequest();
            var replay = DecisionTraceClient.Replay(recording.Trace, "structured-test-v1");
            replay.Pump(0, 0, 750);
            var response = replay.DecideAsync(fresh, CancellationToken.None);
            Assert.DoesNotThrow(() => replay.Pump(1, 0.1, 750.2));
            Assert.That(response.Result.MeetingId, Is.EqualTo(original.Result.MeetingId));
            Assert.That(response.Result.MeetingId, Is.Not.EqualTo(fresh.Social.MeetingId));
            replay.Complete();
        }

        [Test]
        public void Replay_PreservesAnUnknownFactReferenceForTheHostToReject()
        {
            var request = ConversationRequest();
            var recording = DecisionTraceClient.Record(new UnknownFactClient(), "structured-test-v1");
            recording.Pump(0, 0, 750);
            var original = recording.DecideAsync(request, CancellationToken.None);
            recording.Pump(1, 0.1, 750.2);
            Assert.That(NpcFactSpeech.TryRender(request.Observation, original.Result.SpeechFrame, out _, out string originalError), Is.False);
            Assert.That(originalError, Is.EqualTo("speech.fact_unknown"));
            recording.Complete();

            var fresh = ConversationRequest();
            var replay = DecisionTraceClient.Replay(recording.Trace, "structured-test-v1");
            replay.Pump(0, 0, 750);
            var response = replay.DecideAsync(fresh, CancellationToken.None);
            Assert.DoesNotThrow(() => replay.Pump(1, 0.1, 750.2));
            Assert.That(response.Result.SpeechFrame.FactId, Is.EqualTo(original.Result.SpeechFrame.FactId));
            Assert.That(NpcFactSpeech.TryRender(fresh.Observation, response.Result.SpeechFrame, out _, out string replayedError), Is.False);
            Assert.That(replayedError, Is.EqualTo("speech.fact_unknown"));
            replay.Complete();
        }

        [Test]
        public void Recording_RejectsClientsWithoutDeclaredSnapshotConfiguration()
        {
            Assert.Throws<ArgumentException>(() => DecisionTraceClient.Record(new CaptureClient(), "experiment-configuration"));
        }

        [Test]
        public void FreeTextReplay_PreservesGuidCharactersInsideTheSpokenText()
        {
            var request = ConversationRequest(NpcSpeechMode.FreeText);
            var recording = DecisionTraceClient.Record(new TextClient(), "free-text-test-v1");
            recording.Pump(0, 0, 750);
            var original = recording.DecideAsync(request, CancellationToken.None);
            recording.Pump(1, 0.1, 750.2);
            recording.Complete();
            string spoken = original.Result.Text;

            var fresh = ConversationRequest(NpcSpeechMode.FreeText);
            var replay = DecisionTraceClient.Replay(recording.Trace, "free-text-test-v1");
            replay.Pump(0, 0, 750);
            var result = replay.DecideAsync(fresh, CancellationToken.None);
            replay.Pump(1, 0.1, 750.2);
            Assert.That(result.Result.MeetingId, Is.EqualTo(fresh.Social.MeetingId));
            Assert.That(result.Result.Text, Is.EqualTo(spoken));
            Assert.That(result.Result.Text, Does.Contain(request.Social.MeetingId.ToString("N")));
            Assert.That(result.Result.Text, Does.Not.Contain(fresh.Social.MeetingId.ToString("N")));
            replay.Complete();
        }

        [TestCase("null_reply")]
        [TestCase("response_invalid")]
        [TestCase("client_failure")]
        [TestCase("canceled")]
        [TestCase("sync_failure")]
        [TestCase("null_task")]
        public void RecordedFailures_SurviveJsonExportAndReplay(string failure)
        {
            var recording = DecisionTraceClient.Record(new FailureClient(failure), "test-model-v1");
            recording.Pump(0, 0, 720);
            var original = recording.DecideAsync(Request("Pond"), CancellationToken.None);
            recording.Pump(1, 0.1, 720.2);
            string kind = failure == "sync_failure" || failure == "null_task" ? "client_failure" : failure;
            Assert.That(recording.Trace.calls[0].completionKind, Is.EqualTo(kind));
            Assert.That(recording.Trace.calls[0].hasCompletedRealSeconds, Is.False);
            Assert.That(recording.Trace.calls[0].rawResponseJson, Is.Null.Or.Empty);
            recording.Complete();

            var imported = JsonUtility.FromJson<DecisionTrace>(JsonUtility.ToJson(recording.Trace));
            var replay = DecisionTraceClient.Replay(imported, "test-model-v1");
            Assert.That(replay.SnapshotConfiguration, Is.EqualTo("failure-client-v1"));
            replay.Pump(0, 0, 720);
            var result = replay.DecideAsync(Request("Pond"), CancellationToken.None);
            replay.Pump(1, 0.1, 720.2);
            if (failure == "null_reply") Assert.That(result.Result, Is.Null);
            else if (failure == "canceled") Assert.That(result.IsCanceled, Is.True);
            else
            {
                Assert.That(result.IsFaulted, Is.True);
                Assert.That(result.Exception.InnerException, failure == "response_invalid"
                    ? Is.TypeOf<FormatException>() : Is.TypeOf<InvalidOperationException>());
            }
            Assert.That(result.Status, Is.EqualTo(original.Status));
            replay.Complete();
        }

        [Test]
        public void Trace_RejectsImpossibleDeliveryAndCannotRecordAfterCompletion()
        {
            var recording = DecisionTraceClient.Record(new WaitClient(), "test-model-v1");
            recording.Pump(0, 0, 720);
            recording.DecideAsync(Request("Pond"), CancellationToken.None);
            recording.Pump(1, 0.1, 720.2);
            recording.Complete();

            var invalid = recording.Trace;
            invalid.calls[0].deliveryTick = 0;
            Assert.Throws<ArgumentException>(() => DecisionTraceClient.Replay(invalid, "test-model-v1"),
                "A completed result cannot have been delivered in the same pre-dispatch pump.");
            Assert.Throws<InvalidOperationException>(() => recording.Pump(2, 0.2, 720.4));
            var afterCompletion = recording.DecideAsync(Request("Pond"), CancellationToken.None);
            Assert.That(afterCompletion.IsFaulted, Is.True);
            Assert.That(recording.Trace.calls.Count, Is.EqualTo(1));
        }

        [Test]
        public void Replay_RequiresTheRecordedCancellationBeforeReturningALateReply()
        {
            var provider = new ControlledClient();
            var recording = DecisionTraceClient.Record(provider, "test-model-v1");
            using var canceled = new CancellationTokenSource();
            recording.Pump(0, 0, 720);
            var late = recording.DecideAsync(Request("Pond"), canceled.Token);
            canceled.Cancel();
            Assert.That(late.IsCompleted, Is.False, "Cancellation must not claim that an ignoring provider has finished.");
            Assert.That(recording.Trace.calls[0].cancellationTick, Is.EqualTo(0));
            provider.Pending[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            recording.Pump(1, 0.1, 720.2);
            Assert.That(late.Result.Kind, Is.EqualTo(NpcDecisionKind.Wait));
            recording.Complete();

            var mismatched = DecisionTraceClient.Replay(recording.Trace, "test-model-v1");
            mismatched.Pump(0, 0, 720);
            var withheld = mismatched.DecideAsync(Request("Pond"), CancellationToken.None);
            Assert.Throws<InvalidOperationException>(() => mismatched.Pump(1, 0.1, 720.2));
            Assert.That(mismatched.IsStopped, Is.True);
            Assert.That(withheld.Status, Is.Not.EqualTo(TaskStatus.RanToCompletion));

            using var matchingCancellation = new CancellationTokenSource();
            var replay = DecisionTraceClient.Replay(recording.Trace, "test-model-v1");
            replay.Pump(0, 0, 720);
            var replayed = replay.DecideAsync(Request("Pond"), matchingCancellation.Token);
            matchingCancellation.Cancel();
            Assert.That(replayed.IsCompleted, Is.False);
            replay.Pump(1, 0.1, 720.2);
            Assert.That(replayed.Result.Kind, Is.EqualTo(NpcDecisionKind.Wait));
            replay.Complete();
        }

        [Test]
        public void Replay_PreservesOutOfOrderCompletionAndCandidateFailures()
        {
            var provider = new ControlledClient();
            var recording = DecisionTraceClient.Record(provider, "test-model-v1", () => 0.05);
            recording.Pump(0, 0, 720);
            var first = recording.DecideAsync(Request("Pond"), CancellationToken.None);
            var second = recording.DecideAsync(Request("Market"), CancellationToken.None);
            provider.Pending[1].SetResult(new NpcDecisionReply(NpcDecisionKind.InspectLocation, "rest"));
            provider.Pending[0].SetException(new NpcCandidateException("candidate.duration_invalid"));
            Assert.DoesNotThrow(() => recording.Pump(1, 0.1, 720.2));
            Assert.That(first.IsFaulted, Is.True);
            Assert.That(first.Exception.InnerException, Is.TypeOf<NpcCandidateException>());
            Assert.That(second.Result.Kind, Is.EqualTo(NpcDecisionKind.InspectLocation));
            Assert.That(recording.Trace.calls[1].completionOrdinal, Is.LessThan(recording.Trace.calls[0].completionOrdinal));
            Assert.That(recording.Trace.calls[0].completedRealSeconds, Is.EqualTo(0.05));
            recording.Complete();

            var replay = DecisionTraceClient.Replay(recording.Trace, "test-model-v1");
            replay.Pump(0, 0, 720);
            var replayedFirst = replay.DecideAsync(Request("Pond"), CancellationToken.None);
            var replayedSecond = replay.DecideAsync(Request("Market"), CancellationToken.None);
            var delivered = new List<int>();
            replayedFirst.ContinueWith(_ => delivered.Add(1), TaskContinuationOptions.ExecuteSynchronously);
            replayedSecond.ContinueWith(_ => delivered.Add(2), TaskContinuationOptions.ExecuteSynchronously);
            replay.Pump(1, 0.1, 720.2);
            Assert.That(delivered, Is.EqualTo(new[] { 2, 1 }));
            Assert.That(((NpcCandidateException)replayedFirst.Exception.InnerException).Code, Is.EqualTo("candidate.duration_invalid"));
            Assert.That(replayedSecond.Result.LocationId, Is.EqualTo("rest"));
            replay.Complete();
        }

        [Test]
        public void Replay_RebindsMeetingAndReceiptReferencesToTheNewWorld()
        {
            var request = ConversationRequest();
            var recording = DecisionTraceClient.Record(new ReceiptClient(), "structured-test-v1");
            recording.Pump(0, 0, 750);
            var response = recording.DecideAsync(request, CancellationToken.None);
            recording.Pump(1, 0.1, 750.2);
            Assert.That(response.Result.Kind, Is.EqualTo(NpcDecisionKind.Speak));
            Assert.That(response.Result.MeetingId, Is.EqualTo(request.Social.MeetingId));
            recording.Complete();

            var fresh = ConversationRequest();
            Assert.That(fresh.Social.MeetingId, Is.Not.EqualTo(request.Social.MeetingId));
            var replay = DecisionTraceClient.Replay(recording.Trace, "structured-test-v1");
            replay.Pump(0, 0, 750);
            var replayed = replay.DecideAsync(fresh, CancellationToken.None);
            Assert.That(replay.IsStopped, Is.False, replay.Divergence);
            replay.Pump(1, 0.1, 750.2);
            Assert.That(replayed.Result.MeetingId, Is.EqualTo(fresh.Social.MeetingId));
            Assert.That(replayed.Result.SpeechFrame.FactId, Is.EqualTo(
                fresh.Observation.Facts.Single(fact => fact.Predicate == "delivered_quantity").FactId));
            replay.Complete();
        }

        [Test]
        public void Replay_MatchesFreshIdentifiersButStopsWhenObservedFactsChange()
        {
            var recording = DecisionTraceClient.Record(new WaitClient(), "test-model-v1");
            recording.Pump(0, 0, 720);
            var original = recording.DecideAsync(Request("Pond"), CancellationToken.None);
            Assert.That(original.IsCompleted, Is.False, "Provider completion must wait for the next pump.");
            recording.Pump(1, 0.1, 720.2);
            Assert.That(original.IsCompleted, Is.True);
            Assert.That(original.Result.Kind, Is.EqualTo(NpcDecisionKind.Wait));
            recording.Complete();

            var replay = DecisionTraceClient.Replay(recording.Trace, "test-model-v1");
            replay.Pump(0, 0, 720);
            var matched = replay.DecideAsync(Request("Pond"), CancellationToken.None);
            Assert.That(matched.IsCompleted, Is.False);
            replay.Pump(1, 0.1, 720.2);
            Assert.That(matched.Result.Kind, Is.EqualTo(NpcDecisionKind.Wait));
            replay.Complete();

            var changed = DecisionTraceClient.Replay(recording.Trace, "test-model-v1");
            changed.Pump(0, 0, 720);
            var rejected = changed.DecideAsync(Request("Market"), CancellationToken.None);
            Assert.That(changed.IsStopped, Is.True);
            Assert.That(changed.Divergence, Does.Contain("request"));
            Assert.That(rejected.IsFaulted, Is.True);
            Assert.Throws<InvalidOperationException>(() => changed.Pump(1, 0.1, 720.2));
        }

        private static NpcDecisionRequest Request(string regionName)
        {
            var world = new NpcAgentWorld(new[] { new NpcDailySchedule("mina", "home", "outside", "entry",
                "work", "rest", "work", 360, 480, 720, 780, 1020, 1080) });
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", regionName, -6, -6, 6, 6) },
                Array.Empty<NpcObservationEntity>());
            var capture = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world,
                new[] { new NpcDefinition("mina", "Mina", "A shopkeeper", "Hello") }, capture,
                observe: request => scene.Read(request.NpcId, request.Self.WorldRunId, world.TotalMinutes,
                    new[] { new NpcObservationBody("mina", 0, 0) }));
            scheduler.Tick(0);
            return capture.Request;
        }

        private static NpcDecisionRequest ConversationRequest(NpcSpeechMode mode = NpcSpeechMode.StructuredFacts)
        {
            var schedules = new[] { "ren", "sora" }.Select(id => new NpcDailySchedule(id, id + ".home", id + ".outside",
                id + ".entry", id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 810, 1020, 1080));
            var world = new NpcAgentWorld(schedules);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var store = new InMemoryEconomyStateStore(new[] {
                new CharacterEconomySnapshot("ren", new InventorySnapshot(new[] { new ItemStack("fish", 2) }), new WalletSnapshot(0)),
                new CharacterEconomySnapshot("sora", new InventorySnapshot(Array.Empty<ItemStack>()), new WalletSnapshot(50)) },
                Array.Empty<ShopEconomySnapshot>());
            var trading = new CharacterResourceTrading(store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            var terms = new CharacterTradeTerms("ren", "sora", "fish", 1, 25);
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("supply", "sora", "ren", "pond", "sora.rest", "ren.rest",
                720, 750, 780, maxTurns: 6, resourceTerms: terms) }, (npc, location) => NpcMeetingPresence.Arrived, trading);
            var meeting = board.Invite(world.GetState("sora"), "supply").Value;
            Assert.That(board.Respond(world.GetState("ren"), meeting.Id, true).IsSuccess, Is.True);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 750), 0, false, 1));
            board.Observe();
            Assert.That(board.Deliver(world.GetState("sora"), meeting.Id).IsSuccess, Is.True);
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond", -6, -6, 6, 6) },
                Array.Empty<NpcObservationEntity>());
            var capture = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world,
                new[] { new NpcDefinition("sora", "Sora", "A cook", "Hello") }, capture, meetings: board,
                speechMode: mode,
                observe: request => scene.Read(request.NpcId, request.Self.WorldRunId, world.TotalMinutes,
                    new[] { new NpcObservationBody("sora", 0, 0) }, request.Social, trading.Inspect(terms, "sora"), board.GetMemories("sora")));
            scheduler.Tick(0);
            return capture.Request;
        }

        private sealed class CaptureClient : INpcDecisionClient
        {
            public NpcDecisionRequest Request;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            }
        }

        private sealed class WaitClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            public string SnapshotConfiguration => "wait-client-v1";
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
                => Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Wait));
        }

        private sealed class ReceiptClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            public string SnapshotConfiguration => "receipt-client-v1";
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
                => Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: request.Social.MeetingId,
                    speechFrame: new NpcSpeechFrame("report_receipt",
                        request.Observation.Facts.Single(fact => fact.Predicate == "delivered_quantity").FactId, "brief")));
        }

        private sealed class ControlledClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            public string SnapshotConfiguration => "controlled-client-v1";
            public readonly List<TaskCompletionSource<NpcDecisionReply>> Pending = new List<TaskCompletionSource<NpcDecisionReply>>();
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                var pending = new TaskCompletionSource<NpcDecisionReply>();
                Pending.Add(pending);
                return pending.Task;
            }
        }

        private sealed class TextClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            public string SnapshotConfiguration => "text-client-v1";
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
                => Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: request.Social.MeetingId,
                    text: "他说 \"" + request.Social.MeetingId.ToString("N") + "\" is only a label."));
        }

        private sealed class UnknownFactClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            private readonly bool _unknownMeeting;
            public UnknownFactClient(bool unknownMeeting = false) => _unknownMeeting = unknownMeeting;
            public string SnapshotConfiguration => "unknown-fact-client-v1";
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
                => Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Speak,
                    meetingId: _unknownMeeting ? Guid.ParseExact("11111111111111111111111111111111", "N") : request.Social.MeetingId,
                    speechFrame: new NpcSpeechFrame("report_receipt", _unknownMeeting
                        ? request.Observation.Facts.Single(fact => fact.Predicate == "delivered_quantity").FactId
                        : "meeting:11111111111111111111111111111111:delivered_quantity", "brief")));
        }

        private sealed class FailureClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            private readonly string _failure;
            public FailureClient(string failure) => _failure = failure;
            public string SnapshotConfiguration => "failure-client-v1";
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                if (_failure == "null_reply") return Task.FromResult<NpcDecisionReply>(null);
                if (_failure == "response_invalid") return Task.FromException<NpcDecisionReply>(new FormatException());
                if (_failure == "client_failure") return Task.FromException<NpcDecisionReply>(new InvalidOperationException());
                if (_failure == "canceled") return Task.FromCanceled<NpcDecisionReply>(new CancellationToken(true));
                if (_failure == "null_task") return null;
                throw new InvalidOperationException();
            }
        }
    }
}
