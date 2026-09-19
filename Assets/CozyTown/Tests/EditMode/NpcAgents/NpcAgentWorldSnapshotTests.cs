using System;
using System.Linq;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcAgentWorldSnapshotTests
    {
        [Test]
        public void PendingEvents_RestoreOriginalOrderEvenWhenExpiryHasAnEarlierTimestamp()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(730));
            var actor = world.GetState("mina");
            world.SubmitActivity(new NpcActivityRequest("mina", actor.WorldRunId, actor.Revision, "mina.rest", NpcActivity.Resting, 780));
            world.TakeEvents("mina");
            world.Observe(Time(790));

            var prepared = world.PrepareRestore(world.CaptureSnapshot(), Time(790, 0, 2));

            Assert.That(prepared.IsSuccess, Is.True, prepared.ErrorCode);
            var events = prepared.Value.TakeEvents("mina");
            Assert.That(events.Select(item => item.Kind), Is.EqualTo(new[] { NpcAgentEventKind.ScheduleChanged, NpcAgentEventKind.ActivityExpired }));
            Assert.That(events.Select(item => item.TotalMinutes), Is.EqualTo(new[] { 790d, 780d }));
            Assert.That(world.TakeEvents("mina").Count, Is.EqualTo(2));
            Assert.That(prepared.Value.TakeEvents("mina"), Is.Empty);
        }

        [TestCase("missing_resident")]
        [TestCase("future_event")]
        [TestCase("invalid_deadline")]
        [TestCase("future_ordinary_activity")]
        [TestCase("different_schedule")]
        public void InvalidWorldSnapshot_LeavesTheLiveActivityCancellable(string corruption)
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(730));
            var actor = world.GetState("mina");
            world.SubmitActivity(new NpcActivityRequest("mina", actor.WorldRunId, actor.Revision, "mina.rest", NpcActivity.Resting, 780));
            var saved = world.CaptureSnapshot();
            var resident = saved.Residents[0];
            var corruptResident = new NpcResidentStateSnapshot(resident.NpcId, resident.Revision,
                corruption == "invalid_deadline" ? new NpcActivitySnapshot(resident.Activity.ActivityId,
                    "mina.rest", NpcActivity.Resting, 730, double.NaN)
                    : corruption == "future_ordinary_activity" ? new NpcActivitySnapshot(resident.Activity.ActivityId,
                        "mina.rest", NpcActivity.Resting, 750, 780) : resident.Activity,
                corruption == "future_event" ? new[] { new NpcAgentEvent(NpcAgentEventKind.WorldRebuilt, 731) } : resident.Events);
            var corrupt = new NpcAgentWorldSnapshot(730,
                corruption == "different_schedule" ? new[] { Schedule("ren") } : saved.Schedules,
                corruption == "missing_resident" ? Array.Empty<NpcResidentStateSnapshot>() : new[] { corruptResident });
            var live = world.GetState("mina");

            Assert.That(world.PrepareRestore(corrupt, Time(730, 0, 2)).IsSuccess, Is.False);

            Assert.That(world.TotalMinutes, Is.EqualTo(730));
            Assert.That(world.CancelActivity("mina", live.WorldRunId, live.Revision).IsSuccess, Is.True);
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
        }

        [Test]
        public void KnownActivityTarget_RestoresWhenCurrentlyUnreachable()
        {
            bool blocked = false;
            var world = new NpcAgentWorld(new[] { Schedule("ren") }, (npc, location) => !blocked);
            world.Observe(Time(730, 0.25));
            var actor = world.GetState("ren");
            Assert.That(world.SubmitActivity(new NpcActivityRequest("ren", actor.WorldRunId, actor.Revision,
                "pond.meeting", NpcActivity.Resting, 780)).IsSuccess, Is.True);
            var saved = world.CaptureSnapshot();
            blocked = true;

            var prepared = world.PrepareRestore(saved, Time(730, 0.25, 2), (npc, location) => location == "pond.meeting");

            Assert.That(prepared.IsSuccess, Is.True, prepared.ErrorCode);
            Assert.That(prepared.Value.GetState("ren").ActiveActivity.ActivityId, Is.EqualTo(saved.Residents[0].Activity.ActivityId));
            Assert.That(prepared.Value.GetState("ren").Target.TargetLocationId, Is.EqualTo("pond.meeting"));
            prepared.Value.Observe(Time(780, 0, 2));
            Assert.That(prepared.Value.GetState("ren").ActiveActivity, Is.Null);
            Assert.That(prepared.Value.GetState("ren").Target.TargetLocationId, Is.EqualTo("ren.afternoon"));
        }

        [Test]
        public void CaptureAndPrepare_RestoresActivityDeadlineAndEventsWithoutChangingLiveWorld()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(730, 0.25));
            world.TakeEvents("mina");
            var actor = world.GetState("mina");
            var accepted = new NpcActivityRequest("mina", actor.WorldRunId, actor.Revision,
                "mina.rest", NpcActivity.Resting, 780);
            Assert.That(world.SubmitActivity(accepted).IsSuccess, Is.True);

            var saved = world.CaptureSnapshot();
            Assert.That(world.TotalMinutes, Is.EqualTo(730.25));
            Assert.That(world.TakeEvents("mina").Select(item => item.Kind),
                Is.EqualTo(new[] { NpcAgentEventKind.ActivityAccepted }));
            world.Observe(Time(790));
            var prepared = world.PrepareRestore(saved, Time(730, 0.25, 2));

            Assert.That(prepared.IsSuccess, Is.True);
            Assert.That(world.TotalMinutes, Is.EqualTo(790));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            var restored = prepared.Value;
            Assert.That(restored.TotalMinutes, Is.EqualTo(730.25));
            Assert.That(restored.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.rest"));
            Assert.That(restored.GetState("mina").ActiveActivity.ExpiresAtTotalMinutes, Is.EqualTo(780));
            Assert.That(restored.TakeEvents("mina").Select(item => item.Kind),
                Is.EqualTo(new[] { NpcAgentEventKind.ActivityAccepted }));
            Assert.That(restored.SubmitActivity(accepted).ErrorCode, Is.EqualTo("agent.world_stale"));

            restored.Observe(Time(780, 0, 2));
            Assert.That(restored.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(restored.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.afternoon"));
        }

        private static WorldTimeProgress Time(int minute, double fraction = 0, long rebuild = 1)
            => new WorldTimeProgress(new GameClockSnapshot(1, minute), fraction, false, rebuild);

        private static NpcDailySchedule Schedule(string id)
            => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
                id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 780, 1020, 1080);
    }
}
