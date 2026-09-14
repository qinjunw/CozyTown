#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
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
        public IEnumerator MovingInvitationAccept_PreservesOriginalAndExecutionObservationsThenExpires()
        {
            foreach (var mode in new[] { NpcSpeechMode.FreeText, NpcSpeechMode.StructuredFacts })
                yield return MovingInvitationReply(true, mode);
        }

        [UnityTest]
        public IEnumerator MovingInvitationDecline_PreservesOriginalAndExecutionObservationsAndResumesSchedule()
        {
            foreach (var mode in new[] { NpcSpeechMode.FreeText, NpcSpeechMode.StructuredFacts })
                yield return MovingInvitationReply(false, mode);
        }

        private IEnumerator MovingInvitationReply(bool accept, NpcSpeechMode mode)
        {
            yield return LoadFreshTown(Scenarios[accept ? 0 : 1]);
            var initialAssets = Assets().Select(JsonUtility.ToJson).ToArray();
            Advance(WorldTimeProgress.EffectiveSecondsPerGameMinute);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(736).Within(0.001));
            Assert.That(_ren.Status, Is.EqualTo(TownRouteStatus.Travelling));
            var trial = new Trial();
            var client = new MovingInvitationClient();
            _controller.ConfigureDecisions(new RecordingClient(client, trial),
                DefaultMvpContent.CreateConfiguration().Npcs.Where(profile => profile.Id == Ren || profile.Id == Sora),
                meetingPlans: DefaultNpcResourcePlans.Create(), speechMode: mode);

            double tick = 0;
            for (int i = 0; i < 8 && client.Invitation == null; i++)
            {
                _controller.TickDecisions(tick += 0.01);
                yield return null;
            }
            var request = client.Invitation;
            Assert.That(request, Is.Not.Null, "The actual scene must dispatch Ren's invitation reply while he is travelling.");
            Assert.That(request.SpeechMode, Is.EqualTo(mode));
            Assert.That(request.Observation.RegionId, Is.EqualTo("pond-surroundings"));
            Assert.That(_ren.Status, Is.EqualTo(TownRouteStatus.Travelling));
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Invited));
            var call = trial.calls.Single(item => item.decisionId == request.DecisionId.ToString());
            string originalContextJson = call.contextJson;
            Assert.That(call.executionObservationJson, Is.EqualTo("null"));

            Advance(0.1);
            var beforeExecution = _controller.GetObservation(Ren);
            Assert.That(new Vector2((float)beforeExecution.X, (float)beforeExecution.Y),
                Is.Not.EqualTo(new Vector2((float)request.Observation.X, (float)request.Observation.Y)));
            Assert.That(beforeExecution.RegionId, Is.EqualTo(request.Observation.RegionId));
            Assert.That(beforeExecution.SpaceId, Is.EqualTo(request.Observation.SpaceId));
            Assert.That(beforeExecution.NearbyEntityIds, Is.EqualTo(request.Observation.NearbyEntityIds));
            Assert.That(beforeExecution.Facts.Select(FactContents), Is.EqualTo(request.Observation.Facts.Select(FactContents)),
                "The fixture must reproduce coordinate movement without changing the facts supplied to the decision.");
            Assert.That(_controller.GetAgentState(Ren).Revision, Is.EqualTo(request.Self.Revision));
            var candidate = new NpcDecisionReply(accept ? NpcDecisionKind.AcceptInvitation : NpcDecisionKind.DeclineInvitation,
                meetingId: request.Social.MeetingId);
            client.Reply.SetResult(candidate);
            for (int i = 0; i < 8 && _controller.GetDecisionOutcome(Ren)?.DecisionId != request.DecisionId; i++)
            {
                _controller.TickDecisions(tick += 0.01);
                yield return null;
            }

            var meeting = _controller.GetMeeting(Ren);
            Assert.That(meeting.State, Is.EqualTo(accept ? NpcMeetingState.Scheduled : NpcMeetingState.Declined));
            var outcome = _controller.GetDecisionOutcome(Ren);
            TestContext.Out.WriteLine("Moving reply dispatch ({0}, accept={1}): {2}", mode, accept,
                SerializeExecutionObservation(request.Observation));
            TestContext.Out.WriteLine("Moving reply before execution ({0}, accept={1}): {2}", mode, accept,
                SerializeExecutionObservation(beforeExecution));
            TestContext.Out.WriteLine("Moving reply result ({0}, accept={1}): {2}", mode, accept, outcome?.Code);
            Assert.That(outcome?.Code, Is.EqualTo(accept ? "meeting.accept_invite" : "meeting.decline_invite"));
            Assert.That(outcome.Reply, Is.SameAs(candidate));
            Assert.That(outcome.Context.Observation, Is.SameAs(request.Observation));
            Assert.That(outcome.ExecutionObservation, Is.Not.Null);
            Assert.That(outcome.ExecutionObservation, Is.Not.SameAs(request.Observation));
            Assert.That(outcome.ExecutionObservation.X, Is.EqualTo(beforeExecution.X));
            Assert.That(outcome.ExecutionObservation.Y, Is.EqualTo(beforeExecution.Y));
            Assert.That(outcome.ExecutionObservation.ObservedAtTotalMinutes, Is.EqualTo(beforeExecution.ObservedAtTotalMinutes));
            Assert.That(outcome.ExecutionObservation.ListenerId, Is.EqualTo(Sora));
            Assert.That(outcome.Calls, Is.EqualTo(1));
            Assert.That(client.Requests.Count(item => item.NpcId == Ren), Is.EqualTo(1));
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(_controller.ActiveDecisionRequests, Is.Zero);
            CaptureOutcomes(trial);
            Assert.That(call.contextJson, Is.EqualTo(originalContextJson));
            Assert.That(call.executionObservationJson, Is.Not.EqualTo("null"));
            Assert.That(call.executionObservationJson, Does.Contain("\"value\":null"),
                "An unknown pond quantity must retain its null value in the execution evidence.");
            var recorded = JsonUtility.FromJson<ExecutionObservationRecord>(call.executionObservationJson);
            Assert.That(recorded.observerId, Is.EqualTo(Ren));
            Assert.That(recorded.worldRunId, Is.EqualTo(request.Self.WorldRunId.ToString("N")));
            Assert.That(recorded.x, Is.EqualTo(beforeExecution.X));
            Assert.That(recorded.y, Is.EqualTo(beforeExecution.Y));
            Assert.That(recorded.facts.Length, Is.EqualTo(outcome.ExecutionObservation.Facts.Count));
            Assert.That(recorded.facts.Single(fact => fact.factId == Ren + ":owned_quantity").value,
                Is.EqualTo(accept ? "2" : "0"));
            Assert.That(trial.calls.All(item => item.executionObservationJson != "null"), Is.True);
            Assert.That(meeting.Transcript, Is.Empty);
            Assert.That(meeting.DeliveryResultCode, Is.Null);
            Assert.That(_controller.GetComponent<NpcMeetingDialogueView>().VisibleText, Is.Null.Or.Empty);
            foreach (string npcId in new[] { Ren, Sora })
                Assert.That(_controller.GetMeetingMemories(npcId).Select(memory => memory.Kind),
                    Is.EqualTo(new[] { "meeting.invited", accept ? "meeting.accepted" : "meeting.declined" }));
            Assert.That(Assets().Select(JsonUtility.ToJson), Is.EqualTo(initialAssets));

            if (accept)
            {
                Advance((780 - _controller.GameTotalMinutes) * WorldTimeProgress.EffectiveSecondsPerGameMinute);
                Assert.That(_sora.Status, Is.EqualTo(TownRouteStatus.Travelling));
                Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Travelling));
                _controller.TickDecisions(tick += 0.01);
                Assert.That(client.Requests.Count, Is.EqualTo(2), "Travel must not dispatch a delivery decision before both residents arrive.");
                Assert.That(_controller.GetMeeting(Ren).DeliveryResultCode, Is.Null);
                Assert.That(Assets().Select(JsonUtility.ToJson), Is.EqualTo(initialAssets));
            }

            Advance((931 - _controller.GameTotalMinutes) * WorldTimeProgress.EffectiveSecondsPerGameMinute);
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(accept ? NpcMeetingState.Expired : NpcMeetingState.Declined));
            foreach (string npcId in new[] { Ren, Sora })
            {
                Assert.That(_controller.GetAgentState(npcId).ActiveActivity, Is.Null);
                Assert.That(_controller.GetMeetingMemories(npcId).Any(memory => memory.Kind == "meeting.spoken"), Is.False);
            }
            Assert.That(_controller.GetMeeting(Ren).Transcript, Is.Empty);
            Assert.That(_controller.GetMeeting(Ren).DeliveryResultCode, Is.Null);
            Advance((1000 - _controller.GameTotalMinutes) * WorldTimeProgress.EffectiveSecondsPerGameMinute);
            foreach (var resident in new[] { _ren, _sora })
            {
                Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Arrived));
                Assert.That(resident.TargetLocationId, Does.StartWith("work."));
                Assert.That(_controller.GetAgentState(resident.NpcId).ActiveActivity, Is.Null);
            }
            Assert.That(Assets().Select(JsonUtility.ToJson), Is.EqualTo(initialAssets));
            Assert.That(client.Requests.Count(item => item.NpcId == Ren), Is.EqualTo(1));
            yield return UnloadTown();
        }

        private static object FactContents(NpcObservationFact fact) => new {
            fact.FactId, fact.EntityId, fact.Predicate, fact.Value, fact.ValueType, fact.Unit,
            fact.Knowledge, fact.Source, fact.ObserverId, fact.Scope, fact.CanExpress, fact.SpeakerId };

        private sealed class MovingInvitationClient : INpcDecisionClient
        {
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal readonly TaskCompletionSource<NpcDecisionReply> Reply = new TaskCompletionSource<NpcDecisionReply>();
            internal NpcDecisionRequest Invitation;

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                Requests.Add(request);
                if (request.NpcId == Sora && request.Social?.Kind == NpcSocialContextKind.Opportunity)
                    return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Invite, planId: request.Social.PlanId));
                if (request.NpcId == Ren && request.Social?.Kind == NpcSocialContextKind.Invitation)
                {
                    Invitation = request;
                    return Reply.Task;
                }
                return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            }
        }

        private static string SerializeExecutionObservation(NpcLocalObservation observation)
        {
            if (observation == null) return "null";
            var record = new ExecutionObservationRecord {
                observerId = observation.ObserverId, worldRunId = observation.WorldRunId.ToString("N"),
                observedAtTotalMinutes = observation.ObservedAtTotalMinutes, x = observation.X, y = observation.Y,
                spaceId = observation.SpaceId, regionId = observation.RegionId,
                nearbyEntityIds = observation.NearbyEntityIds.ToArray(), radius = observation.Radius,
                nearbyComplete = observation.NearbyComplete, coverageDomain = observation.CoverageDomain,
                listenerId = observation.ListenerId,
                facts = observation.Facts.Select(fact => new ExecutionFactRecord {
                    factId = fact.FactId, entityId = fact.EntityId, predicate = fact.Predicate, value = fact.Value,
                    valueType = fact.ValueType, unit = fact.Unit, knowledge = fact.Knowledge, source = fact.Source,
                    observerId = fact.ObserverId, observedAtTotalMinutes = fact.ObservedAtTotalMinutes,
                    scope = fact.Scope, canExpress = fact.CanExpress, speakerId = fact.SpeakerId }).ToArray() };
            using var stream = new MemoryStream();
            new DataContractJsonSerializer(typeof(ExecutionObservationRecord)).WriteObject(stream, record);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        [Serializable, DataContract] private sealed class ExecutionObservationRecord
        {
            [DataMember] public string observerId, worldRunId, spaceId, regionId, coverageDomain, listenerId;
            [DataMember] public double observedAtTotalMinutes, x, y, radius;
            [DataMember] public bool nearbyComplete;
            [DataMember] public string[] nearbyEntityIds;
            [DataMember] public ExecutionFactRecord[] facts;
        }

        [Serializable, DataContract] private sealed class ExecutionFactRecord
        {
            [DataMember] public string factId, entityId, predicate, value, valueType, unit, knowledge, source, observerId, scope, speakerId;
            [DataMember] public double observedAtTotalMinutes;
            [DataMember] public bool canExpress;
        }
    }
}
#endif
