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
    public sealed class NpcDecisionRecoveryTests
    {
        private NpcAgentWorld _world;
        private NpcObservationScene _scene;
        private readonly NpcDefinition _profile = new NpcDefinition("mina", "Mina", "Resident", "Hello");

        [SetUp]
        public void SetUp()
        {
            var schedule = new NpcDailySchedule("mina", "mina.home", "mina.outside", "mina.entry",
                "mina.work", "mina.rest", "mina.afternoon", 360, 480, 720, 780, 1020, 1080);
            _world = new NpcAgentWorld(new[] { schedule });
            _world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, true, 1));
            _scene = new NpcObservationScene(
                new[] { new NpcObservationRegion("square", "Town square", -10, -10, 10, 10) },
                Array.Empty<NpcObservationEntity>());
        }

        [Test]
        public void DispatchObservation_ClosingReadinessGatePreventsModelRequest()
        {
            bool ready = true;
            var client = new VisitingClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { _profile }, client,
                observe: request =>
                {
                    var observation = Observe(request);
                    ready = false;
                    return observation;
                }, canDecide: () => ready);

            scheduler.Tick(0);

            Assert.That(ready, Is.False);
            Assert.That(client.Requests, Is.Empty);
            Assert.That(scheduler.RequestsStarted, Is.Zero);
            Assert.That(scheduler.RequestsInLastMinute, Is.Zero);
            Assert.That(scheduler.ActiveRequestCount, Is.Zero);
            Assert.That(_world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.world_not_ready"));
        }

        [Test]
        public void ExecutionObservation_ClosingReadinessGateRejectsCompletedVisit()
        {
            bool ready = true, suspendWhenObserved = false;
            var client = new VisitingClient();
            using var scheduler = new NpcDecisionScheduler(_world, new[] { _profile }, client,
                observe: request =>
                {
                    var observation = Observe(request);
                    if (suspendWhenObserved) ready = false;
                    return observation;
                }, canDecide: () => ready);
            scheduler.Tick(0);
            Assert.That(client.Requests, Has.Count.EqualTo(1));
            Assert.That(_world.GetState("mina").ActiveActivity, Is.Null);
            suspendWhenObserved = true;

            scheduler.Tick(1);

            Assert.That(ready, Is.False);
            Assert.That(_world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(_world.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.rest"));
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.world_not_ready"));
            Assert.That(client.Requests, Has.Count.EqualTo(1));
            Assert.That(scheduler.RequestsInLastMinute, Is.EqualTo(1));
            Assert.That(scheduler.ActiveRequestCount, Is.Zero);
        }

        private NpcLocalObservation Observe(NpcDecisionRequest request)
            => _scene.Read(request.NpcId, _world.GetState(request.NpcId).WorldRunId, _world.TotalMinutes,
                new[] { new NpcObservationBody("mina", 0, 0) });

        private sealed class VisitingClient : INpcDecisionClient
        {
            public readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            }
        }
    }
}
