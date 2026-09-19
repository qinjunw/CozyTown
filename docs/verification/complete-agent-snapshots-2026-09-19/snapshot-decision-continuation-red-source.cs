using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcDecisionSnapshotTests
    {
        [Test]
        public void FirstCallInFlight_RestoreConsumesItsAttemptAndRetainsTheOldPhysicalSlot()
        {
            var world = World();
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile() }, client,
                new NpcDecisionSettings(maxConcurrentRequests: 1));
            scheduler.Tick(0);
            var original = client.Requests[0];
            var snapshot = scheduler.CaptureSnapshot(4);
            Assert.That(client.Requests.Count, Is.EqualTo(1), "Capturing must not dispatch a model call.");
            Assert.That(client.Tokens[0].IsCancellationRequested, Is.False);

            var restoredWorld = World();
            restoredWorld.TakeEvents("mina");
            scheduler.ValidateSnapshot(snapshot, restoredWorld, null);
            Assert.That(client.Tokens[0].IsCancellationRequested, Is.False, "Preparing must leave the running request valid.");
            scheduler.RestoreSnapshot(snapshot, restoredWorld, null, 100);
            Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            scheduler.Tick(100);
            Assert.That(client.Requests.Count, Is.EqualTo(1));

            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            scheduler.Tick(101);
            Assert.That(restoredWorld.GetState("mina").ActiveActivity, Is.Null, "The retired response cannot write the restored world.");
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].DecisionId, Is.Not.EqualTo(original.DecisionId));
            Assert.That(client.Requests[1].Self.WorldRunId, Is.Not.EqualTo(original.Self.WorldRunId));
            Assert.That(client.Requests[1].Step, Is.EqualTo(2));
            Assert.That(client.Requests[1].MaxCalls, Is.EqualTo(2));
            Assert.That(client.Requests[1].ActivityDeadlineTotalMinutes, Is.EqualTo(780));

            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(102);
            Assert.That(scheduler.GetLastOutcome("mina").Calls, Is.EqualTo(2));
            Assert.That(scheduler.RequestsStarted, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void QueuedContinuation_RestoresDisclosureAndCorrectionProgressWithoutExtendingItsWindow(bool correction)
        {
            var world = World();
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile() }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1, maxCallsPerDecision: 3, decisionTimeoutSeconds: 120));
            scheduler.Tick(0);
            if (correction) client.Fail(0, "candidate.duration_invalid");
            else client.Complete(0, new NpcDecisionReply(NpcDecisionKind.InspectLocation, "mina.work"));
            scheduler.Tick(1);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            var snapshot = RoundTrip(scheduler.CaptureSnapshot(2));
            var restoredWorld = World(false);
            restoredWorld.TakeEvents("mina");
            scheduler.RestoreSnapshot(snapshot, restoredWorld, null, 100);
            restoredWorld.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 745), 0, false, 1));

            scheduler.Tick(100);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var resumed = client.Requests[1];
            Assert.That(resumed.Step, Is.EqualTo(2));
            Assert.That(resumed.GameTotalMinutes, Is.EqualTo(720));
            Assert.That(resumed.ActivityDeadlineTotalMinutes, Is.EqualTo(780));
            Assert.That(resumed.MaxActivityDurationGameMinutes, Is.EqualTo(35));
            if (correction)
            {
                Assert.That(resumed.CandidateErrorCode, Is.EqualTo("candidate.duration_invalid"));
                client.Fail(1, "candidate.duration_invalid");
                scheduler.Tick(101);
                Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.response_invalid"));
                Assert.That(scheduler.GetLastOutcome("mina").CandidateErrorCodes,
                    Is.EqualTo(new[] { "candidate.duration_invalid", "candidate.duration_invalid" }));
                scheduler.Tick(160);
                Assert.That(client.Requests.Count, Is.EqualTo(2), "Loading must not grant a second correction.");
            }
            else
            {
                Assert.That(resumed.LocationDetails, Is.Not.Null);
                Assert.That(resumed.LocationDetails.LocationId, Is.EqualTo("mina.work"));
                Assert.That(resumed.LocationDetails.IsReachable, Is.False, "Disclosed reachability is sampled again from the restored world.");
                client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Wait));
                scheduler.Tick(101);
                Assert.That(scheduler.GetLastOutcome("mina").Calls, Is.EqualTo(2));
            }
        }

        private static NpcDecisionSchedulerSnapshot RoundTrip(NpcDecisionSchedulerSnapshot snapshot)
        {
            var serializer = new DataContractJsonSerializer(typeof(NpcDecisionSchedulerSnapshot));
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, snapshot);
            stream.Position = 0;
            return (NpcDecisionSchedulerSnapshot)serializer.ReadObject(stream);
        }

        private static NpcDefinition Profile() => new NpcDefinition("mina", "Mina", "Mina persona", "Hello");

        private static NpcAgentWorld World(bool reachable = true)
        {
            var world = new NpcAgentWorld(new[] { new NpcDailySchedule("mina", "mina.home", "mina.outside", "mina.entry",
                "mina.work", "mina.rest", "mina.afternoon", 360, 480, 720, 780, 1020, 1080) }, (npcId, locationId) => reachable);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            return world;
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
            public void Fail(int index, string code) => _replies[index].SetException(new NpcCandidateException(code));
        }
    }
}
