using System;
using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcLocalObservationTests
    {
        [Test]
        public void DeliveryReceipt_RetainsItsTimeAfterCurrentHoldingsChange()
        {
            var world = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora") });
            world.Observe(Time(720));
            var store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) }, Array.Empty<ShopEconomySnapshot>());
            var trading = new CharacterResourceTrading(store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            var terms = new CharacterTradeTerms("ren", "sora", "fish", 1, 25);
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("supply", "sora", "ren", "pond", "sora.rest", "ren.rest",
                720, 750, 780, resourceTerms: terms) }, (npc, location) => NpcMeetingPresence.Arrived, trading);
            var id = board.Invite(world.GetState("sora"), "supply").Value.Id;
            board.Respond(world.GetState("ren"), id, true);
            world.Observe(Time(750));
            board.Observe();
            Assert.That(board.Deliver(world.GetState("sora"), id).IsSuccess, Is.True);
            store.CommitCharacter(Character("sora", 0, 25));
            world.Observe(Time(751));
            var scene = new NpcObservationScene(Array.Empty<NpcObservationRegion>(), Array.Empty<NpcObservationEntity>());
            var view = scene.Read("sora", world.GetState("sora").WorldRunId, 751, new[] { new NpcObservationBody("sora", 0, 0) },
                board.GetContext("sora"), trading.Inspect(terms, "sora"), board.GetMemories("sora"));
            var receipt = view.Facts.Single(f => f.Predicate == "delivery_result");
            Assert.That(receipt.Knowledge, Is.EqualTo("receipt"));
            Assert.That(receipt.Value, Is.EqualTo("resource.delivered"));
            Assert.That(receipt.ObservedAtTotalMinutes, Is.EqualTo(750));
            Assert.That(view.Facts.Single(f => f.Predicate == "delivered_quantity").Value, Is.EqualTo("1"));
            Assert.That(view.Facts.Single(f => f.Predicate == "owned_quantity").Value, Is.EqualTo("0"));
        }

        [Test]
        public void HeardStatements_KeepSpeakerAndTimeWithoutBecomingCurrentAssetsOrThirdPartyKnowledge()
        {
            var world = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora"), Schedule("mina") });
            world.Observe(Time(720));
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("chat", "sora", "ren", "pond", "sora.rest", "ren.rest",
                720, 750, 780, maxTurns: 4) }, (npc, location) => NpcMeetingPresence.Arrived);
            var id = board.Invite(world.GetState("sora"), "chat").Value.Id;
            Assert.That(board.Respond(world.GetState("ren"), id, true).IsSuccess, Is.True);
            world.Observe(Time(750));
            board.Observe();
            Assert.That(board.Speak(world.GetState("sora"), id, "I caught five fish today.").IsSuccess, Is.True);
            world.Observe(Time(751));
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("road", "Road", -10, -10, 10, 10) }, Array.Empty<NpcObservationEntity>());
            var bodies = new[] { new NpcObservationBody("ren", 0, 0), new NpcObservationBody("mina", 1, 0) };
            var view = scene.Read("ren", world.GetState("ren").WorldRunId, 751, bodies, social: board.GetContext("ren"), memories: board.GetMemories("ren"));
            var claim = view.Facts.Single(f => f.Knowledge == "statement");
            Assert.That(claim.SpeakerId, Is.EqualTo("sora"));
            Assert.That(claim.ObservedAtTotalMinutes, Is.EqualTo(750));
            Assert.That(claim.Value, Is.EqualTo("I caught five fish today."));
            Assert.That(claim.Predicate, Is.EqualTo("said"));
            Assert.That(claim.Source, Is.EqualTo("meeting_transcript"));
            Assert.That(view.Facts.Any(f => f.Predicate == "owned_quantity"), Is.False);
            var bystander = scene.Read("mina", world.GetState("mina").WorldRunId, 751, bodies, memories: board.GetMemories("mina"));
            Assert.That(bystander.Facts.Any(f => f.Knowledge == "statement"), Is.False);
        }

        [Test]
        public void RequiredOwnResources_AreSeparateFromOptionalObjectsAndListenerPermission()
        {
            var world = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora") });
            world.Observe(Time(720));
            var store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) }, Array.Empty<ShopEconomySnapshot>());
            var trading = new CharacterResourceTrading(store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            var terms = new CharacterTradeTerms("ren", "sora", "fish", 1, 25);
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("supply", "sora", "ren", "pond", "sora.rest", "ren.rest",
                720, 750, 780, resourceTerms: terms) }, resources: trading);
            board.Observe();
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("road", "Road", -10, -10, 10, 10) },
                new[] { new NpcObservationEntity("a", "A", 0, 0, "decoration"), new NpcObservationEntity("b", "B", 0, 0, "decoration") }, maxNearby: 1);
            var view = scene.Read("sora", world.GetState("sora").WorldRunId, 720, new[] { new NpcObservationBody("sora", 0, 0) },
                social: board.GetContext("sora"), ownResources: trading.Inspect(terms, "sora"));
            Assert.That(view.NearbyComplete, Is.False);
            Assert.That(view.ListenerId, Is.EqualTo("ren"));
            var quantity = view.Facts.Single(f => f.Predicate == "owned_quantity");
            Assert.That(quantity.Value, Is.EqualTo("0"));
            Assert.That(quantity.ValueType, Is.EqualTo("number"));
            Assert.That(quantity.Knowledge, Is.EqualTo("observed"));
            Assert.That(quantity.EntityId, Is.EqualTo("sora"));
            Assert.That(quantity.CanExpress, Is.True, "The current trade participant may state their relevant item count.");
            Assert.That(view.Facts.Single(f => f.Predicate == "balance").CanExpress, Is.False, "Full wallet balance is private planning input.");
            Assert.That(view.Facts.Single(f => f.Predicate == "terms_quantity").Value, Is.EqualTo("1"));
            Assert.That(view.Facts.Any(f => f.Predicate.StartsWith("agreed_")), Is.False,
                "An opportunity supplies proposed terms before either participant agrees.");
            Assert.That(view.Facts.Any(f => f.EntityId == "ren" && f.Predicate == "owned_quantity"), Is.False);
        }

        private static NpcDailySchedule Schedule(string id) => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
            id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 810, 1020, 1080);
        private static WorldTimeProgress Time(int minute) => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, 1);
        private static CharacterEconomySnapshot Character(string id, int fish, int coins) => new CharacterEconomySnapshot(id,
            new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>() : new[] { new ItemStack("fish", fish) }), new WalletSnapshot(coins));

        [Test]
        public void Coverage_FiltersPrivateAndIndoorBodiesBeforeBoundedOrdering()
        {
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("road", "Road", -10, -10, 10, 10) },
                new[] { new NpcObservationEntity("z-lamp", "Lamp", 0, 0, "decoration") }, maxNearby: 1);
            var bodies = new[] { new NpcObservationBody("ren", 0, 0), new NpcObservationBody("sora", 1, 0),
                new NpcObservationBody("a-indoor", 0, 0, "home"), new NpcObservationBody("a-hidden", 0, 0, observable: false) };
            var view = scene.Read("ren", Guid.NewGuid(), 720, bodies);
            Assert.That(view.NearbyEntityIds, Is.EqualTo(new[] { "sora" }));
            Assert.That(view.Facts.Single(f => f.EntityId == "sora" && f.Predicate == "present").Source, Is.EqualTo("resident_body"));
            Assert.That(view.Facts.Any(f => f.EntityId == "sora" && f.Predicate == "interaction"), Is.False,
                "Physical presence does not declare a resident's social actions unavailable.");
            Assert.That(view.Facts.Single(f => f.Predicate == "region_name").Value, Is.EqualTo("Road"));
            Assert.That(view.NearbyComplete, Is.False);
            Assert.That(view.CoverageDomain, Is.EqualTo("registered_entities_same_region_within_radius"));
            Assert.That(view.Facts.Any(f => f.EntityId.StartsWith("a-")), Is.False);
            var empty = scene.Read("ren", Guid.NewGuid(), 721, new[] { new NpcObservationBody("ren", 9, 9) });
            Assert.That(empty.NearbyEntityIds, Is.Empty);
            Assert.That(empty.NearbyComplete, Is.True);
            var outside = scene.Read("ren", Guid.NewGuid(), 722, new[] { new NpcObservationBody("ren", 11, 9) });
            Assert.That(outside.RegionId, Is.Null);
            Assert.That(outside.NearbyComplete, Is.False, "An unmapped region is unknown, not a complete empty world.");
        }

        [Test]
        public void DecorativeObjectsAndUnmodelledFish_HaveDifferentCapabilitiesAndNoInventedQuantity()
        {
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond", -10, -10, 10, 10) },
                new[] { new NpcObservationEntity("lamp", "Road lamp", 0, 0, "decoration"),
                    new NpcObservationEntity("water", "Pond", 1, 0, "landmark", interactionId: "fishing", resourceItemId: "fish") });
            var view = scene.Read("ren", Guid.NewGuid(), 720, new[] { new NpcObservationBody("ren", 0, 0) });
            var lamp = view.Facts.Single(fact => fact.EntityId == "lamp" && fact.Predicate == "interaction");
            Assert.That(lamp.Value, Is.EqualTo("none"));
            Assert.That(view.Facts.Any(fact => fact.EntityId == "lamp" && fact.Predicate == "quantity"), Is.False);
            var fish = view.Facts.Single(fact => fact.EntityId == "water" && fact.Predicate == "quantity");
            Assert.That(fish.Knowledge, Is.EqualTo("unknown"));
            Assert.That(fish.ValueType, Is.EqualTo("unknown"));
            Assert.That(fish.Value, Is.Null);
            Assert.That(fish.Unit, Is.EqualTo("fish"));
            Assert.That(fish.Source, Is.EqualTo("semantic_catalog.unmodelled_quantity"));
            Assert.That(fish.ObserverId, Is.EqualTo("ren"));
            Assert.That(fish.ObservedAtTotalMinutes, Is.EqualTo(720));
        }

        [Test]
        public void ActualRegionAndDistance_DetermineNearbyObjects()
        {
            var scene = new NpcObservationScene(new[] {
                new NpcObservationRegion("lane", "Lane", -10, -10, 0, 10),
                new NpcObservationRegion("pond", "Pond walk", 0, -10, 10, 10) },
                new[] { new NpcObservationEntity("water", "Pond", 1, 0, "landmark") }, radius: 3);
            var run = Guid.NewGuid();
            var approach = scene.Read("ren", run, 720, new[] { new NpcObservationBody("ren", -0.1, 0) });
            Assert.That(approach.RegionId, Is.EqualTo("lane"));
            Assert.That(approach.NearbyEntityIds, Is.Empty, "A nearby object across the region boundary is not observed.");
            var arrived = scene.Read("ren", run, 721, new[] { new NpcObservationBody("ren", 0, 0) });
            Assert.That(arrived.RegionId, Is.EqualTo("pond"), "Rectangles own their minimum edges, excluding maximum edges.");
            Assert.That(arrived.NearbyEntityIds, Is.EqualTo(new[] { "water" }));
            var far = scene.Read("ren", run, 722, new[] { new NpcObservationBody("ren", 4.01, 0) });
            Assert.That(far.NearbyEntityIds, Is.Empty);
            Assert.That(arrived.NearbyEntityIds, Is.EqualTo(new[] { "water" }), "Previously returned observations remain snapshots.");
        }
    }
}
