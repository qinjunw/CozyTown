using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcConversationBudgetTests
    {
        private NpcAgentWorld _world;
        private NpcMeetingBoard _board;
        private IEconomyStateStore _store;
        private CharacterResourceTrading _trading;
        private NpcMeetingPresence _renPresence;
        private bool _observationAvailable;
        private readonly NpcObservationScene _scene = new NpcObservationScene(
            new[] { new NpcObservationRegion("pond", "Pond", -10, -10, 10, 10) },
            Array.Empty<NpcObservationEntity>());
        private readonly NpcObservationBody[] _bodies = {
            new NpcObservationBody("sora", 0, 0), new NpcObservationBody("ren", 2, 0) };

        [SetUp]
        public void SetUp()
        {
            _renPresence = NpcMeetingPresence.Arrived;
            _observationAvailable = true;
            _bodies[0] = new NpcObservationBody("sora", 0, 0);
            _bodies[1] = new NpcObservationBody("ren", 2, 0);
            _world = new NpcAgentWorld(new[] { Schedule("sora", 840), Schedule("ren", 795) });
            _world.Observe(Time(735));
            _store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) },
                Array.Empty<ShopEconomySnapshot>());
            _trading = new CharacterResourceTrading(_store,
                new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            _board = Board();
        }

        private NpcMeetingBoard Board(int maxTurns = 4)
            => new NpcMeetingBoard(_world, new[] {
                new NpcMeetingPlan("fish-supply", "sora", "ren", "pond", "sora.rest", "ren.rest", 735, 780, 795,
                    maxTurns: maxTurns,
                    resourceTerms: new CharacterTradeTerms("ren", "sora", "fish", 1, 25)) },
                (npc, location) => npc == "ren" ? _renPresence : NpcMeetingPresence.Arrived, _trading);

        [Test]
        public void QueuedConversationTurn_ResumesAfterBudgetReturnsAndCompletesBeforeMeetingDeadline()
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1));
            Step(scheduler, "old-request", 0, 794.8);
            Assert.That(client.Requests.Single().Social.Transcript.Count, Is.EqualTo(3));

            Step(scheduler, "schedule-cancel", 0.1, 795);
            Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("agent.decision_stale"));
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            client.Replies[0].SetResult(Say(client.Requests[0], "obsolete-fourth"));
            Step(scheduler, "late-reply-drained", 0.2, 795.2);
            Step(scheduler, "before-opportunity-expiry", 15, 824.8);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Step(scheduler, "opportunity-expired", 15.1, 825);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("agent.decision_stale"));
            Step(scheduler, "other-participant-schedule", 22.6, 840);
            Step(scheduler, "rolling-budget-open", 60, 914.8);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.RequestsInLastMinute, Is.EqualTo(1));
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            Assert.That(_board.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Talking));
            Assert.That(_board.GetContext("ren").Kind, Is.EqualTo(NpcSocialContextKind.Conversation));
            Assert.That(_board.GetCurrent("ren").Transcript.Count, Is.EqualTo(3));
            Assert.That(_board.GetMemories("sora").Any(memory => memory.Text == "obsolete-fourth"), Is.False);
            var replacement = client.Requests[1];
            Assert.That(replacement.DecisionId, Is.Not.EqualTo(client.Requests[0].DecisionId));
            Assert.That(replacement.GameTotalMinutes, Is.EqualTo(914.8));
            Assert.That(replacement.Observation.ObservedAtTotalMinutes, Is.EqualTo(914.8));
            Assert.That(replacement.Triggers.Any(item => item.Kind == NpcAgentEventKind.ScheduleChanged
                && item.TotalMinutes == 795), Is.True);
            client.Replies[1].SetResult(Say(replacement, "valid-fourth"));
            Step(scheduler, "fresh-fourth-published", 60.1, 915);
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(_board.GetLatest("ren").Transcript.Count, Is.EqualTo(4));
            AssertAssets();

            Step(scheduler, "meeting-deadline", 67.6, 930);
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(_board.IsPlaceReserved("pond"), Is.False);
            _world.Observe(Time(1000));
            _board.Observe();
            Assert.That(new[] { "sora", "ren" }.All(id => _world.GetState(id).ActiveActivity == null
                && _world.GetState(id).Target.ExpectedActivity == NpcActivity.Working), Is.True);
            AssertAssets();
        }

        [TestCase(795.2)]
        [TestCase(824.9)]
        [TestCase(825)]
        [TestCase(825.1)]
        public void RollingBudgetRelease_ResamplesPendingConversationAtDispatch(double gameAtRelease)
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1));
            Step(scheduler, "old-request", 0, 794.8);
            var oldRequest = client.Requests.Single();
            Step(scheduler, "schedule-cancel", 0.1, 795);
            client.Replies[0].SetResult(Say(oldRequest, "obsolete-fourth"));
            Step(scheduler, "late-reply-drained", 0.2, 795.2);
            Step(scheduler, "rolling-budget-still-full", 59.9, 795.2);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Step(scheduler, "rolling-budget-open", 60, gameAtRelease);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.RequestsInLastMinute, Is.EqualTo(1));
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            var replacement = client.Requests[1];
            Assert.That(replacement.DecisionId, Is.Not.EqualTo(oldRequest.DecisionId));
            Assert.That(replacement.Step, Is.EqualTo(1));
            Assert.That(replacement.Triggers.Any(trigger => trigger.Kind == NpcAgentEventKind.ScheduleChanged
                && trigger.TotalMinutes == 795), Is.True);
            Assert.That(replacement.Self.Revision, Is.EqualTo(_world.GetState("ren").Revision));
            Assert.That(replacement.GameTotalMinutes, Is.EqualTo(gameAtRelease));
            Assert.That(replacement.Observation.ObservedAtTotalMinutes, Is.EqualTo(gameAtRelease));
            client.Replies[1].SetResult(Say(replacement, "valid-fourth"));
            Step(scheduler, "replacement-completed", 60.01, gameAtRelease + 0.01);
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(_board.GetLatest("ren").Transcript.Count, Is.EqualTo(4));
            Assert.That(_board.GetMemories("sora").Count(memory => memory.Text == "valid-fourth"), Is.EqualTo(1));
            Assert.That(_board.GetMemories("sora").Any(memory => memory.Text == "obsolete-fourth"), Is.False);
            AssertAssets();
        }

        [Test]
        public void ReplyBeforeScheduleBoundary_CompletesWithoutAnotherBudgetSlot()
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1));
            Step(scheduler, "old-request", 0, 794.8);
            client.Replies[0].SetResult(Say(client.Requests[0], "valid-fourth"));
            Step(scheduler, "reply-before-boundary", 0.05, 794.9);
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("meeting.say"));
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(_board.GetLatest("ren").Transcript.Count, Is.EqualTo(4));
            Step(scheduler, "schedule-after-completion", 0.1, 795);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(_board.GetMemories("sora").Count(memory => memory.Text == "valid-fourth"), Is.EqualTo(1));
            AssertAssets();
        }

        [Test]
        public void SpareBudgetSlot_DispatchesNewScheduleDecisionAfterTheCancelledReplyDrains()
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 2));
            Step(scheduler, "old-request", 0, 794.8);
            var oldRequest = client.Requests.Single();
            Step(scheduler, "schedule-cancel", 0.1, 795);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            client.Replies[0].SetResult(Say(oldRequest, "obsolete-fourth"));
            Step(scheduler, "late-reply-drained-and-replacement-started", 0.2, 795.2);
            var replacement = client.Requests[1];
            Assert.That(replacement.DecisionId, Is.Not.EqualTo(oldRequest.DecisionId));
            Assert.That(replacement.Step, Is.EqualTo(1));
            Assert.That(replacement.Triggers.Any(trigger => trigger.Kind == NpcAgentEventKind.ScheduleChanged), Is.True);
            Assert.That(scheduler.RequestsInLastMinute, Is.EqualTo(2));
            client.Replies[1].SetResult(Say(replacement, "valid-fourth"));
            Step(scheduler, "replacement-completed", 0.3, 795.4);
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(_board.GetLatest("ren").Transcript.Count, Is.EqualTo(4));
            Assert.That(_board.GetMemories("sora").Any(memory => memory.Text == "obsolete-fourth"), Is.False);
            AssertAssets();
        }

        [TestCase("world-rebuild")]
        [TestCase("player-busy")]
        [TestCase("presence-lost")]
        [TestCase("completed")]
        [TestCase("meeting-deadline")]
        public void TerminalMeetingChange_DiscardsQueuedTurnAndLateReplyBeforeBudgetReturns(string cause)
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1));
            Step(scheduler, "old-request", 0, 794.8);
            var oldRequest = client.Requests.Single();
            Step(scheduler, "schedule-cancel", 0.1, 795);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
            long rebuild = cause == "world-rebuild" ? 2 : 1;
            double terminalMinute = cause == "meeting-deadline" ? 930 : 795.2;
            if (cause == "player-busy") _renPresence = NpcMeetingPresence.Busy;
            if (cause == "presence-lost") _renPresence = NpcMeetingPresence.Travelling;
            if (cause == "completed")
                Assert.That(_board.EndConversation(_world.GetState("ren"), oldRequest.Social.MeetingId).IsSuccess, Is.True);
            Step(scheduler, cause, 0.2, terminalMinute, rebuild);
            Assert.That(_board.GetCurrent("ren"), Is.Null);
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            if (cause == "world-rebuild")
            {
                Assert.That(_world.GetState("ren").WorldRunId, Is.Not.EqualTo(oldRequest.Self.WorldRunId));
                Assert.That(_board.GetMemories("ren"), Is.Empty);
            }
            else
            {
                Assert.That(_board.GetLatest("ren").State,
                    Is.EqualTo(cause == "completed" ? NpcMeetingState.Completed
                        : cause == "meeting-deadline" ? NpcMeetingState.Expired : NpcMeetingState.Cancelled));
                Assert.That(_board.GetLatest("ren").Transcript.Count, Is.EqualTo(3));
            }
            client.Replies[0].SetResult(Say(oldRequest, "obsolete-fourth"));
            Step(scheduler, "terminal-late-reply-drained", 0.3, terminalMinute, rebuild);
            Step(scheduler, "terminal-budget-open", 60, Math.Max(914.8, terminalMinute), rebuild);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.RequestsInLastMinute, Is.Zero);
            Assert.That(scheduler.ActiveRequestCount, Is.Zero);
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            Assert.That(_board.GetContext("ren"), Is.Null);
            Assert.That(_board.IsPlaceReserved("pond"), Is.False);
            Assert.That(new[] { "ren", "sora" }.All(id => _world.GetState(id).ActiveActivity == null), Is.True);
            Assert.That(_board.GetMemories("sora").Any(memory => memory.Text == "obsolete-fourth"), Is.False);
            AssertAssets();
        }

        [Test]
        public void RequestDispatchedJustBeforeOriginalQueueExpiry_UsesItsNewDecisionTime()
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1));
            Step(scheduler, "old-request", 0, 794.8);
            Step(scheduler, "schedule-cancel", 0.1, 795);
            client.Replies[0].SetResult(Say(client.Requests[0], "obsolete-fourth"));
            Step(scheduler, "late-reply-drained", 0.2, 795.2);
            Step(scheduler, "dispatch-just-before-queue-expiry", 60, 824.9);
            var replacement = client.Requests[1];
            Assert.That(replacement.GameTotalMinutes, Is.EqualTo(824.9));
            Assert.That(replacement.Observation.ObservedAtTotalMinutes, Is.EqualTo(824.9));
            client.Replies[1].SetResult(Say(replacement, "replacement-after-queue-expiry"));
            Step(scheduler, "queue-deadline-after-dispatch", 60.05, 825);
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("meeting.say"));
            Assert.That(client.Tokens[1].IsCancellationRequested, Is.False);
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(_board.GetLatest("ren").Transcript.Count, Is.EqualTo(4));
            Assert.That(_board.GetMemories("sora").Count(memory => memory.Text == "replacement-after-queue-expiry"), Is.EqualTo(1));
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            AssertAssets();
        }

        [TestCase("client", "agent.client_failure")]
        [TestCase("invalid-json", "agent.response_invalid")]
        [TestCase("uncorrectable", "agent.response_invalid")]
        [TestCase("wait", "agent.operation_unavailable")]
        [TestCase("changed-observation", "agent.observation_stale")]
        [TestCase("request-timeout", "agent.request_timeout")]
        [TestCase("correction-wait-timeout", "agent.decision_timeout")]
        public void ResumedTurn_DoesNotCreateNewDecisionsAfterFailureOrRepeatedTicks(string failure, string expectedCode)
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1));
            Step(scheduler, "old-request", 0, 794.8);
            Step(scheduler, "schedule-cancel", 0.1, 795);
            client.Replies[0].SetResult(Say(client.Requests[0], "obsolete-fourth"));
            Step(scheduler, "old-reply-drained", 0.2, 795.2);
            for (int i = 0; i < 4; i++) Step(scheduler, "repeat-waiting-tick", 59, 824.9);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Step(scheduler, "budget-open", 60, 824.9);
            for (int i = 0; i < 4; i++) Step(scheduler, "repeat-in-flight-tick", 60, 824.9);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            var request = client.Requests[1];
            if (failure == "client") client.Replies[1].SetException(new InvalidOperationException("controlled client failure"));
            else if (failure == "invalid-json") client.Replies[1].SetException(new FormatException("controlled invalid response"));
            else if (failure == "uncorrectable") client.Replies[1].SetException(new NpcCandidateException("candidate.schema_mismatch"));
            else if (failure == "correction-wait-timeout") client.Replies[1].SetException(new NpcCandidateException("candidate.text_invalid"));
            else if (failure == "wait") client.Replies[1].SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            else if (failure == "request-timeout")
            {
                Step(scheduler, "request-timeout", 68.01, 825);
                Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
                Assert.That(client.Tokens[1].IsCancellationRequested, Is.True);
                client.Replies[1].SetResult(Say(request, "rejected-fourth"));
            }
            else
            {
                _bodies[1] = new NpcObservationBody("ren", 3, 0);
                client.Replies[1].SetResult(Say(request, "rejected-fourth"));
            }
            Step(scheduler, "failure-drained", failure == "request-timeout" ? 68.02 : 60.1, 825);
            if (failure == "correction-wait-timeout")
            {
                Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
                Step(scheduler, "original-decision-timeout", 72, 825);
                Assert.That(scheduler.GetLastOutcome("ren").DecisionId, Is.EqualTo(request.DecisionId));
                Assert.That(scheduler.GetLastOutcome("ren").Context.Step, Is.EqualTo(2));
                Assert.That(scheduler.GetLastOutcome("ren").CandidateErrorCodes, Is.EqualTo(new[] { "candidate.text_invalid" }));
            }
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo(expectedCode));
            Step(scheduler, "later-budget-open-without-new-event", 120, 825);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.ActiveRequestCount, Is.Zero);
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            Assert.That(_board.GetCurrent("ren").Transcript.Count, Is.EqualTo(3));
            Assert.That(_board.GetMemories("sora").Any(memory => memory.Text == "rejected-fourth"), Is.False);
            AssertAssets();
        }

        [Test]
        public void ObservationUnavailableAtDispatch_ConsumesTurnWithoutAutomaticRetry()
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1));
            Step(scheduler, "old-request", 0, 794.8);
            Step(scheduler, "schedule-cancel", 0.1, 795);
            client.Replies[0].SetResult(Say(client.Requests[0], "obsolete-fourth"));
            Step(scheduler, "old-reply-drained", 0.2, 795.2);
            _observationAvailable = false;
            Step(scheduler, "observation-unavailable-at-dispatch", 60, 825);
            Assert.That(scheduler.GetLastOutcome("ren").Code, Is.EqualTo("agent.observation_unavailable"));
            Assert.That(scheduler.GetLastOutcome("ren").Context.GameTotalMinutes, Is.EqualTo(825));
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            _observationAvailable = true;
            Step(scheduler, "observation-restored-without-new-event", 120, 825);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.ActiveRequestCount, Is.Zero);
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            Assert.That(_board.GetCurrent("ren").Transcript.Count, Is.EqualTo(3));
            AssertAssets();
        }

        [Test]
        public void OldWorldReply_DrainsBeforeDispatchingNewWorldWaitingTurn()
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1));
            Step(scheduler, "old-world-request", 0, 794.8);
            var oldRequest = client.Requests.Single();
            Step(scheduler, "old-world-schedule-cancel", 0.1, 795);
            _world = new NpcAgentWorld(new[] { Schedule("sora", 840), Schedule("ren", 795) });
            _world.Observe(Time(735));
            _store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) },
                Array.Empty<ShopEconomySnapshot>());
            _trading = new CharacterResourceTrading(_store,
                new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            _board.BindWorld(_world, _trading);
            scheduler.BindWorld(_world);
            PrepareFourthTurn();
            Step(scheduler, "new-world-turn-waits-for-old-request", 0.2, 794.8);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Step(scheduler, "budget-open-old-request-still-in-flight", 60, 855);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            client.Replies[0].SetResult(Say(oldRequest, "obsolete-old-world"));
            Step(scheduler, "old-reply-drains-new-world-dispatches", 60.1, 855);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var request = client.Requests[1];
            Assert.That(request.Self.WorldRunId, Is.Not.EqualTo(oldRequest.Self.WorldRunId));
            Assert.That(request.Social.MeetingId, Is.Not.EqualTo(oldRequest.Social.MeetingId));
            Assert.That(request.Observation.WorldRunId, Is.EqualTo(_world.GetState("ren").WorldRunId));
            Assert.That(request.PreviousResultCode, Is.Null);
            client.Replies[1].SetResult(Say(request, "new-world-fourth"));
            Step(scheduler, "new-world-completed", 60.2, 855.1);
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(_board.GetLatest("ren").Transcript.Count, Is.EqualTo(4));
            Assert.That(_board.GetMemories("sora").Any(memory => memory.Text == "obsolete-old-world"), Is.False);
            AssertAssets();
        }

        [Test]
        public void CompletedConversation_PreservesOtherResidentsOrdinaryQueue()
        {
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1), includeSora: true);
            Step(scheduler, "old-request", 0, 794.8);
            Step(scheduler, "schedule-cancel", 0.1, 795);
            client.Replies[0].SetResult(Say(client.Requests[0], "obsolete-fourth"));
            Step(scheduler, "old-reply-drained", 0.2, 795.2);
            Step(scheduler, "conversation-dispatched", 60, 824.9);
            client.Replies[1].SetResult(Say(client.Requests[1], "valid-fourth"));
            Step(scheduler, "completed-with-sora-resting", 60.1, 825);
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Step(scheduler, "ordinary-budget-open", 120, 825);
            Assert.That(client.Requests.Count, Is.EqualTo(3));
            Assert.That(client.Requests[2].NpcId, Is.EqualTo("sora"));
            Assert.That(client.Requests[2].Social, Is.Null);
            client.Replies[2].SetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            Step(scheduler, "ordinary-wait-completed", 120.1, 825);
            Assert.That(scheduler.GetLastOutcome("sora").Code, Is.EqualTo("agent.decision_wait"));
            Assert.That(_board.GetLatest("ren").Transcript.Count, Is.EqualTo(4));
            AssertAssets();
        }

        [Test]
        public void ChangedTurn_RemovesOldMarkerAndSurvivesTheOldReply()
        {
            _board = Board(maxTurns: 6);
            PrepareFourthTurn();
            var client = new ControlledClient();
            using var scheduler = Scheduler(client, new NpcDecisionSettings(maxRequestsPerMinute: 1));
            Step(scheduler, "old-fourth-request", 0, 794.8);
            var oldRequest = client.Requests.Single();
            Step(scheduler, "schedule-cancel", 0.1, 795);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Assert.That(_board.Speak(_world.GetState("ren"), oldRequest.Social.MeetingId, "external-fourth").IsSuccess, Is.True);
            Step(scheduler, "speaker-changed", 0.2, 795.2);
            Assert.That(_board.GetCurrent("ren").SpeakerId, Is.EqualTo("sora"));
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            Assert.That(_board.Speak(_world.GetState("sora"), oldRequest.Social.MeetingId, "external-fifth").IsSuccess, Is.True);
            Step(scheduler, "next-ren-turn-waits", 0.3, 795.3);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            client.Replies[0].SetResult(Say(oldRequest, "obsolete-fourth"));
            Step(scheduler, "old-fourth-drained", 0.4, 795.4);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            Step(scheduler, "sixth-turn-dispatched", 60, 825.3);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var request = client.Requests[1];
            Assert.That(request.Social.Transcript.Count, Is.EqualTo(5));
            Assert.That(request.Triggers.Any(item => item.Kind == NpcAgentEventKind.MeetingChanged
                && item.TotalMinutes == 795.2), Is.True);
            Assert.That(request.Triggers.Any(item => item.Kind == NpcAgentEventKind.ScheduleChanged), Is.False);
            client.Replies[1].SetResult(Say(request, "valid-sixth"));
            Step(scheduler, "sixth-turn-completed", 60.1, 825.4);
            Assert.That(_board.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(_board.GetLatest("ren").Transcript.Count, Is.EqualTo(6));
            Assert.That(_board.GetMemories("sora").Any(memory => memory.Text == "obsolete-fourth"), Is.False);
            AssertAssets();
        }

        private void PrepareFourthTurn()
        {
            var invitation = _board.Invite(_world.GetState("sora"), "fish-supply");
            Assert.That(invitation.IsSuccess, Is.True);
            Assert.That(_board.Respond(_world.GetState("ren"), invitation.Value.Id, true).IsSuccess, Is.True);
            _world.Observe(Time(787));
            _board.Observe();
            Assert.That(_board.Deliver(_world.GetState("sora"), invitation.Value.Id).IsSuccess, Is.True);
            foreach (var speaker in new[] { "sora", "ren", "sora" })
                Assert.That(_board.Speak(_world.GetState(speaker), invitation.Value.Id, "recorded-" + speaker).IsSuccess, Is.True);
            AssertAssets();
        }

        private NpcDecisionScheduler Scheduler(ControlledClient client, NpcDecisionSettings settings, bool includeSora = false)
            => new NpcDecisionScheduler(_world, includeSora ? new[] { Profile("ren"), Profile("sora") } : new[] { Profile("ren") },
                client, settings, _board, Observe);

        private NpcLocalObservation Observe(NpcDecisionRequest request)
            => !_observationAvailable ? null : _scene.Read(request.NpcId, _world.GetState(request.NpcId).WorldRunId, _world.TotalMinutes, _bodies,
                social: request.Social, ownResources: _trading.Inspect(request.Social?.Resources?.Terms, request.NpcId),
                memories: _board.GetMemories(request.NpcId));

        private void Step(NpcDecisionScheduler scheduler, string point, double realSeconds, double gameMinutes, long rebuild = 1)
        {
            _world.Observe(Time(gameMinutes, rebuild));
            var outcomes = scheduler.Tick(realSeconds);
            var meeting = _board.GetLatest("ren");
            Write(
                $"BUDGET_TRACE {{\"point\":\"{point}\",\"realSeconds\":{realSeconds},\"gameMinutes\":{gameMinutes},",
                $"\"renRevision\":{_world.GetState("ren").Revision},\"soraRevision\":{_world.GetState("sora").Revision},",
                $"\"requests\":{scheduler.RequestsStarted},\"rolling\":{scheduler.RequestsInLastMinute},",
                $"\"active\":{scheduler.ActiveRequestCount},\"waiting\":{scheduler.WaitingResidentCount},",
                $"\"meeting\":\"{meeting?.State}\",\"speaker\":\"{meeting?.SpeakerId}\",\"lines\":{meeting?.Transcript.Count ?? 0},",
                $"\"renOutcome\":\"{scheduler.GetLastOutcome("ren")?.Code}\",\"finishedThisTick\":{outcomes.Count}}}");
        }

        private static void Write(params FormattableString[] fragments)
            => TestContext.Out.WriteLine(string.Concat(fragments.Select(FormattableString.Invariant)));

        private void AssertAssets()
        {
            foreach (var id in new[] { "ren", "sora" })
            {
                Assert.That(_store.TryGetCharacter(id, out var character), Is.True);
                Assert.That(character.Backpack.Items.Single().Quantity, Is.EqualTo(1));
                Assert.That(character.Wallet.Balance, Is.EqualTo(25));
            }
        }

        private sealed class ControlledClient : INpcDecisionClient
        {
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal readonly List<TaskCompletionSource<NpcDecisionReply>> Replies = new List<TaskCompletionSource<NpcDecisionReply>>();
            internal readonly List<CancellationToken> Tokens = new List<CancellationToken>();

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                Requests.Add(request);
                Tokens.Add(token);
                var reply = new TaskCompletionSource<NpcDecisionReply>();
                Replies.Add(reply);
                Write(
                    $"BUDGET_REQUEST {{\"ordinal\":{Requests.Count},\"npcId\":\"{request.NpcId}\",\"decisionId\":\"{request.DecisionId}\",",
                    $"\"step\":{request.Step},\"gameMinutes\":{request.GameTotalMinutes},\"revision\":{request.Self.Revision},",
                    $"\"phase\":\"{request.Social?.Kind}\",\"triggers\":\"{string.Join(",", request.Triggers.Select(item => item.Kind))}\",",
                    $"\"previousResult\":\"{request.PreviousResultCode}\",\"candidateError\":\"{request.CandidateErrorCode}\"}}");
                return reply.Task;
            }
        }

        private static NpcDecisionReply Say(NpcDecisionRequest request, string text)
            => new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: request.Social.MeetingId, text: text);
        private static NpcDefinition Profile(string id) => new NpcDefinition(id, id, "Resident", "Hello");
        private static NpcDailySchedule Schedule(string id, int afternoon)
            => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest",
                id + ".afternoon", 360, 480, 720, afternoon, 1020, 1080);
        private static WorldTimeProgress Time(double minute, long rebuild = 1)
            => new WorldTimeProgress(new GameClockSnapshot(1, (int)Math.Floor(minute)), minute - Math.Floor(minute), false, rebuild);
        private static CharacterEconomySnapshot Character(string id, int fish, int coins)
            => new CharacterEconomySnapshot(id,
                new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>() : new[] { new ItemStack("fish", fish) }),
                new WalletSnapshot(coins));
    }
}
