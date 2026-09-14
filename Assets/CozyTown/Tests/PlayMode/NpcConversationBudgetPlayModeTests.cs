#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed partial class NpcResourceScenarioPlayModeTests
    {
        [UnityTest]
        public IEnumerator ConversationScheduleChange_WithEightCallBudget_RebuildsCurrentRequestAndCompletesFourTurns()
            => ConversationBudgetRecovery(NpcSpeechMode.FreeText);

        [UnityTest]
        public IEnumerator StructuredConversationScheduleChange_WithEightCallBudget_RebuildsCurrentRequestAndCompletesFourTurns()
            => ConversationBudgetRecovery(NpcSpeechMode.StructuredFacts);

        private IEnumerator ConversationBudgetRecovery(NpcSpeechMode mode)
        {
            yield return LoadFreshTown(Scenarios[0]);
            var trial = new Trial { initialAssets = Assets() };
            using var client = new ConversationBudgetClient(() => BudgetRealSeconds);
            _controller.ConfigureDecisions(client,
                DefaultMvpContent.CreateConfiguration().Npcs.Where(profile => profile.Id == Ren || profile.Id == Sora),
                meetingPlans: DefaultNpcResourcePlans.Create(), speechMode: mode);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(735).Within(0.001));
            Assert.That(_controller.GetMeetingMemories(Ren), Is.Empty);
            Assert.That(_controller.GetMeetingMemories(Sora), Is.Empty);
            Assert.That(_ren.Status, Is.EqualTo(TownRouteStatus.Travelling));
            BudgetPump(client, 1);
            Assert.That(client.Calls[0].Request.Social.Kind, Is.EqualTo(NpcSocialContextKind.Opportunity));
            client.CompleteJson(1, "{\"schemaVersion\":4,\"operation\":\"invite\"}");
            BudgetPump(client, 2);
            Assert.That(client.Calls[1].Request.DecisionId, Is.EqualTo(client.Calls[0].Request.DecisionId));
            Assert.That(client.Calls[1].Request.Step, Is.EqualTo(2));
            Assert.That(client.Calls[1].Request.CandidateErrorCode, Is.EqualTo("candidate.plan_id_required"));
            client.CompleteRule(2);
            BudgetPump(client, 3);
            Assert.That(client.Calls[2].Request.NpcId, Is.EqualTo(Ren));
            client.CompleteRule(3);
            _controller.TickDecisions(BudgetRealSeconds);
            BudgetCheckpoint("accepted_before_future_meeting", client);
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Scheduled));
            Assert.That(_controller.GetDecisionOutcome(Sora).CandidateErrorCodes,
                Is.EqualTo(new[] { "candidate.plan_id_required" }));

            BudgetAdvanceTo(780);
            BudgetCheckpoint("meeting_started_with_body_still_travelling", client);
            Assert.That(_sora.Status, Is.EqualTo(TownRouteStatus.Travelling));
            Assert.That(client.Calls.Count, Is.EqualTo(3));
            Assert.That(_controller.GetMeeting(Ren).DeliveryResultCode, Is.Not.EqualTo("resource.delivered"));
            BudgetAdvanceTo(790);
            BudgetPump(client, 4);
            Assert.That(AtMeetingLocation(_ren, "rest.fisher_ren"), Is.True);
            Assert.That(AtMeetingLocation(_sora, "road.west_lane"), Is.True);
            Assert.That(client.Calls[3].Request.Social.Kind, Is.EqualTo(NpcSocialContextKind.Delivery));
            client.CompleteRule(4);
            BudgetPump(client, 5);
            client.CompleteSpeech(5, "The agreed carp has been delivered.");
            BudgetPump(client, 6);
            client.CompleteSpeech(6, "I received the agreed payment.");
            BudgetPump(client, 7);
            Assert.That(client.Calls[6].Request.NpcId, Is.EqualTo(Sora));
            Assert.That(_controller.GetMeeting(Ren).Transcript.Count, Is.EqualTo(2));
            BudgetAdvanceTo(794.7);
            client.CompleteSpeech(7, "Thank you for this exchange.");
            BudgetPump(client, 8);
            var delayed = client.Calls[7];
            string originalContext = new ProxyNpcDecisionJsonCodec().SerializeRequest(delayed.Request);
            Assert.That(delayed.Request.NpcId, Is.EqualTo(Ren));
            Assert.That(delayed.Request.SpeechMode, Is.EqualTo(mode));
            Assert.That(delayed.Request.Social.Kind, Is.EqualTo(NpcSocialContextKind.Conversation));
            Assert.That(delayed.Request.GameTotalMinutes, Is.LessThan(794.8));
            BudgetCheckpoint("eighth_request_before_ren_schedule_boundary", client);
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(8));
            AssertBudgetTalkingWithThreeLines();
            CheckAssets(trial, Scenarios[0], Observe(BudgetRealSeconds));
            var deliveredAssets = Assets().Select(JsonUtility.ToJson).ToArray();

            BudgetAdvanceTo(795.1);
            double boundaryObservedAt = _controller.GameTotalMinutes;
            BudgetCheckpoint("ren_revision_changed_old_request_cancelled_and_replacement_waiting", client);
            Assert.That(delayed.CancellationObserved, Is.True);
            Assert.That(_controller.GetAgentState(Ren).Revision, Is.GreaterThan(delayed.Request.Self.Revision));
            var cancelled = _controller.GetDecisionOutcome(Ren);
            Assert.That(cancelled.DecisionId, Is.EqualTo(delayed.Request.DecisionId));
            Assert.That(cancelled.Code, Is.EqualTo("agent.decision_stale"));
            Assert.That(cancelled.ExecutionObservation, Is.Null);
            Assert.That(_controller.WaitingDecisionResidents, Is.EqualTo(1));
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            BudgetAdvanceTo(795.2);
            string discarded = mode == NpcSpeechMode.StructuredFacts ? "I would like to learn about cooking."
                : "This fourth line arrived after its request was cancelled.";
            client.CompleteSpeech(8, discarded, new NpcSpeechFrame("express_wish", "@learn_cooking", "neutral"));
            _controller.TickDecisions(BudgetRealSeconds);
            BudgetCheckpoint("cancelled_reply_returned_without_publication", client);
            Assert.That(_controller.ActiveDecisionRequests, Is.Zero);
            Assert.That(_controller.WaitingDecisionResidents, Is.EqualTo(1));
            AssertBudgetTalkingWithThreeLines();
            Assert.That(_controller.GetObservation(Ren).Facts.Any(fact => fact.Knowledge == "statement" && fact.Value == discarded), Is.False);

            BudgetAdvanceTo(824.9);
            BudgetCheckpoint("replacement_still_waiting_before_thirty_game_minutes", client);
            Assert.That(_controller.WaitingDecisionResidents, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(8));
            BudgetAdvanceTo(825.2);
            BudgetCheckpoint("turn_still_waiting_after_original_opportunity_lifetime", client);
            Assert.That(_controller.WaitingDecisionResidents, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(8));
            long soraRevision = _controller.GetAgentState(Sora).Revision;
            BudgetAdvanceTo(840.1);
            BudgetCheckpoint("sora_schedule_boundary_does_not_dispatch_rens_turn", client);
            Assert.That(_controller.GetAgentState(Sora).Revision, Is.GreaterThan(soraRevision));
            Assert.That(_controller.WaitingDecisionResidents, Is.EqualTo(1));
            Assert.That(client.Calls.Count, Is.EqualTo(8));
            BudgetAdvanceTo(855.2);
            BudgetCheckpoint("budget_reopened_and_ninth_request_uses_current_dispatch_snapshot", client);
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(6));
            Assert.That(client.Calls.Count, Is.EqualTo(9));
            Assert.That(_controller.DecisionRequestsStarted, Is.EqualTo(9));
            Assert.That(_controller.WaitingDecisionResidents, Is.Zero);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            var replacement = client.Calls[8];
            Assert.That(replacement.Request.NpcId, Is.EqualTo(Ren));
            Assert.That(replacement.Request.DecisionId, Is.Not.EqualTo(delayed.Request.DecisionId));
            Assert.That(replacement.Request.Step, Is.EqualTo(1));
            Assert.That(replacement.Request.PreviousResultCode, Is.EqualTo("agent.decision_stale"));
            Assert.That(replacement.Request.CandidateErrorCode, Is.Null);
            Assert.That(replacement.Request.GameTotalMinutes, Is.EqualTo(_controller.GameTotalMinutes));
            Assert.That(replacement.Request.Self.WorldRunId, Is.EqualTo(_controller.GetAgentState(Ren).WorldRunId));
            Assert.That(replacement.Request.Self.Revision, Is.EqualTo(_controller.GetAgentState(Ren).Revision));
            Assert.That(replacement.Request.Self.Target.TargetLocationId, Is.EqualTo(_controller.GetAgentState(Ren).Target.TargetLocationId));
            Assert.That(replacement.Request.Self.ActiveActivity, Is.SameAs(_controller.GetAgentState(Ren).ActiveActivity));
            Assert.That(SerializeExecutionObservation(replacement.Request.Observation),
                Is.EqualTo(SerializeExecutionObservation(_controller.GetObservation(Ren))));
            Assert.That(replacement.Request.Triggers.Any(trigger => trigger.Kind == NpcAgentEventKind.ScheduleChanged
                && trigger.TotalMinutes >= 795 && trigger.TotalMinutes <= boundaryObservedAt), Is.True);
            Assert.That(replacement.Request.Social.Transcript.Count, Is.EqualTo(3));
            Assert.That(new ProxyNpcDecisionJsonCodec().SerializeRequest(delayed.Request), Is.EqualTo(originalContext));
            AssertBudgetTalkingWithThreeLines();
            BudgetAdvanceTo(855.4);
            Assert.That(_controller.GameTotalMinutes, Is.GreaterThan(replacement.Request.GameTotalMinutes));
            string finalLine = mode == NpcSpeechMode.StructuredFacts ? "I am in 池塘周边."
                : "This new decision acknowledges the completed exchange.";
            client.CompleteSpeech(9, finalLine, new NpcSpeechFrame("report_observation", Ren + ":region_name", "neutral"));
            _controller.TickDecisions(BudgetRealSeconds);
            BudgetCheckpoint("delayed_ninth_reply_publishes_fourth_line_and_completes_meeting", client);
            var meeting = _controller.GetMeeting(Ren);
            Assert.That(meeting.State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(meeting.Transcript.Count, Is.EqualTo(4));
            Assert.That(meeting.Transcript.Last().Text, Is.EqualTo(finalLine));
            Assert.That(meeting.Transcript.Any(line => line.Text == discarded), Is.False);
            var completed = _controller.GetDecisionOutcome(Ren);
            Assert.That(completed.DecisionId, Is.EqualTo(replacement.Request.DecisionId));
            Assert.That(completed.Code, Is.EqualTo("meeting.say"));
            Assert.That(completed.ExecutionObservation.ObservedAtTotalMinutes, Is.GreaterThan(replacement.Request.Observation.ObservedAtTotalMinutes));
            Assert.That(client.Calls.Count(call => call.Reply?.Kind == NpcDecisionKind.Deliver), Is.EqualTo(1));
            Assert.That(client.Calls.Any(call => call.Reply?.Kind == NpcDecisionKind.EndConversation), Is.False);
            Assert.That(_controller.ActiveDecisionRequests, Is.Zero);
            foreach (string id in new[] { Ren, Sora })
                Assert.That(_controller.GetAgentState(id).ActiveActivity, Is.Null);
            // Recovery advances the bound world and actual bodies after model dispatch has stopped.
            BudgetAdvanceTo(1000, dispatch: false);
            BudgetCheckpoint("default_work_schedule_recovered_without_further_dispatch", client);
            foreach (var resident in new[] { _ren, _sora })
            {
                Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Arrived));
                Assert.That(resident.TargetLocationId, Does.StartWith("work."));
                Assert.That(_controller.GetAgentState(resident.NpcId).ActiveActivity, Is.Null);
                var memories = _controller.GetMeetingMemories(resident.NpcId);
                Assert.That(memories.Count(memory => memory.Kind == "meeting.spoken"), Is.EqualTo(4));
                Assert.That(memories.Any(memory => memory.Text == discarded), Is.False);
            }
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Completed));
            CheckAssets(trial, Scenarios[0], Observe(BudgetRealSeconds));
            Assert.That(trial.hostViolations, Is.Empty);
            Assert.That(Assets().Select(JsonUtility.ToJson), Is.EqualTo(deliveredAssets));
            Assert.That(client.Calls.Count, Is.EqualTo(9));
            TestContext.WriteLine("conversation-budget-observation-limit: pending request identity and triggers are not exposed; checkpoints record public counters, revisions and dispatched request/outcome triggers without consuming events.");
            yield return UnloadTown();
        }

        [UnityTest]
        public IEnumerator ConversationScheduleChange_WithOneBudgetSlotRemaining_DispatchesNewDecisionAndCompletesFourTurns()
            => ConversationBudgetSlotControl(NpcSpeechMode.FreeText);

        [UnityTest]
        public IEnumerator StructuredConversationScheduleChange_WithOneBudgetSlotRemaining_DispatchesNewDecisionAndCompletesFourTurns()
            => ConversationBudgetSlotControl(NpcSpeechMode.StructuredFacts);

        private IEnumerator ConversationBudgetSlotControl(NpcSpeechMode mode)
        {
            yield return LoadFreshTown(Scenarios[0]);
            var trial = new Trial { initialAssets = Assets() };
            using var client = new ConversationBudgetClient(() => BudgetRealSeconds);
            _controller.ConfigureDecisions(client,
                DefaultMvpContent.CreateConfiguration().Npcs.Where(profile => profile.Id == Ren || profile.Id == Sora),
                meetingPlans: DefaultNpcResourcePlans.Create(), speechMode: mode);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(735).Within(0.001));
            Assert.That(_controller.GetMeetingMemories(Ren), Is.Empty);
            Assert.That(_controller.GetMeetingMemories(Sora), Is.Empty);
            Assert.That(_ren.Status, Is.EqualTo(TownRouteStatus.Travelling));
            BudgetPump(client, 1);
            client.CompleteRule(1);
            BudgetPump(client, 2);
            client.CompleteRule(2);
            _controller.TickDecisions(BudgetRealSeconds);
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Scheduled));
            BudgetAdvanceTo(780);
            Assert.That(_sora.Status, Is.EqualTo(TownRouteStatus.Travelling));
            Assert.That(client.Calls.Count, Is.EqualTo(2));
            Assert.That(_controller.GetMeeting(Ren).DeliveryResultCode, Is.Not.EqualTo("resource.delivered"));
            BudgetAdvanceTo(790);
            BudgetPump(client, 3);
            Assert.That(AtMeetingLocation(_ren, "rest.fisher_ren"), Is.True);
            Assert.That(AtMeetingLocation(_sora, "road.west_lane"), Is.True);
            Assert.That(client.Calls[2].Request.Social.Kind, Is.EqualTo(NpcSocialContextKind.Delivery));
            client.CompleteRule(3);
            BudgetPump(client, 4);
            client.CompleteSpeech(4, "The agreed carp has been delivered.");
            BudgetPump(client, 5);
            client.CompleteSpeech(5, "I received the agreed payment.");
            BudgetPump(client, 6);
            BudgetAdvanceTo(794.7);
            client.CompleteSpeech(6, "Thank you for this exchange.");
            BudgetPump(client, 7);
            var delayed = client.Calls[6];
            Assert.That(delayed.Request.NpcId, Is.EqualTo(Ren));
            Assert.That(delayed.Request.SpeechMode, Is.EqualTo(mode));
            Assert.That(delayed.Request.GameTotalMinutes, Is.LessThan(794.8));
            BudgetCheckpoint("control_seventh_request_with_one_budget_slot_remaining", client);
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(7));
            AssertBudgetTalkingWithThreeLines();
            CheckAssets(trial, Scenarios[0], Observe(BudgetRealSeconds));
            var deliveredAssets = Assets().Select(JsonUtility.ToJson).ToArray();

            BudgetAdvanceTo(795.1);
            double boundaryObservedAt = _controller.GameTotalMinutes;
            BudgetCheckpoint("control_cancelled_request_keeps_replacement_waiting_until_response_returns", client);
            Assert.That(delayed.CancellationObserved, Is.True);
            var cancelled = _controller.GetDecisionOutcome(Ren);
            Assert.That(cancelled.DecisionId, Is.EqualTo(delayed.Request.DecisionId));
            Assert.That(cancelled.Code, Is.EqualTo("agent.decision_stale"));
            Assert.That(cancelled.ExecutionObservation, Is.Null);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_controller.WaitingDecisionResidents, Is.EqualTo(1));
            Assert.That(client.Calls.Count, Is.EqualTo(7));
            BudgetAdvanceTo(795.2);
            string discarded = mode == NpcSpeechMode.StructuredFacts ? "I would like to learn about cooking."
                : "This fourth line arrived after its request was cancelled.";
            client.CompleteSpeech(7, discarded, new NpcSpeechFrame("express_wish", "@learn_cooking", "neutral"));
            BudgetPump(client, 8);
            var replacement = client.Calls[7];
            BudgetCheckpoint("control_eighth_request_is_a_new_schedule_triggered_decision", client);
            Assert.That(replacement.Request.NpcId, Is.EqualTo(Ren));
            Assert.That(replacement.Request.DecisionId, Is.Not.EqualTo(delayed.Request.DecisionId));
            Assert.That(replacement.Request.Step, Is.EqualTo(1));
            Assert.That(replacement.Request.Self.Revision, Is.GreaterThan(delayed.Request.Self.Revision));
            Assert.That(replacement.Request.PreviousResultCode, Is.EqualTo("agent.decision_stale"));
            Assert.That(replacement.Request.CandidateErrorCode, Is.Null);
            Assert.That(replacement.Request.GameTotalMinutes, Is.EqualTo(_controller.GameTotalMinutes));
            Assert.That(replacement.Request.Triggers.Any(trigger => trigger.Kind == NpcAgentEventKind.ScheduleChanged
                && trigger.TotalMinutes >= 795 && trigger.TotalMinutes <= boundaryObservedAt), Is.True);
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(8));
            Assert.That(_controller.WaitingDecisionResidents, Is.Zero);
            AssertBudgetTalkingWithThreeLines();
            string finalLine = mode == NpcSpeechMode.StructuredFacts ? "I am in 池塘周边."
                : "This new decision acknowledges the completed exchange.";
            client.CompleteSpeech(8, finalLine, new NpcSpeechFrame("report_observation", Ren + ":region_name", "neutral"));
            _controller.TickDecisions(BudgetRealSeconds);
            BudgetCheckpoint("control_fourth_published_line_reaches_host_turn_limit", client);
            var meeting = _controller.GetMeeting(Ren);
            Assert.That(meeting.State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(meeting.Transcript.Count, Is.EqualTo(4));
            Assert.That(meeting.Transcript.Last().Text, Is.EqualTo(finalLine));
            Assert.That(meeting.Transcript.Any(line => line.Text == discarded), Is.False);
            Assert.That(_controller.GetDecisionOutcome(Ren).DecisionId, Is.EqualTo(replacement.Request.DecisionId));
            Assert.That(_controller.GetDecisionOutcome(Ren).Code, Is.EqualTo("meeting.say"));
            Assert.That(client.Calls.Count(call => call.Reply?.Kind == NpcDecisionKind.Deliver), Is.EqualTo(1));
            Assert.That(client.Calls.Any(call => call.Reply?.Kind == NpcDecisionKind.EndConversation), Is.False);
            Assert.That(client.Calls.All(call => call.Request.Step == 1 && call.CandidateErrorCode == null), Is.True);
            Assert.That(_controller.ActiveDecisionRequests, Is.Zero);

            BudgetAdvanceTo(1000, dispatch: false);
            BudgetCheckpoint("control_default_work_schedule_recovered_after_dispatch_stopped", client);
            foreach (var resident in new[] { _ren, _sora })
            {
                Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Arrived));
                Assert.That(resident.TargetLocationId, Does.StartWith("work."));
                Assert.That(_controller.GetAgentState(resident.NpcId).ActiveActivity, Is.Null);
                var memories = _controller.GetMeetingMemories(resident.NpcId);
                Assert.That(memories.Count(memory => memory.Kind == "meeting.spoken"), Is.EqualTo(4));
                Assert.That(memories.Any(memory => memory.Text == discarded), Is.False);
            }
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Completed));
            CheckAssets(trial, Scenarios[0], Observe(BudgetRealSeconds));
            Assert.That(trial.hostViolations, Is.Empty);
            Assert.That(Assets().Select(JsonUtility.ToJson), Is.EqualTo(deliveredAssets));
            Assert.That(_controller.DecisionRequestsStarted, Is.EqualTo(8));
            Assert.That(client.Calls.Count, Is.EqualTo(8));
            yield return UnloadTown();
        }

        // The controller receives controlled monotonic seconds; Advance moves the actual scene at the default time ratio.
        private double BudgetRealSeconds => (_controller.GameTotalMinutes - 735) * WorldTimeProgress.EffectiveSecondsPerGameMinute;

        private void BudgetAdvanceTo(double minute, bool dispatch = true)
        {
            Assert.That(minute, Is.GreaterThanOrEqualTo(_controller.GameTotalMinutes));
            Advance((minute - _controller.GameTotalMinutes) * WorldTimeProgress.EffectiveSecondsPerGameMinute);
            if (dispatch) _controller.TickDecisions(BudgetRealSeconds);
        }

        private void BudgetPump(ConversationBudgetClient client, int count)
        {
            for (int i = 0; i < 8 && client.Calls.Count < count; i++) _controller.TickDecisions(BudgetRealSeconds);
            Assert.That(client.Calls.Count, Is.EqualTo(count), "Unexpected controlled-client request sequence.");
        }

        private void AssertBudgetTalkingWithThreeLines()
        {
            var meeting = _controller.GetMeeting(Ren);
            Assert.That(meeting.State, Is.EqualTo(NpcMeetingState.Talking));
            Assert.That(meeting.SpeakerId, Is.EqualTo(Ren));
            Assert.That(meeting.Transcript.Count, Is.EqualTo(3));
            Assert.That(meeting.DeliveryResultCode, Is.EqualTo("resource.delivered"));
        }

        private void BudgetCheckpoint(string label, ConversationBudgetClient client)
        {
            var meeting = _controller.GetMeeting(Ren);
            var record = new ConversationBudgetCheckpoint {
                label = label, realSeconds = BudgetRealSeconds, gameMinutes = _controller.GameTotalMinutes,
                worldRunId = _controller.GetAgentState(Ren).WorldRunId.ToString("N"), speechMode = client.Calls.FirstOrDefault()?.Request.SpeechMode.ToString(),
                requestsStarted = _controller.DecisionRequestsStarted, requestsInLastMinute = _controller.DecisionRequestsInLastMinute,
                activeRequests = _controller.ActiveDecisionRequests, waitingResidents = _controller.WaitingDecisionResidents,
                renRevision = _controller.GetAgentState(Ren).Revision, soraRevision = _controller.GetAgentState(Sora).Revision,
                meetingState = meeting?.State.ToString(), speakerId = meeting?.SpeakerId,
                scene = Observe(BudgetRealSeconds), assets = Assets(),
                outcomes = new[] { Ren, Sora }.Select(id => _controller.GetDecisionOutcome(id)).Where(o => o != null)
                    .Select(outcome => new ConversationBudgetOutcome { npcId = outcome.NpcId, decisionId = outcome.DecisionId.ToString("N"),
                        code = outcome.Code, step = outcome.Context.Step, revision = outcome.Context.Self.Revision,
                        triggers = BudgetEvents(outcome.Context), executionObservationJson = SerializeExecutionObservation(outcome.ExecutionObservation) }).ToArray(),
                requests = client.Calls.Select(call => new ConversationBudgetRequest {
                    npcId = call.Request.NpcId, decisionId = call.Request.DecisionId.ToString("N"), step = call.Request.Step,
                    revision = call.Request.Self.Revision, dispatchedAtRealSeconds = call.RealSeconds,
                    gameMinutes = call.Request.GameTotalMinutes, phase = call.Request.Social?.Kind.ToString(),
                    triggers = BudgetEvents(call.Request), contextJson = new ProxyNpcDecisionJsonCodec().SerializeRequest(call.Request),
                    rawCandidate = call.RawCandidate, candidateErrorCode = call.CandidateErrorCode,
                    returnedOperation = call.Reply?.Operation, returnedText = call.Reply?.Text,
                    returnedSpeechFrame = call.Reply?.SpeechFrame == null ? null : new SpeechFrameRecord {
                        speechIntent = call.Reply.SpeechFrame.Intent, factId = call.Reply.SpeechFrame.FactId, tone = call.Reply.SpeechFrame.Tone },
                    cancellationObserved = call.CancellationObserved, responseCompleted = call.Completion.Task.IsCompleted }).ToArray()
            };
            TestContext.WriteLine("conversation-budget-checkpoint " + JsonUtility.ToJson(record));
        }

        private static string[] BudgetEvents(NpcDecisionRequest request)
            => request.Triggers.Select(trigger => trigger.Kind + "@" + trigger.TotalMinutes.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        private sealed class ConversationBudgetClient : INpcDecisionClient, IDisposable
        {
            private readonly Func<double> _realSeconds;
            internal readonly List<ConversationBudgetCall> Calls = new List<ConversationBudgetCall>();
            internal ConversationBudgetClient(Func<double> realSeconds) => _realSeconds = realSeconds;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                var call = new ConversationBudgetCall { Request = request, RealSeconds = _realSeconds() };
                call.Registration = token.Register(() => call.CancellationObserved = true);
                Calls.Add(call);
                return call.Completion.Task;
            }
            internal void CompleteJson(int number, string json)
            {
                var call = Calls[number - 1];
                call.RawCandidate = json;
                try { Complete(number, new ProxyNpcDecisionJsonCodec().ParseResponse(json, call.Request)); }
                catch (NpcCandidateException error) { call.CandidateErrorCode = error.Code; call.Completion.SetException(error); }
            }
            internal void CompleteRule(int number)
            {
                var request = Calls[number - 1].Request;
                Complete(number, new RuleClient(false).DecideAsync(request, CancellationToken.None).GetAwaiter().GetResult());
            }
            internal void CompleteSpeech(int number, string text, NpcSpeechFrame frame = null)
            {
                var request = Calls[number - 1].Request;
                if (request.SpeechMode == NpcSpeechMode.StructuredFacts)
                {
                    frame ??= new NpcSpeechFrame("express_wish", "@talk", "neutral");
                    Assert.That(NpcFactSpeech.TryRender(request.Observation, frame, out _, out var error), Is.True, error);
                    Complete(number, new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: request.Social.MeetingId, speechFrame: frame));
                }
                else Complete(number, new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: request.Social.MeetingId, text: text));
            }
            private void Complete(int number, NpcDecisionReply reply)
            {
                var call = Calls[number - 1];
                call.Reply = reply;
                call.Completion.SetResult(reply);
            }
            public void Dispose()
            {
                foreach (var call in Calls) { call.Registration.Dispose(); call.Completion.TrySetCanceled(); }
            }
        }

        private sealed class ConversationBudgetCall
        {
            internal NpcDecisionRequest Request;
            internal NpcDecisionReply Reply;
            internal double RealSeconds;
            internal bool CancellationObserved;
            internal string RawCandidate, CandidateErrorCode;
            internal CancellationTokenRegistration Registration;
            internal readonly TaskCompletionSource<NpcDecisionReply> Completion = new TaskCompletionSource<NpcDecisionReply>();
        }

        [Serializable] private sealed class ConversationBudgetCheckpoint
        {
            public string label, meetingState, speakerId, worldRunId, speechMode;
            public double realSeconds, gameMinutes;
            public long requestsStarted, renRevision, soraRevision;
            public int requestsInLastMinute, activeRequests, waitingResidents;
            public Observation scene;
            public AssetRow[] assets;
            public ConversationBudgetOutcome[] outcomes;
            public ConversationBudgetRequest[] requests;
        }
        [Serializable] private sealed class ConversationBudgetOutcome
        {
            public string npcId, decisionId, code, executionObservationJson;
            public int step;
            public long revision;
            public string[] triggers;
        }
        [Serializable] private sealed class ConversationBudgetRequest
        {
            public string npcId, decisionId, phase, contextJson, rawCandidate, candidateErrorCode, returnedOperation, returnedText;
            public int step;
            public long revision;
            public double dispatchedAtRealSeconds, gameMinutes;
            public bool cancellationObserved, responseCompleted;
            public string[] triggers;
            public SpeechFrameRecord returnedSpeechFrame;
        }
    }
}
#endif
