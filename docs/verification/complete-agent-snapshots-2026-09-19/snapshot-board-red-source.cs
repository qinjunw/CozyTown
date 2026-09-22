using System.Linq;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcMeetingBoardSnapshotTests
    {
        [Test]
        public void PreparedMeeting_ContinuesOriginalAppointmentWithoutAcceptingItAgain()
        {
            var world = new NpcAgentWorld(new[] { Schedule("ren", 720), Schedule("sora", 750) });
            world.Observe(Time(720));
            var plan = new NpcMeetingPlan("lunch", "ren", "sora", "pond", "ren.rest", "sora.rest",
                720, 750, 780, maxTurns: 2);
            var board = new NpcMeetingBoard(world, new[] { plan }, (npc, location) => NpcMeetingPresence.Arrived);
            var meetingId = board.Invite(world.GetState("ren"), "lunch").Value.Id;
            Assert.That(board.Respond(world.GetState("sora"), meetingId, true).IsSuccess, Is.True);
            var savedWorld = world.CaptureSnapshot();
            var savedMeetings = board.CaptureSnapshot();
            board.CancelAll();

            var restoredWorld = world.PrepareRestore(savedWorld, Time(720, 2)).Value;
            var prepared = board.PrepareRestore(savedMeetings, restoredWorld);

            Assert.That(prepared.IsSuccess, Is.True, prepared.ErrorCode);
            Assert.That(board.GetCurrent("ren"), Is.Null);
            var restored = prepared.Value;
            Assert.That(restored.GetCurrent("ren").Id, Is.EqualTo(meetingId));
            Assert.That(restored.GetCurrent("sora").State, Is.EqualTo(NpcMeetingState.Scheduled));
            Assert.That(restored.IsPlaceReserved("pond"), Is.True);
            Assert.That(restoredWorld.GetTargetAt("sora", 749).TargetLocationId, Is.EqualTo("sora.work"));
            Assert.That(restored.GetMemories("ren").Select(item => item.Kind),
                Is.EqualTo(new[] { "meeting.invited", "meeting.accepted" }));

            restoredWorld.Observe(Time(750, 2));
            restored.Observe();
            Assert.That(restored.GetCurrent("ren").State, Is.EqualTo(NpcMeetingState.Talking));
            Assert.That(restored.Speak(restoredWorld.GetState("ren"), meetingId, "The pond is quiet.").IsSuccess, Is.True);
            Assert.That(restored.Speak(restoredWorld.GetState("sora"), meetingId, "Let's enjoy lunch.").IsSuccess, Is.True);
            Assert.That(restored.GetLatest("ren").State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(restoredWorld.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(restoredWorld.GetState("sora").ActiveActivity, Is.Null);
            Assert.That(restored.IsPlaceReserved("pond"), Is.False);
        }

        private static NpcDailySchedule Schedule(string id, int rest)
            => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest",
                id + ".afternoon", 360, 480, rest, 810, 1020, 1080);

        private static WorldTimeProgress Time(int minute, long rebuild = 1)
            => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, rebuild);
    }
}
