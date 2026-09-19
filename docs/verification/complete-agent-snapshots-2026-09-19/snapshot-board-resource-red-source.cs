using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcMeetingBoardSnapshotTests
    {
        [Test]
        public void CompletedResourceResultWithoutDeliveryReceipt_IsRejectedBeforeAnyLiveChange()
        {
            var fixture = new ResourceFixture();
            fixture.Board.Speak(fixture.World.GetState("sora"), fixture.MeetingId, "Here is the payment.");
            fixture.Board.Speak(fixture.World.GetState("ren"), fixture.MeetingId, "Here is the fish.");
            string json = Write(fixture.Board.CaptureSnapshot()).Replace("\"deliveryResultCode\":\"resource.delivered\"", "\"deliveryResultCode\":null");
            var restoredWorld = fixture.World.PrepareRestore(RoundTrip(fixture.World.CaptureSnapshot()), Time(720, 2)).Value;

            var prepared = fixture.Board.PrepareRestore(Read<NpcMeetingBoardSnapshot>(json), restoredWorld);

            Assert.That(prepared.IsSuccess, Is.False);
            Assert.That(prepared.ErrorCode, Is.EqualTo("meeting.snapshot_result_invalid"));
            Assert.That(fixture.Board.GetLatest("sora").DeliveryResultCode, Is.EqualTo("resource.delivered"));
            fixture.AssertDeliveredOnce();
        }

        private sealed class ResourceFixture
        {
            internal readonly NpcAgentWorld World;
            internal readonly NpcMeetingBoard Board;
            internal readonly Guid MeetingId;
            private readonly InMemoryEconomyStateStore _store;

            internal ResourceFixture()
            {
                World = new NpcAgentWorld(new[] { Schedule("ren", 720), Schedule("sora", 720) });
                World.Observe(Time(720));
                _store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) },
                    Array.Empty<ShopEconomySnapshot>());
                var resources = new CharacterResourceTrading(_store,
                    new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
                var plan = new NpcMeetingPlan("fish-supply", "sora", "ren", "pond", "sora.rest", "ren.rest",
                    720, 720, 780, maxTurns: 2, resourceTerms: new CharacterTradeTerms("ren", "sora", "fish", 1, 25));
                Board = new NpcMeetingBoard(World, new[] { plan }, (npc, location) => NpcMeetingPresence.Arrived, resources);
                MeetingId = Board.Invite(World.GetState("sora"), plan.Id).Value.Id;
                Assert.That(Board.Respond(World.GetState("ren"), MeetingId, true).IsSuccess, Is.True);
                Board.Observe();
                Assert.That(Board.Deliver(World.GetState("sora"), MeetingId).IsSuccess, Is.True);
            }

            internal void AssertDeliveredOnce()
            {
                _store.TryGetCharacter("ren", out var ren);
                _store.TryGetCharacter("sora", out var sora);
                Assert.That(ren.Backpack.Items.Single().Quantity, Is.EqualTo(1));
                Assert.That(sora.Backpack.Items.Single().Quantity, Is.EqualTo(1));
                Assert.That(ren.Wallet.Balance, Is.EqualTo(25));
                Assert.That(sora.Wallet.Balance, Is.EqualTo(25));
            }

            private static CharacterEconomySnapshot Character(string id, int fish, int coins)
                => new CharacterEconomySnapshot(id, new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>()
                    : new[] { new ItemStack("fish", fish) }), new WalletSnapshot(coins));
        }

        private static T RoundTrip<T>(T snapshot) => Read<T>(Write(snapshot));

        private static string Write<T>(T snapshot)
        {
            using var stream = new MemoryStream();
            new DataContractJsonSerializer(typeof(T)).WriteObject(stream, snapshot);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static T Read<T>(string json)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

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
