using System;
using System.Linq;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcAgentWorldTests
    {
        [Test]
        public void WorldTime_OnlyProducesRelevantResidentEventsAndKeepsSnapshotsIndependent()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina", 720), Schedule("ren", 735) });
            world.Observe(Time(719));
            var before = world.GetState("mina");
            Assert.That(world.TakeEvents("mina").Select(item => item.Kind),
                Is.EqualTo(new[] { NpcAgentEventKind.WorldRebuilt }));
            world.TakeEvents("ren");

            world.Observe(Time(719, 0.25));
            world.Observe(Time(719, 0.75));
            Assert.That(world.TakeEvents("mina"), Is.Empty);
            Assert.That(world.GetState("mina").Revision, Is.EqualTo(before.Revision));
            world.Observe(Time(720));

            Assert.That(world.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.rest"));
            Assert.That(world.GetState("ren").Target.TargetLocationId, Is.EqualTo("ren.work"));
            Assert.That(before.Target.TargetLocationId, Is.EqualTo("mina.work"));
            Assert.That(world.TakeEvents("mina").Select(item => item.Kind),
                Is.EqualTo(new[] { NpcAgentEventKind.ScheduleChanged }));
            Assert.That(world.TakeEvents("mina"), Is.Empty);
            Assert.That(world.TakeEvents("ren"), Is.Empty);
        }

        private static WorldTimeProgress Time(int minute, double fraction = 0, int day = 1, long rebuild = 1)
        {
            return new WorldTimeProgress(new GameClockSnapshot(day, minute), fraction, false, rebuild);
        }

        [Test]
        public void TemporaryActivity_SurvivesScheduleChangesAndExpiresIntoCurrentSchedule()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina"), Schedule("ren") });
            world.Observe(Time(719));
            var state = world.GetState("mina");
            Assert.That(world.SubmitActivity(new NpcActivityRequest("mina", state.WorldRunId, state.Revision,
                "mina.rest", NpcActivity.Resting, 800)).IsSuccess, Is.True);

            world.Observe(Time(790));
            Assert.That(world.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.rest"));
            Assert.That(world.GetState("ren").Target.TargetLocationId, Is.EqualTo("ren.afternoon"));
            world.Observe(Time(800));
            Assert.That(world.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.afternoon"));
            Assert.That(world.TakeEvents("mina").Select(item => item.Kind), Does.Contain(NpcAgentEventKind.ActivityExpired));
        }

        [Test]
        public void DuplicateAndBusyRequests_DoNotReplaceAnAcceptedActivity()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(719));
            var initial = world.GetState("mina");
            var request = new NpcActivityRequest("mina", initial.WorldRunId, initial.Revision,
                "mina.rest", NpcActivity.Resting, 800);
            Assert.That(world.SubmitActivity(request).IsSuccess, Is.True);
            var accepted = world.GetState("mina");
            world.TakeEvents("mina");

            Assert.That(world.SubmitActivity(request).ErrorCode, Is.EqualTo("agent.decision_stale"));
            Assert.That(world.SubmitActivity(new NpcActivityRequest("mina", accepted.WorldRunId, accepted.Revision,
                "mina.work", NpcActivity.Working, 900)).ErrorCode, Is.EqualTo("agent.busy"));
            Assert.That(world.CancelActivity("mina", initial.WorldRunId, initial.Revision).ErrorCode,
                Is.EqualTo("agent.decision_stale"));
            Assert.That(world.GetState("mina").ActiveActivity, Is.SameAs(request));
            Assert.That(world.GetState("mina").Revision, Is.EqualTo(accepted.Revision));
            Assert.That(world.TakeEvents("mina"), Is.Empty);
        }

        [TestCase(719)]
        [TestCase(700)]
        [TestCase(2160)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidDeadline_LeavesTheResidentUnchanged(double expires)
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(719));
            var initial = world.GetState("mina");
            world.TakeEvents("mina");

            Assert.That(world.SubmitActivity(new NpcActivityRequest("mina", initial.WorldRunId, initial.Revision,
                "mina.rest", NpcActivity.Resting, expires)).ErrorCode, Is.EqualTo("agent.deadline_invalid"));
            Assert.That(world.GetState("mina").Revision, Is.EqualTo(initial.Revision));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(world.TakeEvents("mina"), Is.Empty);
        }

        [TestCase("missing", "mina.rest", NpcActivity.Resting, "agent.npc_unknown")]
        [TestCase("mina", "missing", NpcActivity.Resting, "agent.target_unavailable")]
        [TestCase("mina", "ren.rest", NpcActivity.Resting, "agent.target_unavailable")]
        [TestCase("mina", "mina.rest", NpcActivity.Home, "agent.activity_invalid")]
        [TestCase("mina", "mina.rest", (NpcActivity)999, "agent.activity_invalid")]
        public void InvalidActivity_LeavesTheResidentUnchanged(string npcId, string location,
            NpcActivity activity, string error)
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina"), Schedule("ren") });
            world.Observe(Time(719));
            var initial = world.GetState("mina");
            world.TakeEvents("mina");

            Assert.That(world.SubmitActivity(new NpcActivityRequest(npcId, initial.WorldRunId, initial.Revision,
                location, activity, 800)).ErrorCode, Is.EqualTo(error));
            Assert.That(world.GetState("mina").Revision, Is.EqualTo(initial.Revision));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(world.TakeEvents("mina"), Is.Empty);
        }

        [Test]
        public void CancelAfterScheduleChange_RestoresTheCurrentSchedule()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(719));
            var initial = world.GetState("mina");
            Assert.That(world.SubmitActivity(new NpcActivityRequest("mina", initial.WorldRunId, initial.Revision,
                "mina.rest", NpcActivity.Resting, 900)).IsSuccess, Is.True);
            world.Observe(Time(790));
            var current = world.GetState("mina");

            Assert.That(world.CancelActivity("mina", current.WorldRunId, current.Revision).IsSuccess, Is.True);
            Assert.That(world.GetState("mina").Target.TargetLocationId, Is.EqualTo("mina.afternoon"));
            Assert.That(world.GetState("mina").ActiveActivity, Is.Null);
            Assert.That(current.ActiveActivity, Is.Not.Null, "Previously captured snapshots must remain unchanged.");
            Assert.That(world.TakeEvents("mina").Select(item => item.Kind), Does.Contain(NpcAgentEventKind.ActivityCancelled));
        }

        [Test]
        public void Rebuild_RejectsPreviousWorldEvenWhenRevisionMatches()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(719));
            var initial = world.GetState("mina");
            var oldRequest = new NpcActivityRequest("mina", initial.WorldRunId, initial.Revision,
                "mina.rest", NpcActivity.Resting, 900);
            Assert.That(world.SubmitActivity(oldRequest).IsSuccess, Is.True);
            world.Observe(Time(790));

            world.Observe(Time(719, rebuild: 2));

            var rebuilt = world.GetState("mina");
            Assert.That(rebuilt.WorldRunId, Is.Not.EqualTo(initial.WorldRunId));
            Assert.That(rebuilt.Revision, Is.EqualTo(initial.Revision));
            Assert.That(rebuilt.ActiveActivity, Is.Null);
            Assert.That(rebuilt.Target.TargetLocationId, Is.EqualTo("mina.work"));
            Assert.That(world.SubmitActivity(oldRequest).ErrorCode, Is.EqualTo("agent.world_stale"));
            Assert.That(world.TakeEvents("mina").Select(item => item.Kind),
                Is.EqualTo(new[] { NpcAgentEventKind.WorldRebuilt }));
        }

        [Test]
        public void UnconsumedEvents_CoalesceAcrossDaysAndKeepTheLatestTime()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(719));
            for (int day = 1; day <= 30; day++)
            {
                world.Observe(Time(720, day: day));
                world.Observe(Time(790, day: day));
            }

            var events = world.TakeEvents("mina");
            Assert.That(events.Count, Is.EqualTo(3));
            Assert.That(events.Select(item => item.Kind), Is.EquivalentTo(new[] {
                NpcAgentEventKind.WorldRebuilt, NpcAgentEventKind.DayChanged, NpcAgentEventKind.ScheduleChanged }));
            Assert.That(events.Single(item => item.Kind == NpcAgentEventKind.ScheduleChanged).TotalMinutes,
                Is.EqualTo(29 * 1440 + 790));
            Assert.That(events.Single(item => item.Kind == NpcAgentEventKind.DayChanged).TotalMinutes,
                Is.EqualTo(29 * 1440 + 720));
            Assert.That(world.TakeEvents("mina"), Is.Empty);
        }

        [Test]
        public void LargeJump_DoesNotBackfillExpiredScheduleOpportunities()
        {
            var schedule = new NpcDailySchedule("mina", "home", "outside", "entry",
                "work", "rest", "work", 360, 480, 720, 780, 1020, 1080);
            var world = new NpcAgentWorld(new[] { schedule });
            world.Observe(Time(719));
            var initial = world.GetState("mina");
            world.TakeEvents("mina");

            world.Observe(Time(790));

            Assert.That(world.GetState("mina").Target.TargetLocationId, Is.EqualTo("work"));
            Assert.That(world.GetState("mina").Revision, Is.EqualTo(initial.Revision));
            Assert.That(world.TakeEvents("mina"), Is.Empty);
        }

        [Test]
        public void TimeCannotMoveBackwardsWithoutRebuildingTheWorld()
        {
            var world = new NpcAgentWorld(new[] { Schedule("mina") });
            world.Observe(Time(719));
            var initial = world.GetState("mina");
            world.TakeEvents("mina");

            Assert.Throws<ArgumentException>(() => world.Observe(Time(700)));
            Assert.That(world.TotalMinutes, Is.EqualTo(719));
            Assert.That(world.GetState("mina").Revision, Is.EqualTo(initial.Revision));
            Assert.That(world.GetState("mina").WorldRunId, Is.EqualTo(initial.WorldRunId));
            Assert.That(world.TakeEvents("mina"), Is.Empty);
        }

        private static NpcDailySchedule Schedule(string id, int restMinute = 720)
        {
            return new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
                id + ".work", id + ".rest", id + ".afternoon", 360, 480, restMinute, 780, 1020, 1080);
        }
    }
}
