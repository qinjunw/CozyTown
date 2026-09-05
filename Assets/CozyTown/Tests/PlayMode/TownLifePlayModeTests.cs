using CozyTown.Runtime.Core;
using CozyTown.Runtime.Content;
using System.Linq;
using CozyTown.Runtime.NpcLife;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.PlayMode
{
    public sealed class TownLifePlayModeTests
    {
        private GameObject world;
        private CozyTownServices services;
        private NpcWorldResident2D resident;

        [SetUp]
        public void SetUp()
        {
            world = new GameObject("Town life fixture");
            var map = world.AddComponent<TownMap2D>();
            map.Configure(new[] { new TownHome("home.mina", "npc.mina", "outside", "entry") },
                new[] { new TownLocation("outside", Vector2.zero),
                    new TownLocation("entry", new Vector2(0, 0.5f)),
                    new TownLocation("work", new Vector2(20, 0)),
                    new TownLocation("rest", new Vector2(20, 10)) },
                new[] { new TownRoad("entry", "outside"), new TownRoad("outside", "work"),
                    new TownRoad("work", "rest") });
            var actor = new GameObject("Mina");
            actor.transform.SetParent(world.transform);
            actor.AddComponent<TownInteractionPoint2D>().Configure(TownInteractionKind.Npc, "Talk");
            actor.AddComponent<BoxCollider2D>().isTrigger = true;
            resident = actor.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, new NpcDailySchedule("npc.mina", "home.mina", "outside", "entry",
                "work", "rest", "work", 360, 480, 720, 780, 1020, 1080),
                actor.AddComponent<SpriteRenderer>());
            services = CozyTownCompositionRoot.CreateDefault();
            var controller = world.AddComponent<CozyTownTownLifeController>();
            controller.Configure(resident);
            controller.Bind(services.WorldTimeFlow);
        }

        [Test]
        public void AcceptedElapsedTime_MovesResidentTowardWorkWithoutClaimingArrival()
        {
            Assert.That(services.DaytimeClock.AdvanceElapsed(5).IsSuccess, Is.True);
            Assert.That(resident.Position.x, Is.EqualTo(10).Within(0.001f));
            Assert.That(resident.Position.y, Is.EqualTo(0).Within(0.001f));
            Assert.That(resident.TargetLocationId, Is.EqualTo("work"));
            Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Travelling));
            Assert.That(resident.IsHome, Is.False);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(world);

        [Test]
        public void ReturnHome_HidesBodyAndDisablesInteractionOnlyAfterArrival()
        {
            services.WorldTime.AdvanceMinutes(660);
            Assert.That(resident.IsHome, Is.False);
            services.WorldTime.AdvanceMinutes(30);
            Assert.That(resident.Position, Is.EqualTo(new Vector2(0, 0.5f)));
            Assert.That(resident.IsHome, Is.True);
            Assert.That(resident.GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(resident.GetComponent<TownInteractionPoint2D>().CanInteract(new InteractionContext(world)), Is.False);
            Assert.That(resident.GetComponent<Collider2D>().enabled, Is.False);
        }

        [Test]
        public void Sleep_CrossesRestReturnMidnightAndDepartureFromActualJourney()
        {
            services.WorldTime.AdvanceMinutes(350);
            Assert.That(resident.Position, Is.EqualTo(new Vector2(20, 0)));
            Assert.That(services.Sleep.SleepForMinutes(60).IsSuccess, Is.True);
            Assert.That(resident.Position, Is.EqualTo(new Vector2(20, 10)));
            Assert.That(resident.TargetLocationId, Is.EqualTo("rest"));
            services.WorldTime.AdvanceMinutes(220);
            Assert.That(services.Sleep.SleepForMinutes(60).IsSuccess, Is.True);
            Assert.That(resident.IsHome, Is.True);
            services.WorldTime.AdvanceMinutes(330);
            Assert.That(services.Sleep.SleepForMinutes(480).IsSuccess, Is.True);
            Assert.That(services.Time.Current.Day, Is.EqualTo(2));
            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(420));
            Assert.That(resident.Position, Is.EqualTo(new Vector2(20, 0)));
            Assert.That(resident.IsHome, Is.False);
            Assert.That(resident.GetComponent<Collider2D>().enabled, Is.True);
        }

        [TestCase(359, "entry", true)]
        [TestCase(360, "outside", false)]
        [TestCase(479, "outside", false)]
        [TestCase(480, "work", false)]
        [TestCase(720, "rest", false)]
        [TestCase(780, "work", false)]
        [TestCase(1020, "work", false)]
        [TestCase(1079, "work", false)]
        [TestCase(1080, "entry", true)]
        public void SuccessfulLoad_RebuildsLegalStageAndRepeatedLoadIsStable(int minute, string location, bool home)
        {
            services.WorldTime.AdvanceMinutes((minute - 360 + 1440) % 1440);
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            services.Sleep.SleepForMinutes(60);
            for (int repeat = 0; repeat < 2; repeat++)
            {
                Assert.That(services.GameSave.Load().IsSuccess, Is.True);
                Assert.That(world.GetComponent<TownMap2D>().TryGetLocation(location, out var expected), Is.True);
                Assert.That(resident.Position, Is.EqualTo(expected));
                Assert.That(resident.IsHome, Is.EqualTo(home));
            }
        }

        [Test]
        public void FailedTimeAndLoad_KeepActualJourneyAndFractionalProgress()
        {
            services.DaytimeClock.AdvanceElapsed(2.25);
            Vector2 before = resident.Position;
            string target = resident.TargetLocationId;
            var farm = services.Farm.CaptureSnapshot();
            services.Farm.AdvanceDay(2);
            Assert.That(services.DaytimeClock.AdvanceElapsed(100).IsSuccess, Is.False);
            Assert.That(services.Sleep.SleepForMinutes(60).IsSuccess, Is.False);
            Assert.That(services.GameSave.Load().IsSuccess, Is.False);
            Assert.That(resident.Position, Is.EqualTo(before));
            Assert.That(resident.TargetLocationId, Is.EqualTo(target));
            services.Farm.Restore(farm);
            services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(resident.Position.x, Is.EqualTo(5).Within(0.001));
        }

        [Test]
        public void Binding_RejectsMissingFutureLocationBeforeTimeCanStart()
        {
            resident.Configure(world.GetComponent<TownMap2D>(),
                new NpcDailySchedule("npc.mina", "home.mina", "outside", "entry",
                    "work", "missing-rest", "work", 360, 480, 720, 780, 1020, 1080),
                resident.GetComponent<SpriteRenderer>());
            Assert.Throws<System.InvalidOperationException>(() =>
                world.GetComponent<CozyTownTownLifeController>().Bind(services.WorldTimeFlow));
            Assert.That(services.Time.Current.MinuteOfDay, Is.EqualTo(360));
        }

        [Test]
        public void RebindingSameClock_KeepsTheJourneyAtItsActualPosition()
        {
            services.DaytimeClock.AdvanceElapsed(2.5);
            Vector2 actual = resident.Position;
            world.GetComponent<CozyTownTownLifeController>().Bind(services.WorldTimeFlow);
            Assert.That(resident.Position, Is.EqualTo(actual));
            services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(resident.Position.x, Is.EqualTo(5.5).Within(0.001));
        }

        [Test]
        public void DisabledController_DoesNotLoseLoadReconstructionWhenTimeContinues()
        {
            services.DaytimeClock.AdvanceElapsed(2.5);
            services.GameSave.Save();
            var controller = world.GetComponent<CozyTownTownLifeController>();
            controller.enabled = false;
            services.WorldTime.AdvanceMinutes(20);
            services.GameSave.Load();
            services.WorldTime.AdvanceMinutes(2);
            controller.enabled = true;
            Assert.That(resident.Position, Is.EqualTo(Vector2.zero), "A successful load must invalidate the old journey even when the controller missed the notification.");
        }

        [Test]
        public void SleepAfterPartialMinute_ConsumesTheChosenDurationFromActualPosition()
        {
            var map = world.GetComponent<TownMap2D>();
            map.Configure(new[] { new TownHome("home.mina", "npc.mina", "outside", "entry") },
                new[] { new TownLocation("outside", Vector2.zero), new TownLocation("entry", new Vector2(0, 0.5f)),
                    new TownLocation("work", new Vector2(200, 0)), new TownLocation("rest", new Vector2(200, 10)) },
                new[] { new TownRoad("entry", "outside"), new TownRoad("outside", "work"), new TownRoad("work", "rest") });
            services = CozyTownCompositionRoot.CreateDefault();
            world.GetComponent<CozyTownTownLifeController>().Bind(services.WorldTimeFlow);
            services.DaytimeClock.AdvanceElapsed(0.25);
            Assert.That(resident.Position.x, Is.EqualTo(0.5f).Within(0.001));
            services.Sleep.SleepForMinutes(60);
            Assert.That(resident.Position.x, Is.EqualTo(60.5f).Within(0.001));
            Assert.That(services.WorldTimeFlow.Current.FractionalMinute, Is.Zero);
        }

        [Test]
        public void DailyNpcActivity_DoesNotChangeEconomicOrProductionResults()
        {
            var withoutResidents = CozyTownCompositionRoot.CreateDefault();
            foreach (var game in new[] { services, withoutResidents })
            {
                Assert.That(game.Inventory.Add(DefaultMvpIds.Items.PotatoSeed, 1).IsSuccess, Is.True);
                string plot = game.Farm.Plots.First().PlotId;
                Assert.That(game.Farm.Plant(plot, DefaultMvpIds.Items.PotatoSeed).IsSuccess, Is.True);
                Assert.That(game.Farm.Water(plot).IsSuccess, Is.True);
                Assert.That(game.WorldTime.AdvanceMinutes(1440).IsSuccess, Is.True);
            }
            var actual = services.EconomyState.CaptureSnapshot();
            var expected = withoutResidents.EconomyState.CaptureSnapshot();
            Assert.That(actual.Characters.Length, Is.EqualTo(expected.Characters.Length));
            foreach (var character in actual.Characters)
            {
                var match = expected.Characters.Single(item => item.CharacterId == character.CharacterId);
                Assert.That(character.Wallet.Balance, Is.EqualTo(match.Wallet.Balance));
                Assert.That(character.Backpack.Items, Is.EqualTo(match.Backpack.Items));
            }
            Assert.That(actual.Shops.Length, Is.EqualTo(expected.Shops.Length));
            foreach (var shop in actual.Shops)
            {
                var match = expected.Shops.Single(item => item.ShopId == shop.ShopId);
                Assert.That(shop.Wallet.Balance, Is.EqualTo(match.Wallet.Balance));
                Assert.That(shop.Stock.Items, Is.EqualTo(match.Stock.Items));
                Assert.That(shop.LastRestockedDay, Is.EqualTo(match.LastRestockedDay));
            }
            Assert.That(services.Farm.CaptureSnapshot().Plots, Is.EqualTo(withoutResidents.Farm.CaptureSnapshot().Plots));
            Assert.That(services.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            Assert.That(services.Livestock.CaptureSnapshot().Animals, Is.EqualTo(withoutResidents.Livestock.CaptureSnapshot().Animals));
            Assert.That(resident.IsHome, Is.False);
        }
    }
}
