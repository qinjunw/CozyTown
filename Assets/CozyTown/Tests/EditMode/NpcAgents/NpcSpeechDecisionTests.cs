using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcSpeechDecisionTests
    {
        [TestCase(NpcSpeechMode.FreeText)]
        [TestCase(NpcSpeechMode.StructuredFacts)]
        public void MixedSpeechPaths_AreRejectedBeforeWritingConversationOrMemory(NpcSpeechMode mode)
        {
            var world = new NpcAgentWorld(new[] { "ren", "sora" }.Select(id => new NpcDailySchedule(id,
                id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest", id + ".afternoon",
                360, 480, 720, 810, 1020, 1080)));
            world.Observe(Time(720));
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("lunch", "sora", "ren", "pond",
                "sora.rest", "ren.rest", 720, 750, 780) }, (npc, location) => NpcMeetingPresence.Arrived);
            var meeting = board.Invite(world.GetState("sora"), "lunch").Value;
            board.Respond(world.GetState("ren"), meeting.Id, true);
            world.Observe(Time(750));
            board.Observe();
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond walk", -5, -5, 5, 5) },
                Array.Empty<NpcObservationEntity>());
            var reply = new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: meeting.Id, text: "I caught 999 carp.",
                speechFrame: new NpcSpeechFrame("report_observation", "sora:region_name", "neutral"));
            var client = new FixedClient(reply);
            using var scheduler = new NpcDecisionScheduler(world, new[] { new NpcDefinition("sora", "Sora", "Cook", "Hello") },
                client, meetings: board, speechMode: mode, observe: request => scene.Read("sora", request.Self.WorldRunId,
                    world.TotalMinutes, new[] { new NpcObservationBody("sora", 0, 0) }, request.Social));
            scheduler.Tick(0);
            scheduler.Tick(1);
            var outcome = scheduler.GetLastOutcome("sora");
            Assert.That(outcome.Code, Is.EqualTo("speech.mode_mismatch"));
            Assert.That(outcome.Reply, Is.SameAs(reply));
            Assert.That(board.GetCurrent("sora").Transcript, Is.Empty);
            Assert.That(board.GetMemories("sora").Any(item => item.Kind == "meeting.spoken"), Is.False);
            Assert.That(board.GetMemories("ren").Any(item => item.Kind == "meeting.spoken"), Is.False);
            Assert.That(outcome.Calls, Is.EqualTo(1));
        }

        private static WorldTimeProgress Time(int minute) => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, 1);
        private sealed class FixedClient : INpcDecisionClient
        {
            private readonly NpcDecisionReply _reply;
            internal FixedClient(NpcDecisionReply reply) => _reply = reply;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token) => Task.FromResult(_reply);
        }
    }
}
