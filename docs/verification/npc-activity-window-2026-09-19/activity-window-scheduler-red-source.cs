using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcActivityWindowDecisionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DirectClient_ExceedingDisclosedDurationUsesOnlyOneCorrection(bool repeatInvalidDuration)
        {
            var world = World(779, 0.5, "mina");
            using var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client,
                new NpcDecisionSettings(maxCallsPerDecision: 4));
            scheduler.Tick(0);
            var first = client.Requests[0];
            client.Complete(0, Visit("mina", 1));
            world.Observe(Time(779, 0.75));

            scheduler.Tick(1);

            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var correction = client.Requests[1];
            Assert.That(correction.DecisionId, Is.EqualTo(first.DecisionId));
            Assert.That(correction.GameTotalMinutes, Is.EqualTo(779.5));
            Assert.That(correction.ActivityDeadlineTotalMinutes, Is.EqualTo(780));
            Assert.That(correction.MaxActivityDurationGameMinutes, Is.EqualTo(0.25));
            Assert.That(correction.CandidateErrorCode, Is.EqualTo("candidate.duration_invalid"));
            Assert.That(correction.Step, Is.EqualTo(2));
            Assert.That(first.MaxActivityDurationGameMinutes, Is.EqualTo(0.5));
            Assert.That(first.CandidateErrorCode, Is.Null);
            client.Complete(1, Visit("mina", repeatInvalidDuration ? 1 : 0.25));

            scheduler.Tick(2);
            scheduler.Tick(3);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var outcome = scheduler.GetLastOutcome("mina");
            Assert.That(outcome.Calls, Is.EqualTo(2));
            Assert.That(outcome.CandidateErrorCodes, Is.EqualTo(repeatInvalidDuration
                ? new[] { "candidate.duration_invalid", "candidate.duration_invalid" }
                : new[] { "candidate.duration_invalid" }));
            if (repeatInvalidDuration)
            {
                Assert.That(outcome.Code, Is.EqualTo("agent.response_invalid"));
                Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            }
            else
            {
                Assert.That(outcome.Code, Is.EqualTo("agent.activity_accepted"));
                Assert.That(world.GetState("mina").ActiveActivity.ExpiresAtTotalMinutes, Is.EqualTo(780));
            }
        }

        [Test]
        public void InspectContinuation_WaitingForBudgetDisclosesTheRemainingWindowAtDispatch()
        {
            var world = World(720, 0, "mina");
            using var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1, decisionTimeoutSeconds: 120));
            scheduler.Tick(0);
            var first = client.Requests[0];
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.InspectLocation, "mina.work"));
            world.Observe(Time(730));
            scheduler.Tick(1);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            world.Observe(Time(740));
            scheduler.Tick(59.9);
            Assert.That(client.Requests.Count, Is.EqualTo(1));

            scheduler.Tick(60);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var continuation = client.Requests[1];
            Assert.That(continuation.DecisionId, Is.EqualTo(first.DecisionId));
            Assert.That(continuation.GameTotalMinutes, Is.EqualTo(720));
            Assert.That(continuation.ActivityDeadlineTotalMinutes, Is.EqualTo(780));
            Assert.That(continuation.MaxActivityDurationGameMinutes, Is.EqualTo(40));
            Assert.That(continuation.LocationDetails.LocationId, Is.EqualTo("mina.work"));
            Assert.That(continuation.Step, Is.EqualTo(2));
            Assert.That(first.MaxActivityDurationGameMinutes, Is.EqualTo(60));
            Assert.That(first.LocationDetails, Is.Null);
            client.Complete(1, Visit("mina", 40));
            world.Observe(Time(745));

            scheduler.Tick(61);

            Assert.That(world.GetState("mina").ActiveActivity.ExpiresAtTotalMinutes, Is.EqualTo(780));
            Assert.That(scheduler.RequestsStarted, Is.EqualTo(2));
        }

        [Test]
        public void DurationCorrection_DoesNotExtendTheOriginalOpportunityLifetime()
        {
            var world = World(720, 0, "mina");
            using var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client);
            scheduler.Tick(0);
            client.Complete(0, Visit("mina", 61));
            world.Observe(Time(749));
            scheduler.Tick(1);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].GameTotalMinutes, Is.EqualTo(720));
            Assert.That(client.Requests[1].MaxActivityDurationGameMinutes, Is.EqualTo(31));
            client.Complete(1, Visit("mina", 20));
            world.Observe(Time(750));

            scheduler.Tick(2);

            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.opportunity_expired"));
            Assert.That(scheduler.GetLastOutcome("mina").Calls, Is.EqualTo(2));
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Tokens[1].IsCancellationRequested, Is.True);
        }

        [Test]
        public void DurationCorrection_SharesTheRateBudgetAndOriginalDecisionTimeout()
        {
            var world = World(720, 0, "mina");
            using var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1));
            scheduler.Tick(0);
            client.Complete(0, Visit("mina", 61));

            scheduler.Tick(1);
            scheduler.Tick(11.9);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.RequestsInLastMinute, Is.EqualTo(1));
            Assert.That(scheduler.GetLastOutcome("mina"), Is.Null);

            scheduler.Tick(12);
            scheduler.Tick(60);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.decision_timeout"));
            Assert.That(scheduler.GetLastOutcome("mina").Calls, Is.EqualTo(1));
            Assert.That(scheduler.GetLastOutcome("mina").CandidateErrorCodes,
                Is.EqualTo(new[] { "candidate.duration_invalid" }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DispatchCallback_CrossingTheWindowPreventsTheClientCall(bool advanceInReadiness)
        {
            var world = World(779, 0.5, "mina");
            var scene = Scene();
            bool advanceWhenReady = false;
            using var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client,
                observe: request =>
                {
                    if (advanceInReadiness) advanceWhenReady = true;
                    else world.Observe(Time(780));
                    return Observe(scene, world, request);
                }, canDecide: () =>
                {
                    if (advanceWhenReady)
                    {
                        advanceWhenReady = false;
                        world.Observe(Time(780));
                    }
                    return true;
                });

            scheduler.Tick(0);

            Assert.That(world.TotalMinutes, Is.EqualTo(780));
            Assert.That(client.Requests, Is.Empty);
            Assert.That(scheduler.RequestsStarted, Is.Zero);
            Assert.That(scheduler.RequestsInLastMinute, Is.Zero);
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(world.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.afternoon"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExecutionCallback_AdvancingWithinTheWindowCannotPostponeItsDeadline(bool advanceInReadiness)
        {
            VerifyExecutionCallback(advanceInReadiness, 779, 0.75, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExecutionCallback_CrossingTheWindowCannotAcceptTheCompletedVisit(bool advanceInReadiness)
        {
            VerifyExecutionCallback(advanceInReadiness, 780, 0, false);
        }

        private static void VerifyExecutionCallback(bool advanceInReadiness, int minute, double fraction, bool accepted)
        {
            var world = World(779, 0.5, "mina");
            var scene = Scene();
            bool advanceOnObservation = false, advanceWhenReady = false;
            using var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client,
                observe: request =>
                {
                    if (advanceOnObservation)
                    {
                        advanceOnObservation = false;
                        if (advanceInReadiness) advanceWhenReady = true;
                        else world.Observe(Time(minute, fraction));
                    }
                    return Observe(scene, world, request);
                }, canDecide: () =>
                {
                    if (advanceWhenReady)
                    {
                        advanceWhenReady = false;
                        world.Observe(Time(minute, fraction));
                    }
                    return true;
                });
            scheduler.Tick(0);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            client.Complete(0, Visit("mina", 0.5));
            advanceOnObservation = true;

            scheduler.Tick(1);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.GetLastOutcome("mina").ActivityAccepted, Is.EqualTo(accepted));
            if (accepted)
            {
                Assert.That(world.GetState("mina").ActiveActivity.ExpiresAtTotalMinutes, Is.EqualTo(780));
                Assert.That(client.Requests[0].MaxActivityDurationGameMinutes, Is.EqualTo(0.5));
            }
            else
            {
                Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
                Assert.That(world.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.afternoon"));
            }
        }

        [Test]
        public void ReplacementPending_KeepsItsOriginalWindowAndResamplesDurationWhenDispatched()
        {
            var world = World(720, 0, "mina", "ren");
            var profiles = new[] { Profile("mina"), Profile("ren") };
            using var firstClient = new ControlledClient();
            using var first = new NpcDecisionScheduler(world, profiles, firstClient,
                new NpcDecisionSettings(maxConcurrentRequests: 1));
            first.Tick(0);
            Assert.That(firstClient.Requests.Count, Is.EqualTo(1));
            Assert.That(firstClient.Requests[0].NpcId, Is.EqualTo("mina"));
            Assert.That(first.WaitingResidentCount, Is.EqualTo(1));
            firstClient.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            world.Observe(Time(740));
            using var replacementClient = new ControlledClient();
            using var replacement = first.CreateReplacement(profiles, replacementClient);

            replacement.Tick(1);

            Assert.That(replacementClient.Requests.Count, Is.EqualTo(1));
            var pending = replacementClient.Requests[0];
            Assert.That(pending.NpcId, Is.EqualTo("ren"));
            Assert.That(pending.GameTotalMinutes, Is.EqualTo(720));
            Assert.That(pending.ActivityDeadlineTotalMinutes, Is.EqualTo(780));
            Assert.That(pending.MaxActivityDurationGameMinutes, Is.EqualTo(40));
            Assert.That(pending.Triggers[0].TotalMinutes, Is.EqualTo(720));
            Assert.That(replacement.RequestsStarted, Is.EqualTo(2));
            replacementClient.Complete(0, Visit("ren", 40));
            world.Observe(Time(749));

            replacement.Tick(2);

            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(world.GetState("ren").ActiveActivity.ExpiresAtTotalMinutes, Is.EqualTo(780));
        }

        private sealed class ControlledClient : INpcDecisionClient, IDisposable
        {
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            private readonly List<TaskCompletionSource<NpcDecisionReply>> _replies = new List<TaskCompletionSource<NpcDecisionReply>>();

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                Requests.Add(request);
                Tokens.Add(token);
                var reply = new TaskCompletionSource<NpcDecisionReply>();
                _replies.Add(reply);
                return reply.Task;
            }

            internal void Complete(int index, NpcDecisionReply reply) => _replies[index].SetResult(reply);

            public void Dispose()
            {
                foreach (var reply in _replies) reply.TrySetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            }
        }

        private static NpcDecisionReply Visit(string npcId, double duration)
            => new NpcDecisionReply(NpcDecisionKind.Visit, npcId + ".work", NpcActivity.Working, duration);

        private static NpcDefinition Profile(string id) => new NpcDefinition(id, id, "Resident", "Hello");

        private static NpcAgentWorld World(int minute, double fraction, params string[] ids)
        {
            var schedules = new List<NpcDailySchedule>();
            foreach (string id in ids)
                schedules.Add(new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
                    id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 780, 1020, 1080));
            var world = new NpcAgentWorld(schedules);
            world.Observe(Time(minute, fraction));
            return world;
        }

        private static WorldTimeProgress Time(int minute, double fraction = 0)
            => new WorldTimeProgress(new GameClockSnapshot(1, minute), fraction, false, 1);

        private static NpcObservationScene Scene()
            => new NpcObservationScene(new[] { new NpcObservationRegion("square", "Town square", -10, -10, 10, 10) },
                Array.Empty<NpcObservationEntity>());

        private static NpcLocalObservation Observe(NpcObservationScene scene, NpcAgentWorld world, NpcDecisionRequest request)
            => scene.Read(request.NpcId, world.GetState(request.NpcId).WorldRunId, world.TotalMinutes,
                new[] { new NpcObservationBody(request.NpcId, 0, 0) });
    }
}
