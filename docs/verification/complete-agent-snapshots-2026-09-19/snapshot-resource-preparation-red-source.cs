using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
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

        [Test]
        public void QueuedResident_RestoresItsOriginalOpportunityAndTheNextPollingResident()
        {
            var world = WorldFor(new[] { "mina", "ren" });
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile(), Profile("ren") }, client,
                new NpcDecisionSettings(maxConcurrentRequests: 1));
            scheduler.Tick(0);
            Assert.That(client.Requests[0].NpcId, Is.EqualTo("mina"));
            var snapshot = RoundTrip(scheduler.CaptureSnapshot(2));
            var prepared = world.PrepareRestore(world.CaptureSnapshot(),
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 2));
            Assert.That(prepared.IsSuccess, Is.True);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));

            scheduler.RestoreSnapshot(snapshot, prepared.Value, null, 100);
            scheduler.Tick(100);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].NpcId, Is.EqualTo("ren"));
            Assert.That(client.Requests[1].Step, Is.EqualTo(1));
            Assert.That(client.Requests[1].GameTotalMinutes, Is.EqualTo(720));
            Assert.That(client.Requests[1].ActivityDeadlineTotalMinutes, Is.EqualTo(780));
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(101);
            Assert.That(client.Requests.Count, Is.EqualTo(3));
            Assert.That(client.Requests[2].NpcId, Is.EqualTo("mina"));
            Assert.That(client.Requests[2].Step, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SocialContinuation_RestoresConsumedOpportunityOrQueuedDeliveredTurn(bool waitingTurn)
        {
            var world = WorldFor(new[] { "mina", "ren" });
            var store = new InMemoryEconomyStateStore(new[] { Character("mina", 0, 50), Character("ren", 2, 0) },
                Array.Empty<ShopEconomySnapshot>());
            var resources = new CharacterResourceTrading(store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            var plan = new NpcMeetingPlan("fish", "mina", "ren", "pond", "mina.rest", "ren.rest", 720, 720, 780,
                resourceTerms: new CharacterTradeTerms("ren", "mina", "fish", 1, 25));
            var board = new NpcMeetingBoard(world, new[] { plan }, (npcId, locationId) => NpcMeetingPresence.Arrived, resources);
            if (waitingTurn)
            {
                var invitation = board.Invite(world.GetState("mina"), "fish");
                Assert.That(board.Respond(world.GetState("ren"), invitation.Value.Id, true).IsSuccess, Is.True);
                board.Observe();
                Assert.That(board.Deliver(world.GetState("mina"), invitation.Value.Id).IsSuccess, Is.True);
            }
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile(), Profile("ren") }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1), board);
            scheduler.Tick(0);
            if (waitingTurn)
            {
                client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Speak,
                    meetingId: client.Requests[0].Social.MeetingId, text: "The payment is yours."));
                scheduler.Tick(1);
                Assert.That(board.GetCurrent("ren").SpeakerId, Is.EqualTo("ren"));
                Assert.That(scheduler.WaitingResidentCount, Is.EqualTo(1));
            }
            else Assert.That(board.GetContext("mina"), Is.Null, "The dispatched opportunity has already been consumed.");
            var snapshot = RoundTrip(scheduler.CaptureSnapshot(2));
            var restoredWorld = world.PrepareRestore(world.CaptureSnapshot(),
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 2)).Value;
            var restoredBoard = board.PrepareRestore(board.CaptureSnapshot(), restoredWorld).Value;
            if (!waitingTurn) client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));

            scheduler.RestoreSnapshot(snapshot, restoredWorld, restoredBoard, 100);
            scheduler.Tick(100);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var resumed = client.Requests[1];
            Assert.That(resumed.NpcId, Is.EqualTo(waitingTurn ? "ren" : "mina"));
            Assert.That(resumed.Social.Kind, Is.EqualTo(waitingTurn ? NpcSocialContextKind.Conversation : NpcSocialContextKind.Opportunity));
            Assert.That(resumed.Step, Is.EqualTo(waitingTurn ? 1 : 2));
            if (waitingTurn)
            {
                Assert.That(resumed.Social.Transcript.Select(line => line.Text), Is.EqualTo(new[] { "The payment is yours." }));
                client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: resumed.Social.MeetingId, text: "Here is the fish."));
                scheduler.Tick(101);
                Assert.That(restoredBoard.GetCurrent("mina").Transcript.Count, Is.EqualTo(2));
                store.TryGetCharacter("ren", out var ren);
                store.TryGetCharacter("mina", out var mina);
                Assert.That(ren.Backpack.Items.Single().Quantity, Is.EqualTo(1));
                Assert.That(mina.Backpack.Items.Single().Quantity, Is.EqualTo(1));
                Assert.That(ren.Wallet.Balance, Is.EqualTo(25));
                Assert.That(mina.Wallet.Balance, Is.EqualTo(25));
            }
            else
            {
                Assert.That(restoredBoard.GetContext("mina"), Is.Null, "Continuation must not return the consumed opportunity to the board.");
                client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Invite, planId: "fish"));
                scheduler.Tick(101);
                Assert.That(restoredBoard.GetCurrent("mina").State, Is.EqualTo(NpcMeetingState.Invited));
            }
        }

        private static CharacterEconomySnapshot Character(string id, int fish, int coins)
            => new CharacterEconomySnapshot(id, new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>()
                : new[] { new ItemStack("fish", fish) }), new WalletSnapshot(coins));

        [Test]
        public void LegacyReset_ClearsLogicalWorkWhileKeepingActualRequestsAndTheirBudget()
        {
            var world = World();
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile() }, new UndeclaredClient(client),
                new NpcDecisionSettings(maxRequestsPerMinute: 1, maxConcurrentRequests: 1));
            scheduler.Tick(0);
            var restoredWorld = World();

            scheduler.ResetWorld(restoredWorld, null, 1);

            Assert.That(client.Tokens[0].IsCancellationRequested, Is.True);
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            scheduler.Tick(1);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            scheduler.Tick(2);
            Assert.That(restoredWorld.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(scheduler.RequestsStarted, Is.EqualTo(1));
            scheduler.Tick(60);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].Self.WorldRunId, Is.EqualTo(restoredWorld.GetState("mina").WorldRunId));
            Assert.That(client.Requests[1].Step, Is.EqualTo(1));
            Assert.That(client.Requests[1].PreviousResultCode, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrChangedClientConfiguration_RejectsSnapshotWorkWithoutCancellingTheLiveRequest(bool missing)
        {
            var world = World();
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile() }, client);
            scheduler.Tick(0);
            var snapshot = RoundTrip(scheduler.CaptureSnapshot(1));
            client.SnapshotConfiguration = missing ? null : "fixture:controlled-v2";

            if (missing) Assert.Throws<InvalidOperationException>(() => scheduler.CaptureSnapshot(2));
            else Assert.Throws<ArgumentException>(() => scheduler.ValidateSnapshot(snapshot, World(), null));

            Assert.That(client.Tokens[0].IsCancellationRequested, Is.False);
            Assert.That(client.Requests.Count, Is.EqualTo(1));
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(2);
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.decision_wait"));
        }

        [Test]
        public void EarlierSnapshot_RestoresLogicalCooldownWhilePreservingFutureActualCalls()
        {
            var world = World();
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile() }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 2));
            var savedWorld = world.CaptureSnapshot();
            var savedDecisions = RoundTrip(scheduler.CaptureSnapshot(0));
            scheduler.Tick(10);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(11);

            var restoredWorld = world.PrepareRestore(savedWorld,
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 2)).Value;
            scheduler.RestoreSnapshot(savedDecisions, restoredWorld, null, 12);
            scheduler.Tick(12);

            Assert.That(client.Requests.Count, Is.EqualTo(2), "The saved zero cooldown must not inherit a later timeline's behavior cooldown.");
            Assert.That(scheduler.RequestsInLastMinute, Is.EqualTo(2));
            var secondWorld = world.PrepareRestore(savedWorld,
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 3)).Value;
            scheduler.RestoreSnapshot(savedDecisions, secondWorld, null, 13);
            scheduler.Tick(13);
            Assert.That(client.Requests.Count, Is.EqualTo(2), "Repeated loads retain the current process's actual rolling-call budget.");
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(69.99);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            scheduler.Tick(70);
            Assert.That(client.Requests.Count, Is.EqualTo(3));
            Assert.That(scheduler.RequestsStarted, Is.EqualTo(3));
        }

        [Test]
        public void CancellationCallback_CannotDisposeTheSchedulerDuringSnapshotPublication()
        {
            var world = World();
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile() }, client);
            client.OnCancellation = () => scheduler.Dispose();
            scheduler.Tick(0);
            var snapshot = scheduler.CaptureSnapshot(1);
            var restoredWorld = world.PrepareRestore(world.CaptureSnapshot(),
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 2)).Value;

            scheduler.RestoreSnapshot(snapshot, restoredWorld, null, 100);
            client.OnCancellation = null;
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));

            Assert.DoesNotThrow(() => scheduler.Tick(101));
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(client.Requests[1].Self.WorldRunId, Is.EqualTo(restoredWorld.GetState("mina").WorldRunId));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EarlierResourceOpportunity_PreparationUsesSavedRelationsBeforeEconomicPublication(bool pending)
        {
            var world = WorldFor(new[] { "eli", "mina", "ren" });
            var store = new InMemoryEconomyStateStore(new[] { Character("mina", 0, 50), Character("ren", 2, 0) },
                Array.Empty<ShopEconomySnapshot>());
            var resources = new CharacterResourceTrading(store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            var plan = new NpcMeetingPlan("fish", "mina", "ren", "pond", "mina.rest", "ren.rest", 720, 720, 780,
                resourceTerms: new CharacterTradeTerms("ren", "mina", "fish", 1, 25));
            var board = new NpcMeetingBoard(world, new[] { plan }, (npcId, locationId) => NpcMeetingPresence.Arrived, resources);
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world,
                pending ? new[] { Profile("eli"), Profile() } : new[] { Profile() }, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 1), board);
            scheduler.Tick(0);
            var savedDecisions = RoundTrip(scheduler.CaptureSnapshot(1));
            var savedWorld = world.CaptureSnapshot();
            var savedMeetings = board.CaptureSnapshot();
            var savedEconomy = store.CaptureSnapshot();
            var meetingId = board.Invite(world.GetState("mina"), "fish").Value.Id;
            Assert.That(board.Respond(world.GetState("ren"), meetingId, true).IsSuccess, Is.True);
            board.Observe();
            Assert.That(board.Deliver(world.GetState("mina"), meetingId).IsSuccess, Is.True);
            var restoredWorld = world.PrepareRestore(savedWorld,
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 2)).Value;
            var restoredBoard = board.PrepareRestore(savedMeetings, restoredWorld).Value;

            Assert.DoesNotThrow(() => scheduler.ValidateSnapshot(savedDecisions, restoredWorld, restoredBoard));

            Assert.That(client.Tokens[0].IsCancellationRequested, Is.False);
            store.TryGetCharacter("mina", out var futureMina);
            Assert.That(futureMina.Backpack.Items.Single().Quantity, Is.EqualTo(1), "Preparation must leave the current economy unchanged.");
            Assert.That(store.Restore(savedEconomy).IsSuccess, Is.True);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.RestoreSnapshot(savedDecisions, restoredWorld, restoredBoard, 100);
            scheduler.Tick(100);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var resumed = client.Requests[1];
            Assert.That(resumed.NpcId, Is.EqualTo("mina"));
            Assert.That(resumed.Social.Kind, Is.EqualTo(NpcSocialContextKind.Opportunity));
            Assert.That(resumed.Social.Resources.CanMeetKnownTerms, Is.True);
            Assert.That(resumed.Step, Is.EqualTo(pending ? 1 : 2));
        }

        [Test]
        public void SecondCallInFlight_RestoreDoesNotGrantAThirdCallOrApplyTheRetiredReply()
        {
            var world = World();
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile() }, client);
            scheduler.Tick(0);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.InspectLocation, "mina.work"));
            scheduler.Tick(1);
            var saved = RoundTrip(scheduler.CaptureSnapshot(2));
            var restoredWorld = world.PrepareRestore(world.CaptureSnapshot(),
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 2)).Value;

            scheduler.RestoreSnapshot(saved, restoredWorld, null, 100);
            scheduler.Tick(100);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.decision_step_limit"));
            Assert.That(scheduler.GetLastOutcome("mina").Calls, Is.EqualTo(2));
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            scheduler.Tick(101);
            Assert.That(restoredWorld.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
        }

        [Test]
        public void ResumedDecision_UsesOnlyItsSavedRemainingTotalTimeWithoutASecondCooldown()
        {
            var world = World();
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile() }, client,
                new NpcDecisionSettings(requestTimeoutSeconds: 30));
            scheduler.Tick(0);
            var saved = RoundTrip(scheduler.CaptureSnapshot(10));
            var restoredWorld = world.PrepareRestore(world.CaptureSnapshot(),
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 2)).Value;
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));

            scheduler.RestoreSnapshot(saved, restoredWorld, null, 100);
            scheduler.Tick(100);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            scheduler.Tick(101.99);
            Assert.That(scheduler.GetLastOutcome("mina"), Is.Null);
            scheduler.Tick(102);
            Assert.That(scheduler.GetLastOutcome("mina").Code, Is.EqualTo("agent.decision_timeout"));
            Assert.That(client.Tokens[1].IsCancellationRequested, Is.True);
            Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(1));
            client.Complete(1, new NpcDecisionReply(NpcDecisionKind.Wait));
            scheduler.Tick(103);
            Assert.That(client.Requests.Count, Is.EqualTo(2));
        }

        [TestCase("\"activityDeadlineTotalMinutes\":780", "\"activityDeadlineTotalMinutes\":781")]
        [TestCase("\"nextStep\":2", "\"nextStep\":0")]
        [TestCase("\"calls\":1", "\"calls\":0")]
        [TestCase("\"expectedRevision\":1", "\"expectedRevision\":999")]
        public void DamagedContinuation_IsRejectedBeforeTheOriginalRequestLosesAuthority(string before, string after)
        {
            var world = World();
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile() }, client);
            scheduler.Tick(0);
            var saved = scheduler.CaptureSnapshot(1);
            var serializer = new DataContractJsonSerializer(typeof(NpcDecisionSchedulerSnapshot));
            using var output = new MemoryStream();
            serializer.WriteObject(output, saved);
            string json = Encoding.UTF8.GetString(output.ToArray());
            Assert.That(json, Does.Contain(before));
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(json.Replace(before, after)));
            var damaged = (NpcDecisionSchedulerSnapshot)serializer.ReadObject(input);
            var restoredWorld = world.PrepareRestore(world.CaptureSnapshot(),
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 2)).Value;

            Assert.Throws<ArgumentException>(() => scheduler.ValidateSnapshot(damaged, restoredWorld, null));

            Assert.That(client.Tokens[0].IsCancellationRequested, Is.False);
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Visit, "mina.work", NpcActivity.Working, 20));
            scheduler.Tick(2);
            Assert.That(world.GetState("mina").ActiveActivity, Is.Not.Null);
        }

        [Test]
        public void NewProcess_RestoresRemainingCooldownAndPreviousResultAtItsOwnTimeOrigin()
        {
            var world = World();
            var originalClient = new ControlledClient();
            using var original = new NpcDecisionScheduler(world, new[] { Profile() }, originalClient);
            original.Tick(0);
            originalClient.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));
            original.Tick(1);
            var self = world.GetState("mina");
            Assert.That(world.SubmitActivity(new NpcActivityRequest("mina", self.WorldRunId, self.Revision,
                "mina.rest", NpcActivity.Resting, 900)).IsSuccess, Is.True);
            self = world.GetState("mina");
            Assert.That(world.CancelActivity("mina", self.WorldRunId, self.Revision).IsSuccess, Is.True);
            original.Tick(2);
            var saved = RoundTrip(original.CaptureSnapshot(10));
            var restoredWorld = world.PrepareRestore(world.CaptureSnapshot(),
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 2)).Value;
            var client = new ControlledClient();
            using var restored = new NpcDecisionScheduler(restoredWorld, new[] { Profile() }, client);

            restored.RestoreSnapshot(saved, restoredWorld, null, 0);
            restored.Tick(19.99);
            Assert.That(client.Requests, Is.Empty);
            restored.Tick(20);

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(client.Requests[0].PreviousResultCode, Is.EqualTo("agent.decision_wait"));
            Assert.That(client.Requests[0].GameTotalMinutes, Is.EqualTo(720));
            Assert.That(client.Requests[0].Step, Is.EqualTo(1));
        }

        [Test]
        public void RestoredContinuation_SamplesObservationFromThePublishedWorldAtDispatch()
        {
            var world = World();
            var observedWorld = world;
            double bodyX = 0;
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("square", "Square", -10, -10, 10, 10) },
                Array.Empty<NpcObservationEntity>());
            var client = new ControlledClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { Profile() }, client,
                observe: request => scene.Read(request.NpcId, request.Self.WorldRunId, observedWorld.TotalMinutes,
                    new[] { new NpcObservationBody("mina", bodyX, 0) }));
            scheduler.Tick(0);
            var saved = RoundTrip(scheduler.CaptureSnapshot(1));
            var restoredWorld = world.PrepareRestore(world.CaptureSnapshot(),
                new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 2)).Value;
            client.Complete(0, new NpcDecisionReply(NpcDecisionKind.Wait));

            scheduler.RestoreSnapshot(saved, restoredWorld, null, 100);
            observedWorld = restoredWorld;
            observedWorld.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 745), 0, false, 2));
            bodyX = 2;
            scheduler.Tick(100);

            Assert.That(client.Requests.Count, Is.EqualTo(2));
            var observation = client.Requests[1].Observation;
            Assert.That(observation.WorldRunId, Is.EqualTo(restoredWorld.GetState("mina").WorldRunId));
            Assert.That(observation.WorldRunId, Is.Not.EqualTo(client.Requests[0].Observation.WorldRunId));
            Assert.That(observation.ObservedAtTotalMinutes, Is.EqualTo(745));
            Assert.That(observation.X, Is.EqualTo(2));
            Assert.That(client.Requests[1].MaxActivityDurationGameMinutes, Is.EqualTo(35));
        }

        private static NpcDefinition Profile(string id = "mina") => new NpcDefinition(id, id, id + " persona", "Hello");

        private static NpcAgentWorld World(bool reachable = true)
            => WorldFor(new[] { "mina" }, reachable);

        private static NpcAgentWorld WorldFor(string[] ids, bool reachable = true)
        {
            var world = new NpcAgentWorld(ids.Select(id => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
                id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 780, 1020, 1080)), (npcId, locationId) => reachable);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            return world;
        }

        private sealed class UndeclaredClient : INpcDecisionClient
        {
            private readonly ControlledClient _inner;
            public UndeclaredClient(ControlledClient inner) => _inner = inner;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
                => _inner.DecideAsync(request, cancellationToken);
        }

        private sealed class ControlledClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            public string SnapshotConfiguration { get; set; } = "fixture:controlled-v1";
            public Action OnCancellation { get; set; }
            public readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            private readonly List<TaskCompletionSource<NpcDecisionReply>> _replies = new List<TaskCompletionSource<NpcDecisionReply>>();

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                Tokens.Add(cancellationToken);
                if (OnCancellation != null) cancellationToken.Register(() => OnCancellation?.Invoke());
                var reply = new TaskCompletionSource<NpcDecisionReply>();
                _replies.Add(reply);
                return reply.Task;
            }

            public void Complete(int index, NpcDecisionReply reply) => _replies[index].SetResult(reply);
            public void Fail(int index, string code) => _replies[index].SetException(new NpcCandidateException(code));
        }
    }
}
