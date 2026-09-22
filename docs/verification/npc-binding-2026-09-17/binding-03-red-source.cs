using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class NpcBindingPlayModeTests
    {
        private const string Ren = DefaultMvpIds.Npcs.Fisher;
        private const string Sora = DefaultMvpIds.Npcs.Cook;
        private GameObject _root;
        private CozyTownServices _services;
        private CozyTownTownLifeController _controller;
        private NpcWorldResident2D _ren, _sora;
        private PendingClient _client;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Binding fixture");
            _ren = CreateResident(Ren, 0);
            _sora = CreateResident(Sora, 40);
            _services = CozyTownCompositionRoot.CreateDefault();
            _services.WorldTime.AdvanceMinutes(360);
            _controller = _root.AddComponent<CozyTownTownLifeController>();
            _controller.enabled = false;
            _controller.Configure(_ren, _sora);
            _controller.Bind(_services.WorldTimeFlow, _services.ResourceTrading);
            _client = new PendingClient();
            _controller.ConfigureDecisions(_client, new[] {
                new NpcDefinition(Sora, "Sora", "Cook", "Hello"),
                new NpcDefinition(Ren, "Ren", "Fisher", "Hello") },
                new NpcDecisionSettings(maxRequestsPerMinute: 3, maxConcurrentRequests: 1),
                new[] { new NpcMeetingPlan("fish", Sora, Ren, "pond", Sora + ".rest", Ren + ".rest",
                    720, 750, 780, resourceTerms: new CharacterTradeTerms(Ren, Sora, DefaultMvpIds.Items.Carp, 1, 25)) });
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            foreach (var reply in _client.Replies) reply.TrySetResult(new NpcDecisionReply(NpcDecisionKind.Wait));
        }

        [Test]
        public void MissingResourceParticipant_PreservesInvitationClockAndPendingReply()
        {
            StartInvitation();
            var before = _controller.GetAgentState(Ren);
            var meeting = _controller.GetMeeting(Ren);
            var position = _ren.Position;
            var other = CozyTownCompositionRoot.CreateDefault();
            var invalidResources = EmptyResources();

            Assert.Throws<ArgumentException>(() => _controller.Bind(other.WorldTimeFlow, invalidResources));

            Assert.That(_controller.GetAgentState(Ren).WorldRunId, Is.EqualTo(before.WorldRunId));
            Assert.That(_controller.GetAgentState(Ren).Revision, Is.EqualTo(before.Revision));
            Assert.That(_controller.GetMeeting(Ren).Id, Is.EqualTo(meeting.Id));
            Assert.That(_controller.GetMeetingResources(Ren).OwnedQuantity, Is.EqualTo(2));
            Assert.That(_ren.Position, Is.EqualTo(position));
            other.WorldTime.AdvanceMinutes(10);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720));
            _services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(_controller.GameTotalMinutes, Is.EqualTo(720.5));
            _controller.TickDecisions(1);
            Assert.That(_client.Tokens[1].IsCancellationRequested, Is.False);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(2));
            _client.Replies[1].SetResult(new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: meeting.Id));
            _controller.TickDecisions(2);
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Scheduled));
            Assert.That(_controller.GetAgentState(Ren).ActiveActivity, Is.Not.Null);
            Assert.That(_controller.GetAgentState(Sora).ActiveActivity, Is.Not.Null);
        }

        private void StartInvitation()
        {
            _controller.TickDecisions(0);
            Assert.That(_client.Requests[0].Social.Kind, Is.EqualTo(NpcSocialContextKind.Opportunity));
            _client.Replies[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Invite, planId: "fish"));
            _controller.TickDecisions(0.1);
            _controller.TickDecisions(0.2);
            Assert.That(_controller.GetMeeting(Ren).State, Is.EqualTo(NpcMeetingState.Invited));
            Assert.That(_client.Requests[1].Social.Kind, Is.EqualTo(NpcSocialContextKind.Invitation));
        }

        [Test]
        public void MissingDecisionResident_PreservesWorldAndPendingActivityChoice()
        {
            _controller.ConfigureDecisions(_client, new[] { new NpcDefinition(Sora, "Sora", "Cook", "Hello") },
                new NpcDecisionSettings(maxRequestsPerMinute: 1, maxConcurrentRequests: 1));
            _controller.TickDecisions(0);
            var before = _controller.GetAgentState(Sora);
            var other = CozyTownCompositionRoot.CreateDefault();
            _controller.Configure(_ren);

            Assert.Throws<ArgumentException>(() => _controller.Bind(other.WorldTimeFlow, other.ResourceTrading));

            _controller.Configure(_ren, _sora);
            Assert.That(_controller.GetAgentState(Sora).WorldRunId, Is.EqualTo(before.WorldRunId));
            _controller.Bind(_services.WorldTimeFlow, _services.ResourceTrading);
            Assert.That(_controller.GetAgentState(Sora).WorldRunId, Is.EqualTo(before.WorldRunId));
            _controller.TickDecisions(1);
            Assert.That(_client.Tokens[0].IsCancellationRequested, Is.False);
            Assert.That(_controller.ActiveDecisionRequests, Is.EqualTo(1));
            _client.Replies[0].SetResult(new NpcDecisionReply(NpcDecisionKind.Visit, Sora + ".outside", NpcActivity.Resting, 60));
            _controller.TickDecisions(2);
            Assert.That(_controller.GetDecisionOutcome(Sora).ActivityAccepted, Is.True);
            Assert.That(_controller.DecisionRequestsInLastMinute, Is.EqualTo(1));
            _services.DaytimeClock.AdvanceElapsed(0.5);
            Assert.That(_sora.Position, Is.EqualTo(new Vector2(40, 19)));
        }

        private NpcWorldResident2D CreateResident(string id, float x)
        {
            var area = new GameObject(id + " map");
            area.transform.SetParent(_root.transform);
            var map = area.AddComponent<TownMap2D>();
            map.Configure(new[] { new TownHome(id + ".home", id, id + ".outside", id + ".entry") },
                new[] { new TownLocation(id + ".outside", new Vector2(x, 0)),
                    new TownLocation(id + ".entry", new Vector2(x, 0.5f)),
                    new TownLocation(id + ".work", new Vector2(x + 20, 0)),
                    new TownLocation(id + ".rest", new Vector2(x, 20)) },
                new[] { new TownRoad(id + ".entry", id + ".outside"),
                    new TownRoad(id + ".outside", id + ".work"), new TownRoad(id + ".outside", id + ".rest") });
            var actor = new GameObject(id);
            actor.transform.SetParent(area.transform);
            var resident = actor.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
                id + ".work", id + ".rest", id + ".work", 360, 480, 720, 810, 1020, 1080),
                actor.AddComponent<SpriteRenderer>());
            return resident;
        }

        private static CharacterResourceTrading EmptyResources()
            => new CharacterResourceTrading(new InMemoryEconomyStateStore(Array.Empty<CharacterEconomySnapshot>(),
                Array.Empty<ShopEconomySnapshot>()), DefaultMvpContent.CreateConfiguration().Items, 8);

        private sealed class PendingClient : INpcDecisionClient
        {
            public readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public readonly List<TaskCompletionSource<NpcDecisionReply>> Replies = new List<TaskCompletionSource<NpcDecisionReply>>();
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                Requests.Add(request);
                Tokens.Add(token);
                var reply = new TaskCompletionSource<NpcDecisionReply>();
                Replies.Add(reply);
                return reply.Task;
            }
        }
    }
}
