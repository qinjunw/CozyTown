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
    public sealed class NpcDecisionReplacementTests
    {
        [Test]
        public void Replacement_PreservesQueuedOpportunityWithTheNewProfileAndExpressionSettings()
        {
            var world = RestWorld("mina", "ren");
            using var firstClient = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, Profiles("mina", "ren"), firstClient,
                new NpcDecisionSettings(maxConcurrentRequests: 1));
            scheduler.Tick(0);
            Assert.That(firstClient.Requests[0].NpcId, Is.EqualTo("mina"));
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            world.Observe(Time(725));
            firstClient.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            using var replacementClient = new ControlledClient();
            using var replacement = scheduler.CreateReplacement(new[]
                {
                    Profile("mina"), new NpcDefinition("ren", "Ren updated", "Updated private persona", "Hello")
                }, replacementClient, new NpcDecisionSettings(maxConcurrentRequests: 1, maxCallsPerDecision: 1),
                speechMode: NpcSpeechMode.StructuredFacts);

            replacement.Tick(1);

            Assert.That(replacementClient.Requests.Count, Is.EqualTo(1));
            var request = replacementClient.Requests[0];
            Assert.That(request.NpcId, Is.EqualTo("ren"));
            Assert.That(request.DisplayName, Is.EqualTo("Ren updated"));
            Assert.That(request.Persona, Is.EqualTo("Updated private persona"));
            Assert.That(request.SpeechMode, Is.EqualTo(NpcSpeechMode.StructuredFacts));
            Assert.That(request.MaxCalls, Is.EqualTo(1));
            Assert.That(request.GameTotalMinutes, Is.EqualTo(720));
            Assert.That(request.Triggers.Count, Is.EqualTo(1));
            Assert.That(request.Triggers[0].Kind, Is.EqualTo(NpcAgentEventKind.WorldRebuilt));
            Assert.That(request.Triggers[0].TotalMinutes, Is.EqualTo(720));
            Assert.That(request.KnownLocationIds, Does.Contain("ren.work"));
            Assert.That(request.KnownLocationIds, Does.Not.Contain("mina.work"));
        }

        [Test]
        public void RepeatedReplacement_DoesNotExtendTheQueuedOpportunityDeadline()
        {
            var world = RestWorld("mina", "ren");
            using var firstClient = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, Profiles("mina", "ren"), firstClient,
                new NpcDecisionSettings(maxRequestsPerMinute: 1, maxConcurrentRequests: 1));
            scheduler.Tick(0);
            firstClient.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            world.Observe(Time(749));
            using var replacementClient = new ControlledClient();
            using var replacement = scheduler.CreateReplacement(Profiles("mina", "ren"), replacementClient);
            using var repeated = replacement.CreateReplacement(Profiles("mina", "ren"), replacementClient);
            repeated.Tick(1);
            Assert.That(repeated.WaitingResidentCount, Is.EqualTo(1));

            world.Observe(Time(750));
            repeated.Tick(60);

            Assert.That(replacementClient.Requests, Is.Empty);
            Assert.That(repeated.RequestsStarted, Is.EqualTo(1));
            Assert.That(repeated.RequestsInLastMinute, Is.Zero);
            Assert.That(repeated.WaitingResidentCount, Is.Zero);
        }

        [Test]
        public void RemovingAndReaddingAProfile_PreservesItsCooldownAcrossRepeatedReplacement()
        {
            var world = RestWorld("mina");
            using var firstClient = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, Profiles("mina"), firstClient);
            scheduler.Tick(0);
            firstClient.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(1);
            using var replacementClient = new ControlledClient();
            using var removed = scheduler.CreateReplacement(Array.Empty<NpcDefinition>(), replacementClient);
            QueueRestOpportunity(world, "mina");
            removed.Tick(5);
            using var readded = removed.CreateReplacement(Profiles("mina"), replacementClient);
            readded.Tick(29.99);
            Assert.That(replacementClient.Requests, Is.Empty);
            Assert.That(readded.WaitingResidentCount, Is.EqualTo(1));
            using var repeated = readded.CreateReplacement(Profiles("mina"), replacementClient);

            repeated.Tick(30);

            Assert.That(replacementClient.Requests.Count, Is.EqualTo(1));
            Assert.That(replacementClient.Requests[0].NpcId, Is.EqualTo("mina"));
            Assert.That(repeated.RequestsStarted, Is.EqualTo(2));
            Assert.That(repeated.RequestsInLastMinute, Is.EqualTo(2));
        }

        [Test]
        public void Replacement_PreservesTheMonotonicRealTimeBoundary()
        {
            var world = RestWorld("mina");
            using var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, Profiles("mina"), client);
            scheduler.Tick(20);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(21);
            using var replacement = scheduler.CreateReplacement(Profiles("mina"), client);

            Assert.Throws<ArgumentOutOfRangeException>(() => replacement.Tick(20.99));

            Assert.DoesNotThrow(() => replacement.Tick(21));
            Assert.That(replacement.RequestsStarted, Is.EqualTo(1));
            Assert.That(replacement.RequestsInLastMinute, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompletedUnconsumedReply_IsDiscardedWithoutCancellingItsCompletedToken(bool faulted)
        {
            var world = RestWorld("mina");
            using var firstClient = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, Profiles("mina"), firstClient);
            scheduler.Tick(0);
            if (faulted) firstClient.Fail(0, new InvalidOperationException("Simulated provider failure."));
            else firstClient.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            using var replacementClient = new ControlledClient();

            using var replacement = scheduler.CreateReplacement(Profiles("mina"), replacementClient);
            replacement.Tick(1);

            Assert.That(firstClient.Tokens[0].IsCancellationRequested, Is.False);
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(replacement.GetLastOutcome("mina"), Is.Null);
            Assert.That(replacementClient.Requests, Is.Empty);
            Assert.That(replacement.RequestsStarted, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => scheduler.Tick(2));
            Assert.Throws<ObjectDisposedException>(() => scheduler.CreateReplacement(Profiles("mina"), replacementClient));
        }

        [Test]
        public void InvalidReplacementProfiles_LeaveTheOriginalCompletedDecisionApplicable()
        {
            var world = RestWorld("mina");
            using var firstClient = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, Profiles("mina"), firstClient);
            scheduler.Tick(0);
            firstClient.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            using var replacementClient = new ControlledClient();

            Assert.Throws<ArgumentException>(() =>
                scheduler.CreateReplacement(Profiles("mina", "mina"), replacementClient));
            scheduler.Tick(1);

            Assert.That(world.GetState("mina").ActiveActivity.TargetLocationId, Is.EqualTo("mina.work"));
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.activity_accepted"));
            Assert.That(firstClient.Tokens[0].IsCancellationRequested, Is.False);
            Assert.That(replacementClient.Requests, Is.Empty);
            Assert.That(scheduler.RequestsStarted, Is.EqualTo(1));
        }

        [Test]
        public void Replacement_DiscardsAQueuedOpportunityWhoseResidentAcceptedAnotherActivity()
        {
            var world = RestWorld("mina", "ren");
            using var firstClient = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, Profiles("mina", "ren"), firstClient,
                new NpcDecisionSettings(maxConcurrentRequests: 1));
            scheduler.Tick(0);
            Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            var state = world.GetState("ren");
            Assert.That(world.SubmitActivity(new NpcActivityRequest("ren", state.WorldRunId, state.Revision,
                "ren.work", NpcActivity.Working, 740)).IsSuccess, Is.True);
            firstClient.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            using var replacementClient = new ControlledClient();
            using var replacement = scheduler.CreateReplacement(Profiles("mina", "ren"), replacementClient);

            replacement.Tick(1);

            Assert.That(replacementClient.Requests, Is.Empty);
            Assert.That(replacement.WaitingResidentCount, Is.Zero);
            Assert.That(world.GetState("ren").ActiveActivity.TargetLocationId, Is.EqualTo("ren.work"));
        }

        [Test]
        public void ClientCallback_CannotReplaceOrTickBeforeItsTaskHasBeenRegistered()
        {
            var world = RestWorld("mina");
            using var replacementClient = new ControlledClient();
            NpcDecisionScheduler scheduler = null;
            Exception replacementError = null;
            Exception tickError = null;
            int activeDuringCallback = -1;
            var client = new CallbackClient(() =>
            {
                activeDuringCallback = scheduler.ActiveRequestCount;
                replacementError = CaptureFailure(() =>
                {
                    using var unexpected = scheduler.CreateReplacement(Profiles("mina"), replacementClient);
                });
                tickError = CaptureFailure(() => scheduler.Tick(0));
            });
            using (scheduler = new NpcDecisionScheduler(world, Profiles("mina"), client))
            {
                scheduler.Tick(0);

                Assert.That(activeDuringCallback, Is.Zero);
                Assert.That(replacementError, Is.TypeOf<InvalidOperationException>());
                Assert.That(tickError, Is.TypeOf<InvalidOperationException>());
                Assert.That(client.Requests, Is.EqualTo(1));
                Assert.That(scheduler.RequestsStarted, Is.EqualTo(1));
                Assert.That(replacementClient.Requests, Is.Empty);

                scheduler.Tick(1);

                Assert.That(world.GetState("mina").ActiveActivity.TargetLocationId, Is.EqualTo("mina.work"));
                Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.activity_accepted"));
            }
        }

        [Test]
        public void ReplacementProfileEnumeration_CannotTickReplaceOrRebindTheOriginalScheduler()
        {
            var world = RestWorld("mina");
            var otherWorld = RestWorld("mina");
            using var firstClient = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, Profiles("mina"), firstClient);
            scheduler.Tick(0);
            firstClient.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            using var replacementClient = new ControlledClient();
            Exception tickError = null;
            Exception replacementError = null;
            Exception bindError = null;

            IEnumerable<NpcDefinition> ReentrantProfiles()
            {
                tickError = CaptureFailure(() => scheduler.Tick(1));
                replacementError = CaptureFailure(() =>
                {
                    using var unexpected = scheduler.CreateReplacement(Profiles("mina"), replacementClient);
                });
                bindError = CaptureFailure(() => scheduler.BindWorld(otherWorld));
                yield return Profile("mina");
            }

            using var replacement = scheduler.CreateReplacement(ReentrantProfiles(), replacementClient);

            Assert.That(tickError, Is.TypeOf<InvalidOperationException>());
            Assert.That(replacementError, Is.TypeOf<InvalidOperationException>());
            Assert.That(bindError, Is.TypeOf<InvalidOperationException>());
            Assert.That(firstClient.Requests.Count, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => scheduler.CreateReplacement(Profiles("mina"), replacementClient));
            replacement.Tick(1);
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            QueueRestOpportunity(world, "mina");
            replacement.Tick(30);
            Assert.That(replacementClient.Requests.Count, Is.EqualTo(1));
            Assert.That(replacementClient.Requests[0].Self.WorldRunId, Is.EqualTo(world.GetState("mina").WorldRunId));
            replacementClient.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));

            replacement.Tick(31);

            Assert.That(world.GetState("mina").ActiveActivity.TargetLocationId, Is.EqualTo("mina.work"));
            Assert.That(otherWorld.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(replacement.RequestsStarted, Is.EqualTo(2));
        }

        private sealed class CallbackClient : INpcDecisionClient
        {
            private readonly Action _onFirstRequest;
            public int Requests { get; private set; }

            public CallbackClient(Action onFirstRequest) => _onFirstRequest = onFirstRequest;

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests++;
                if (Requests == 1) _onFirstRequest();
                return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            }
        }

        private sealed class ControlledClient : INpcDecisionClient, IDisposable
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
            public void Fail(int index, Exception error) => _replies[index].SetException(error);

            public void Dispose()
            {
                foreach (var reply in _replies) reply.TrySetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            }
        }

        private static Exception CaptureFailure(Action action)
        {
            try { action(); }
            catch (Exception error) { return error; }
            return null;
        }

        private static NpcAgentWorld RestWorld(params string[] ids)
        {
            var world = new NpcAgentWorld(Array.ConvertAll(ids, Schedule));
            world.Observe(Time(720));
            return world;
        }

        private static void QueueRestOpportunity(NpcAgentWorld world, string npcId)
        {
            var state = world.GetState(npcId);
            Assert.That(world.SubmitActivity(new NpcActivityRequest(npcId, state.WorldRunId, state.Revision,
                npcId + ".rest", NpcActivity.Resting, 780)).IsSuccess, Is.True);
            state = world.GetState(npcId);
            Assert.That(world.CancelActivity(npcId, state.WorldRunId, state.Revision).IsSuccess, Is.True);
        }

        private static NpcDefinition[] Profiles(params string[] ids) => Array.ConvertAll(ids, Profile);
        private static NpcDefinition Profile(string id) => new NpcDefinition(id, id, id + " private persona", "Hello");
        private static WorldTimeProgress Time(int minute) => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, 1);
        private static NpcDailySchedule Schedule(string id)
            => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest",
                id + ".afternoon", 360, 480, 720, 780, 1020, 1080);
    }
}
