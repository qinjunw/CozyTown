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
    public sealed class NpcDecisionSchedulerTests
    {
        [Test]
        public void RestOpportunity_TriggersOnlyItsResidentWithoutPlayerInput()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina", 720), Schedule("ren", 735) });
            world.Observe(Time(719));
            var client = new WaitingClient();
            var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina"), Profile("ren") }, client);
            scheduler.Tick(0);
            Assert.That(client.Requests, Is.Empty, "The existing work schedule needs no new model decision.");

            world.Observe(Time(720));
            scheduler.Tick(1);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(client.Requests[0].NpcId, Is.EqualTo("mina"));
            Assert.That(client.Requests[0].Persona, Is.EqualTo("mina private persona"));
            Assert.That(client.Requests[0].Self.Target.ExpectedActivity, Is.EqualTo(NpcActivity.Resting));
            world.Observe(Time(720, 0.25));
            scheduler.Tick(1.125);
            scheduler.Tick(1.25);
            Assert.That(client.Requests.Count, Is.EqualTo(1), "Clock ticks alone must not request another decision.");
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
        }

        private sealed class WaitingClient : INpcDecisionClient
        {
            public readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            }
        }

        [Test]
        public void GlobalBudget_LimitsActualRequestsAndRetainsWaitingResidents()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina"), Schedule("ren"), Schedule("sora"), Schedule("eli") });
            world.Observe(Time(720));
            var client = new ControlledClient();
            var scheduler = new NpcDecisionScheduler(world,
                new[] { Profile("mina"), Profile("ren"), Profile("sora"), Profile("eli") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 3, maxConcurrentRequests: 2));

            scheduler.Tick(0);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            scheduler.Tick(0.5);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(1);
            Assert.That(client.Requests.Count, Is.EqualTo(3));
            Assert.That(client.Requests[2].NpcId, Is.EqualTo("sora"));
            client.Complete(2, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(2);
            scheduler.Tick(59.99);
            Assert.That(client.Requests.Count, Is.EqualTo(3));

            scheduler.Tick(60);

            Assert.That(client.Requests.Count, Is.EqualTo(4));
            Assert.That(client.Requests[3].NpcId, Is.EqualTo("eli"));
        }

        [Test]
        public void LocationDetails_AreDisclosedOnDemandBeforeTheResidentsOwnActivity()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina"), Schedule("ren") });
            world.Observe(Time(720));
            var client = new ControlledClient();
            var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client);
            scheduler.Tick(0);
            var first = client.Requests[0];
            Assert.That(first.LocationDetails, Is.Null);
            Assert.That(first.KnownLocationIds, Does.Contain("mina.work"));
            Assert.That(first.KnownLocationIds, Does.Not.Contain("ren.work"));

            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.InspectLocation, "mina.work"));
            scheduler.Tick(1);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var second = client.Requests[1];
            Assert.That(second.DecisionId, Is.EqualTo(first.DecisionId));
            Assert.That(first.Step, Is.EqualTo(1));
            Assert.That(second.Step, Is.EqualTo(2));
            Assert.That(second.MaxCalls, Is.EqualTo(2));
            Assert.That(second.LocationDetails.LocationId, Is.EqualTo("mina.work"));
            Assert.That(second.LocationDetails.IsReachable, Is.True);
            Assert.That(first.LocationDetails, Is.Null, "A later disclosure must not mutate the first model input.");
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            scheduler.Tick(2);
            scheduler.Tick(3);

            Assert.That(world.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.work"));
            Assert.That(world.GetState("mina").ActiveActivity.ExpiresAtTotalMinutes, Is.EqualTo(740));
            Assert.That(world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("mina").Context, Is.SameAs(second));
            Assert.That(scheduler.GetLastOutcome("mina").Reply, Is.Not.Null);
            Assert.That(scheduler.GetLastOutcome("mina").Reply.LocationId, Is.EqualTo("mina.work"));
        }

        [Test]
        public void Rebuild_CancelsOldDecisionAndPreservesTheRealTimeBudget()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(720));
            var client = new ControlledClient();
            var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1, maxConcurrentRequests: 1, residentCooldownSeconds: 1));
            scheduler.Tick(0);
            var previousWorld = client.Requests[0].Self.WorldRunId;

            world.Observe(Time(720, rebuild: 2));
            scheduler.Tick(1);

            Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.world_stale"));
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            scheduler.Tick(2);
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            scheduler.Tick(59);
            Assert.That(client.Requests.Count, Is.EqualTo(1), "A load cannot reset the actual request budget.");
            scheduler.Tick(60);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].Self.WorldRunId, Is.Not.EqualTo(previousWorld));
            Assert.That(client.Requests[1].PreviousResultCode, Is.Null, "A new world must not inherit the old timeline's decision result.");
        }

        [Test]
        public void Timeout_KeepsTheActualRequestSlotUntilAnUncooperativeClientFinishes()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina"), Schedule("ren") });
            world.Observe(Time(720));
            var client = new ControlledClient();
            var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina"), Profile("ren") }, client,
                new NpcDecisionSettings(maxConcurrentRequests: 1, requestTimeoutSeconds: 2));
            scheduler.Tick(0);

            scheduler.Tick(2);

            Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.request_timeout"));
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            scheduler.Tick(10);
            Assert.That(client.Requests.Count, Is.EqualTo(1), "Ignoring cancellation must not allow extra physical requests.");
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            scheduler.Tick(11);
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].NpcId, Is.EqualTo("ren"));
        }

        [TestCase("state", "agent.decision_stale")]
        [TestCase("game_time", "agent.opportunity_expired")]
        [TestCase("real_time", "agent.decision_timeout")]
        public void InvalidDecisionContext_StopsFurtherModelWork(string change, string expectedCode)
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(720));
            var client = new ControlledClient();
            var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1));
            scheduler.Tick(0);
            if (change == "state")
            {
                var state = world.GetState("mina");
                Assert.That(world.SubmitActivity(new NpcActivityRequest(state.NpcId, state.WorldRunId, state.Revision,
                    "mina.rest", NpcActivity.Resting, 900)).IsSuccess, Is.True);
                state = world.GetState("mina");
                Assert.That(world.CancelActivity(state.NpcId, state.WorldRunId, state.Revision).IsSuccess, Is.True);
            }
            if (change == "game_time") world.Observe(Time(751));
            if (change == "real_time")
                client.Complete(0, new NpcDecisionReply(NpcDecisionKind.InspectLocation, "mina.work"));
            scheduler.Tick(1);
            if (change == "real_time") scheduler.Tick(12);

            Assert.That(scheduler.GetLastOutcome("mina"), Is.Not.Null);
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo(expectedCode));
            if (change != "real_time")
            {
                Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
                client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            }
            scheduler.Tick(13);
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void DisposingTheScheduler_CancelsWorkAndPreventsLaterWorldWrites()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(720));
            var client = new ControlledClient();
            var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client);
            scheduler.Tick(0);

            scheduler.Dispose();

            Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            Assert.Throws<ObjectDisposedException>(() => scheduler.Tick(1));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            scheduler.Dispose();
        }

        [TestCase(NpcDecisionKind.InspectLocation, "ren.work", NpcActivity.Resting, 20, "agent.location_unknown")]
        [TestCase(NpcDecisionKind.Visit, "ren.work", NpcActivity.Resting, 20, "agent.location_unknown")]
        [TestCase(NpcDecisionKind.Visit, "mina.work", NpcActivity.Home, 20, "agent.activity_invalid")]
        [TestCase(NpcDecisionKind.Visit, "mina.work", NpcActivity.Resting, 0, "agent.response_invalid")]
        [TestCase(NpcDecisionKind.Visit, "mina.work", NpcActivity.Resting, double.NaN, "agent.response_invalid")]
        [TestCase((NpcDecisionKind)99, "mina.work", NpcActivity.Resting, 20, "agent.response_invalid")]
        public void InvalidCandidate_CannotUseAnotherResidentsContextOrBypassActivityChecks(
            NpcDecisionKind kind, string location, NpcActivity activity, double duration, string code)
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina"), Schedule("ren") }, (npcId, locationId) => true);
            world.Observe(Time(720));
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client,
                new NpcDecisionSettings(maxCallsPerDecision: duration == 0 || double.IsNaN(duration) ? 1 : 2));
            scheduler.Tick(0);
            client.Complete(0, new NpcDecisionReply(kind, location, activity, duration));

            scheduler.Tick(1);

            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo(code));
            if (duration == 0 || double.IsNaN(duration))
                Assert.That(scheduler.GetLastOutcome("mina").CandidateErrorCodes, Is.EqualTo(new[] { "candidate.duration_invalid" }));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(world.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedInspection_StopsAtTheConfiguredModelRoundLimit()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(720));
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client);
            scheduler.Tick(0);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.InspectLocation, "mina.work"));
            scheduler.Tick(1);
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.InspectLocation, "mina.rest"));

            scheduler.Tick(2);
            scheduler.Tick(3);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.decision_step_limit"));
            Assert.That(scheduler.GetLastOutcome("mina").Calls, Is.EqualTo(2));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
        }

        [Test]
        public void QueuedOpportunity_ExpiresBeforeItCanConsumeRequestBudget()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina"), Schedule("ren") });
            world.Observe(Time(720));
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina"), Profile("ren") }, client,
                new NpcDecisionSettings(maxConcurrentRequests: 1));
            scheduler.Tick(0);
            world.Observe(Time(751));
            scheduler.Tick(1);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));

            scheduler.Tick(2);
            scheduler.Tick(60);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(world.GetState("ren").ActiveActivity, Is.Null);
        }

        [Test]
        public void Cooldown_RetainsANewOpportunityUntilTheResidentCanDecideAgain()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(720));
            var client = new WaitingClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, client);
            scheduler.Tick(0);
            scheduler.Tick(1);
            var state = world.GetState("mina");
            world.SubmitActivity(new NpcActivityRequest(state.NpcId, state.WorldRunId, state.Revision,
                "mina.rest", NpcActivity.Resting, 900));
            state = world.GetState("mina");
            world.CancelActivity(state.NpcId, state.WorldRunId, state.Revision);
            scheduler.Tick(2);
            scheduler.Tick(29.99);
            Assert.That(client.Requests.Count, Is.EqualTo(1));

            scheduler.Tick(30);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].Self.Revision, Is.EqualTo(world.GetState("mina").Revision));
            Assert.That(client.Requests[1].PreviousResultCode, Is.EqualTo("agent.decision_wait"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ClientFailure_ConsumesBudgetAndLeavesTheDefaultSchedule(bool synchronous)
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina"), Schedule("ren") });
            world.Observe(Time(720));
            var client = new BrokenClient(synchronous);
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina"), Profile("ren") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1));
            scheduler.Tick(0);
            scheduler.Tick(1);

            Assert.That(client.Requests, Is.EqualTo(1));
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.client_failure"));
            Assert.That(world.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.rest"));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            scheduler.Tick(60);
            Assert.That(client.Requests, Is.EqualTo(2));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MalformedResponse_HasADistinctOutcomeFromTransportFailure(bool synchronous)
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(720));
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile("mina") }, new BrokenClient(synchronous, true));
            scheduler.Tick(0);
            scheduler.Tick(1);
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.response_invalid"));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
        }

        private sealed class BrokenClient : INpcDecisionClient
        {
            private readonly bool _synchronous;
            private readonly bool _invalidResponse;
            public int Requests { get; private set; }
            public BrokenClient(bool synchronous, bool invalidResponse = false)
            {
                _synchronous = synchronous;
                _invalidResponse = invalidResponse;
            }

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests++;
                Exception error = _invalidResponse ? (Exception)new FormatException("Simulated malformed response.")
                    : new InvalidOperationException("Simulated provider failure.");
                if (_synchronous) throw error;
                return Task.FromException<NpcDecisionReply>(error);
            }
        }

        private sealed class ControlledClient : INpcDecisionClient
        {
            public readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            private readonly List<TaskCompletionSource<NpcDecisionReply>> _replies = new List<TaskCompletionSource<NpcDecisionReply>>();

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                Tokens.Add(cancellationToken);
                var reply = new TaskCompletionSource<NpcDecisionReply>();
                _replies.Add(reply);
                return reply.Task;
            }

            public void Complete(int index, NpcDecisionReply reply) => _replies[index].SetResult(reply);
        }

        private static NpcDefinition Profile(string id) => new NpcDefinition(id, id, id + " private persona", "Hello");

        private static WorldTimeProgress Time(int minute, double fraction = 0, int day = 1, long rebuild = 1)
            => new WorldTimeProgress(new GameClockSnapshot(day, minute), fraction, false, rebuild);

        private static NpcDailySchedule Schedule(string id, int restMinute = 720)
            => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest",
                id + ".afternoon", 360, 480, restMinute, 780, 1020, 1080);
    }
}
