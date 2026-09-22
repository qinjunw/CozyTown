using System;
using System.IO;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Save
{
    public sealed class CompleteSnapshotStorageTests
    {
        [Test]
        public void CompleteSnapshot_RoundTripsFractionalClockAllResidentsAndIndependentEvents()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            services.DaytimeClock.AdvanceElapsed(0.125);
            var ids = DefaultMvpContent.CreateConfiguration().Npcs.Select(npc => npc.Id).ToArray();
            var world = new NpcAgentWorld(ids.Select(id => new NpcDailySchedule(id, id + ".home",
                id + ".outside", id + ".entry", id + ".work", id + ".rest", id + ".afternoon",
                360, 480, 720, 780, 1020, 1080)));
            world.Observe(services.WorldTimeFlow.Current);
            var bodies = ids.Select(id => new NpcBodySnapshot(id,
                new TownRouteSnapshot(new Position2DSnapshot(1, 2), new Position2DSnapshot(0, 1),
                    id + ".work", new[] { new Position2DSnapshot(1, 2) }, 1, 1, false),
                NpcActivity.Working, false)).ToArray();
            var complete = new CompleteWorldSnapshot("fixture-content-v1", "fixture-body-v1",
                world.CaptureSnapshot(), false, null, false, null, bodies,
                new PlayerBodySnapshot(new Position2DSnapshot(3, 4), new Position2DSnapshot(1, 0)));
            var economy = services.EconomyState.CaptureSnapshot();
            var snapshot = new GameSaveSnapshot(4, services.WorldSeed.Value, services.Time.Current,
                economy.Characters, economy.Shops, services.Farm.CaptureSnapshot(), services.Livestock.CaptureSnapshot(),
                0.25, complete);
            var directory = Path.Combine(Path.GetTempPath(), "CozyTown.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new JsonFileSaveStorage(Path.Combine(directory, "complete.json"));
                var saved = storage.Save("main", snapshot);
                Assert.That(saved.IsSuccess, Is.True, saved.ErrorCode);
                foreach (string id in ids) world.TakeEvents(id);
                var loaded = storage.Load("main");
                Assert.That(loaded.IsSuccess, Is.True, loaded.ErrorCode);
                Assert.That(loaded.Value.SchemaVersion, Is.EqualTo(4));
                Assert.That(loaded.Value.FractionalMinute, Is.EqualTo(0.25));
                Assert.That(loaded.Value.Characters.Select(character => character.CharacterId),
                    Is.EquivalentTo(economy.Characters.Select(character => character.CharacterId)));
                Assert.That(loaded.Value.CompleteWorld.Residents.Select(body => body.NpcId), Is.EquivalentTo(ids));
                Assert.That(loaded.Value.CompleteWorld.World.Residents.All(npc => npc.Events.Count > 0), Is.True);
                Assert.That(loaded.Value.CompleteWorld.Player.Position.X, Is.EqualTo(3));
                Assert.That(loaded.Value.CompleteWorld.ContentConfiguration, Is.EqualTo("fixture-content-v1"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
