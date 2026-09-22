using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Town;
using CozyTown.Unity.Npc;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed partial class CompleteWorldSnapshotPlayModeTests
    {
        [Test]
        public void CaptureTwice_DoesNotConsumeEventsDispatchRequestsOrAdvanceBodies()
        {
            CreateWorld();
            var client = ConfigureClient();
            var positions = _residents.Select(item => item.Position).ToArray();
            double time = _services.WorldTimeFlow.Current.TotalMinutes;
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var first = _services.SaveStorage.Load("main").Value.CompleteWorld;
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var second = _services.SaveStorage.Load("main").Value.CompleteWorld;

            Assert.That(client.Requests, Is.Empty);
            Assert.That(_controller.DecisionRequestsStarted, Is.Zero);
            Assert.That(_services.WorldTimeFlow.Current.TotalMinutes, Is.EqualTo(time));
            for (int i = 0; i < _residents.Length; i++)
            {
                var events = first.World.Residents[i].Events;
                Assert.That(events, Is.Not.Empty);
                CollectionAssert.AreEqual(events.Select(item => item.Kind), second.World.Residents[i].Events.Select(item => item.Kind));
                CollectionAssert.AreEqual(events.Select(item => item.Kind), _controller.TakeAgentEvents(_residents[i].NpcId).Select(item => item.Kind));
                Assert.That(_controller.TakeAgentEvents(_residents[i].NpcId), Is.Empty);
                Assert.That(_residents[i].Position, Is.EqualTo(positions[i]));
            }
        }

        [Test]
        public void BlockedBody_FullSaveKeepsStopAndReplanStateWithoutRetrying()
        {
            CreateWorld();
            var wall = AddWall(new Vector2(10, 0), new Vector2(1, 3));
            _services.DaytimeClock.AdvanceElapsed(10);
            Assert.That(_residents[0].Status, Is.EqualTo(TownRouteStatus.Blocked));
            var stopped = _residents[0].Position;
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            Assert.That(_services.SaveStorage.Load("main").Value.CompleteWorld.Residents[0].Route.HasReplanned, Is.True);
            _services.DaytimeClock.AdvanceElapsed(1);

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            Assert.That(_residents[0].Position, Is.EqualTo(stopped));
            Assert.That(_residents[0].Status, Is.EqualTo(TownRouteStatus.Blocked));
            UnityEngine.Object.DestroyImmediate(wall.gameObject);
            _services.DaytimeClock.AdvanceElapsed(1);
            Assert.That(_residents[0].Position, Is.EqualTo(stopped));
            Assert.That(_residents[0].Status, Is.EqualTo(TownRouteStatus.Blocked));
        }

        [Test]
        public void NoLegalPosition_FullSaveRestoresHiddenBlockedResidentWithoutFindingAnotherPosition()
        {
            CreateWorld();
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var saved = _services.SaveStorage.Load("main").Value;
            Assert.That(_services.SaveStorage.Save("main", Legacy(saved)).IsSuccess, Is.True);
            AddWall(new Vector2(10, 5), new Vector2(24, 14));
            var trigger = _residents[0].gameObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(_residents[0].CaptureSnapshot().NoLegalPosition, Is.True);
            Vector2 hidden = _residents[0].Position;
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            _services.DaytimeClock.AdvanceElapsed(1);

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            Assert.That(_residents[0].Position, Is.EqualTo(hidden));
            Assert.That(_residents[0].CaptureSnapshot().NoLegalPosition, Is.True);
            Assert.That(_residents[0].Status, Is.EqualTo(TownRouteStatus.Blocked));
            Assert.That(_residents[0].IsHome, Is.False);
            Assert.That(_residents[0].IsPresentInWorld, Is.False);
            Assert.That(_residents[0].GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(trigger.enabled, Is.False);
        }

        [Test]
        public void HomeBodies_FullSaveRestoresArrivalAndHiddenPresence()
        {
            CreateWorld();
            _services.WorldTime.AdvanceMinutes(800);
            Assert.That(_residents.All(item => item.IsHome), Is.True);
            var positions = _residents.Select(item => item.Position).ToArray();
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            _services.WorldTime.AdvanceMinutes(800);
            Assert.That(_residents.All(item => item.IsHome), Is.False);

            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);

            for (int i = 0; i < _residents.Length; i++)
            {
                Assert.That(_residents[i].Position, Is.EqualTo(positions[i]));
                Assert.That(_residents[i].Status, Is.EqualTo(TownRouteStatus.Arrived));
                Assert.That(_residents[i].IsHome, Is.True);
                Assert.That(_residents[i].IsPresentInWorld, Is.False);
            }
        }

        [Test]
        public void FailedLastBodyPreparation_DoesNotCancelStillLegalModelRequest()
        {
            CreateWorld();
            var client = ConfigureClient(holdReplies: true);
            _services.WorldTime.AdvanceMinutes(360);
            _controller.TickDecisions(_realSeconds);
            Assert.That(client.Requests.Count, Is.EqualTo(4));
            var request = client.Requests[0];
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var saved = _services.SaveStorage.Load("main").Value;
            var bodies = saved.CompleteWorld.Residents.ToArray();
            var last = bodies[bodies.Length - 1];
            var route = last.Route;
            bodies[bodies.Length - 1] = new NpcBodySnapshot(last.NpcId,
                new TownRouteSnapshot(new Position2DSnapshot(999, 999), route.Facing, route.TargetLocationId,
                    route.Waypoints, route.WaypointIndex, route.Status, route.HasReplanned), last.Activity, last.NoLegalPosition);
            var old = saved.CompleteWorld;
            var invalid = new CompleteWorldSnapshot(old.ContentConfiguration, old.BodyConfiguration,
                old.World, old.MeetingsEnabled, old.Meetings, old.DecisionsEnabled, old.Decisions, bodies, old.Player);
            Assert.That(_services.SaveStorage.Save("main", new GameSaveSnapshot(saved.SchemaVersion,
                saved.WorldSeed, saved.Clock, saved.Characters, saved.Shops, saved.Farm, saved.Livestock,
                saved.FractionalMinute, invalid)).IsSuccess, Is.True);

            Assert.That(_services.GameSave.Load().IsSuccess, Is.False);
            Assert.That(client.Tokens.All(token => !token.IsCancellationRequested), Is.True);
            Assert.That(_controller.GetAgentState(request.NpcId).WorldRunId, Is.EqualTo(request.Self.WorldRunId));
            client.Complete(0);
            _realSeconds = 1;
            _controller.TickDecisions(_realSeconds);
            Assert.That(_controller.GetAgentState(request.NpcId).ActiveActivity, Is.Not.Null);
            Assert.That(_controller.GetAgentState(request.NpcId).ActiveActivity.TargetLocationId,
                Is.EqualTo(request.NpcId + ".work"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PublicationFailure_ReportsCommittedStateAndPreservesExistingRecoveryBoundary(bool core)
        {
            CreateWorld();
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var oldRun = _controller.GetAgentState(_residents[0].NpcId).WorldRunId;
            _services.WorldTime.AdvanceMinutes(10);
            Action<WorldTimeProgress> fail = _ => throw new InvalidOperationException("snapshot publication fixture");
            if (core) _services.WorldTimeFlow.Changed += fail;
            else _services.WorldTimeFlow.PresentationChanged += fail;

            var result = _services.GameSave.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(_services.Time.Current.MinuteOfDay, Is.EqualTo(360));
            Assert.That(_controller.GetAgentState(_residents[0].NpcId).WorldRunId, Is.Not.EqualTo(oldRun));
            Assert.That(_services.WorldTimeFlow.State,
                Is.EqualTo(core ? WorldTimeFlowState.RecoveryRequired : WorldTimeFlowState.Ready));
            if (core) _services.WorldTimeFlow.Changed -= fail;
            else _services.WorldTimeFlow.PresentationChanged -= fail;
            Assert.That(_services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.EqualTo(!core));
            Assert.That(_services.GameSave.Load().IsSuccess, Is.True);
        }

        [Test]
        public void FixedInputsAfterRepeatedRestore_ProduceSameRequestsBodiesActivitiesAndResources()
        {
            CreateWorld();
            var client = ConfigureClient();
            _world.GetComponent<TownLocalObservation2D>().Configure(new NpcObservationScene(new[] {
                new NpcObservationRegion("shared-road", "Shared road", -10, -10, 200, 20) },
                new[] { new NpcObservationEntity("road-sign", "Road sign", 0, 0, "landmark") }, radius: 200),
                Array.Empty<NpcObservationRegion>());
            _services.WorldTime.AdvanceMinutes(360);
            Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
            var original = _services.SaveStorage.Load("main").Value;
            string[] expectedRequests = null, expectedState = null;
            Guid firstRun = Guid.Empty;
            Guid firstDecision = Guid.Empty;
            for (int run = 0; run < 2; run++)
            {
                _realSeconds = 100 * (run + 1);
                Assert.That(_services.Wallet.Credit(17).IsSuccess, Is.True);
                Assert.That(_services.SaveStorage.Save("main", original).IsSuccess, Is.True);
                Assert.That(_services.GameSave.Load().IsSuccess, Is.True);
                int requestStart = client.Requests.Count;
                _controller.TickDecisions(_realSeconds);
                _realSeconds += 0.1;
                _controller.TickDecisions(_realSeconds);
                Assert.That(_services.DaytimeClock.AdvanceElapsed(0.25).IsSuccess, Is.True);
                var requests = client.Requests.Skip(requestStart).ToArray();
                Assert.That(requests.Length, Is.EqualTo(4));
                Assert.That(requests.All(request => request.Observation.RegionId == "shared-road"
                    && request.Observation.NearbyEntityIds.Count == 4), Is.True);
                var requestValues = requests.Select(request => string.Join("|", request.NpcId, request.Step,
                    request.GameTotalMinutes.ToString("R", CultureInfo.InvariantCulture),
                    string.Join(",", request.AllowedOperations), request.Observation.RegionId,
                    request.Observation.X.ToString("R", CultureInfo.InvariantCulture),
                    request.Observation.Y.ToString("R", CultureInfo.InvariantCulture),
                    string.Join(",", request.Observation.NearbyEntityIds),
                    string.Join(",", request.Observation.Facts.Select(fact => fact.Predicate + "=" + fact.Value)))).ToArray();
                Assert.That(_services.GameSave.Save().IsSuccess, Is.True);
                var after = _services.SaveStorage.Load("main").Value;
                var values = after.CompleteWorld.Residents.Select(body => string.Join("|", body.NpcId,
                    body.Route.Position.X, body.Route.Position.Y, body.Route.Facing.X, body.Route.Facing.Y,
                    body.Route.Status, body.Route.WaypointIndex, body.Route.HasReplanned, body.Route.TargetLocationId))
                    .Concat(after.CompleteWorld.World.Residents.Select(resident => string.Join("|", resident.NpcId,
                        resident.Revision, resident.Activity?.TargetLocationId, resident.Activity?.StartsAtTotalMinutes,
                        resident.Activity?.ExpiresAtTotalMinutes)))
                    .Concat(after.Characters.Select(character => string.Join("|", character.CharacterId,
                        character.Wallet.Balance, string.Join(",", character.Backpack.Items.Select(item => item.ItemId + ":" + item.Quantity)))))
                    .ToArray();
                if (run == 0)
                {
                    expectedRequests = requestValues;
                    expectedState = values;
                    firstRun = requests[0].Self.WorldRunId;
                    firstDecision = requests[0].DecisionId;
                }
                else
                {
                    CollectionAssert.AreEqual(expectedRequests, requestValues);
                    CollectionAssert.AreEqual(expectedState, values);
                    Assert.That(requests[0].Self.WorldRunId, Is.Not.EqualTo(firstRun));
                    Assert.That(requests[0].DecisionId, Is.Not.EqualTo(firstDecision));
                }
            }
            Assert.That(client.Requests.Count, Is.EqualTo(8), "Both runs remain counted in the current process request ledger.");
        }

        private SnapshotClient ConfigureClient(bool holdReplies = false)
        {
            var client = new SnapshotClient(holdReplies);
            _controller.ConfigureDecisions(client, DefaultMvpContent.CreateConfiguration().Npcs,
                new NpcDecisionSettings(maxRequestsPerMinute: 100, maxConcurrentRequests: 4));
            return client;
        }

        private BoxCollider2D AddWall(Vector2 position, Vector2 size)
        {
            var wall = new GameObject("Snapshot obstacle");
            wall.transform.SetParent(_world.transform);
            wall.transform.position = position;
            var collider = wall.AddComponent<BoxCollider2D>();
            collider.size = size;
            Physics2D.SyncTransforms();
            return collider;
        }

        private static GameSaveSnapshot Legacy(GameSaveSnapshot saved)
            => new GameSaveSnapshot(3, saved.WorldSeed, saved.Clock, saved.Characters, saved.Shops, saved.Farm, saved.Livestock);

        private sealed class SnapshotClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            private readonly bool _hold;
            private readonly List<TaskCompletionSource<NpcDecisionReply>> _replies = new List<TaskCompletionSource<NpcDecisionReply>>();
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public string SnapshotConfiguration => "fixed-visit-fixture/v1";
            internal SnapshotClient(bool hold) => _hold = hold;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                Tokens.Add(cancellationToken);
                var reply = new TaskCompletionSource<NpcDecisionReply>();
                _replies.Add(reply);
                if (!_hold) Complete(_replies.Count - 1);
                return reply.Task;
            }
            internal void Complete(int index) => _replies[index].SetResult(new NpcDecisionReply(NpcDecisionKind.Visit,
                Requests[index].NpcId + ".work", NpcActivity.Working, 10));
        }
    }
}
