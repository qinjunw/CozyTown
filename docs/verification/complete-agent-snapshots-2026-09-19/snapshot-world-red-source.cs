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
