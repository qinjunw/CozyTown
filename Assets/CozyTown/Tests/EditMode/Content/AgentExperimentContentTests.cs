using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Content
{
    public sealed class AgentExperimentContentTests
    {
        [TestCase(NpcSpeechMode.FreeText)]
        [TestCase(NpcSpeechMode.StructuredFacts)]
        public void FourResidents_SharingEightRequestsPerMinute_WaitExpireAndResumeWithoutLeakingActivities(NpcSpeechMode mode)
        {
            var configuration = AgentExperimentContent.CreateConfiguration();
            var services = CozyTownCompositionRoot.Create(configuration);
            var schedules = ExperimentSchedules();
            var world = new NpcAgentWorld(schedules, (npc, location) => true);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, true, 1));
            var board = new NpcMeetingBoard(world, AgentExperimentContent.CreateMeetingPlans(),
                (npc, location) => NpcMeetingPresence.Arrived, services.ResourceTrading);
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("main-road", "Main road", -10, -10, 10, 10) },
                Array.Empty<NpcObservationEntity>(), radius: 10);
            var bodies = configuration.Npcs.Select((profile, index) => new NpcObservationBody(profile.Id, index, 0)).ToArray();
            var client = new HeldFixedClient();
            using var scheduler = new NpcDecisionScheduler(world, configuration.Npcs, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 8, maxConcurrentRequests: 2), board,
                request => scene.Read(request.NpcId, request.Self.WorldRunId, world.TotalMinutes, bodies, request.Social,
                    services.ResourceTrading.Inspect(request.Social?.Resources?.Terms, request.NpcId), board.GetMemories(request.NpcId)), mode);
            double realSeconds = 0;
            bool observedBudgetWait = false;
            void Tick()
            {
                client.Now = realSeconds += 0.1;
                scheduler.Tick(client.Now);
                Assert.That(client.PendingCount, Is.LessThanOrEqualTo(2), "Physical client tasks share the same concurrency limit.");
                Assert.That(scheduler.ActiveRequestCount, Is.EqualTo(client.PendingCount));
                observedBudgetWait |= scheduler.RequestsInLastMinute == 8 && scheduler.WaitingResidentCount > 0;
                client.ReleaseAll();
            }
            foreach (int minute in new[] { 720, 735, 780 })
            {
                world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, 1));
                for (int i = 0; i < 15; i++) Tick();
            }

            CollectionAssert.AreEquivalent(configuration.Npcs.Select(item => item.Id), client.Requests.Select(item => item.NpcId).Distinct());
            Assert.That(client.StartTimes.Count, Is.EqualTo(8), "Both meetings compete for the shared request window.");
            Assert.That(observedBudgetWait, Is.True);
            Assert.That(client.MaximumPending, Is.EqualTo(2));
            var firstMeeting = board.GetLatest(DefaultMvpIds.Npcs.Shopkeeper).Id;
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 1000), 0, false, 1));
            for (int i = 0; i < 3; i++) Tick();
            Assert.That(configuration.Npcs.Any(profile => board.GetLatest(profile.Id)?.State == NpcMeetingState.Expired), Is.True);
            AssertReleasedToSchedule();

            realSeconds = 65;
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(2, 720), 0, false, 1));
            for (int i = 0; i < 15; i++) Tick();
            Assert.That(client.StartTimes.Count, Is.GreaterThan(8), "Expired conversations must not prevent later dispatch after the window refills.");
            Assert.That(board.GetLatest(DefaultMvpIds.Npcs.Shopkeeper).Id, Is.Not.EqualTo(firstMeeting));
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(2, 1000), 0, false, 1));
            for (int i = 0; i < 3; i++) Tick();
            AssertReleasedToSchedule();
            Assert.That(scheduler.WaitingResidentCount, Is.Zero);
            Assert.That(scheduler.ActiveRequestCount, Is.Zero);
            Assert.That(scheduler.RequestsStarted, Is.EqualTo(client.StartTimes.Count));
            foreach (double started in client.StartTimes)
                Assert.That(client.StartTimes.Count(time => time > started - 60 && time <= started), Is.LessThanOrEqualTo(8));

            void AssertReleasedToSchedule()
            {
                foreach (var schedule in schedules)
                {
                    var state = world.GetState(schedule.NpcId);
                    var target = schedule.Query(1000);
                    Assert.That(board.GetCurrent(schedule.NpcId), Is.Null, schedule.NpcId);
                    Assert.That(state.ActiveActivity, Is.Null, schedule.NpcId);
                    Assert.That(state.Target.TargetLocationId, Is.EqualTo(target.TargetLocationId), schedule.NpcId);
                    Assert.That(state.Target.ExpectedActivity, Is.EqualTo(target.ExpectedActivity), schedule.NpcId);
                }
            }
        }

        [TestCase(NpcSpeechMode.FreeText)]
        [TestCase(NpcSpeechMode.StructuredFacts)]
        public void FixedClient_WhenBuyerLosesFundsBeforeDelivery_CancelsWithoutTransferringAssets(NpcSpeechMode mode)
        {
            var configuration = AgentExperimentContent.CreateConfiguration();
            var services = CozyTownCompositionRoot.Create(configuration);
            var world = new NpcAgentWorld(ExperimentSchedules(), (npc, location) => true);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 735), 0, true, 1));
            var plan = DefaultNpcResourcePlans.Create().Single();
            var board = new NpcMeetingBoard(world, new[] { plan },
                (npc, location) => NpcMeetingPresence.Arrived, services.ResourceTrading);
            var invited = board.Invite(world.GetState(plan.InitiatorId), plan.Id);
            Assert.That(invited.IsSuccess, Is.True, invited.ErrorCode);
            Assert.That(board.Respond(world.GetState(plan.PartnerId), invited.Value.Id, true).IsSuccess, Is.True);
            Assert.That(services.EconomyState.TryGetCharacter(plan.InitiatorId, out var buyer), Is.True);
            Assert.That(services.EconomyState.CommitCharacter(new CharacterEconomySnapshot(buyer.CharacterId,
                buyer.Backpack, new WalletSnapshot(10))).IsSuccess, Is.True);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 780), 0, false, 1));
            var client = new RecordingFixedClient();
            using var scheduler = new NpcDecisionScheduler(world, configuration.Npcs, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 16, maxConcurrentRequests: 2), board, speechMode: mode);

            scheduler.Tick(0);
            scheduler.Tick(0.1);

            Assert.That(board.GetLatest(plan.InitiatorId).State, Is.EqualTo(NpcMeetingState.Cancelled));
            Assert.That(client.Replies.Single().Kind, Is.EqualTo(NpcDecisionKind.CancelExchange));
            Assert.That(client.Requests.Single().AllowedOperations, Is.EqualTo(new[] { "cancel_exchange" }));
            Assert.That(board.GetLatest(plan.InitiatorId).DeliveryResultCode, Is.Not.EqualTo("resource.delivered"));
            Assert.That(world.GetState(plan.InitiatorId).ActiveActivity, Is.Null);
            Assert.That(world.GetState(plan.PartnerId).ActiveActivity, Is.Null);
            var characters = services.EconomyState.CaptureSnapshot().Characters;
            Assert.That(characters.Single(item => item.CharacterId == plan.InitiatorId).Wallet.Balance, Is.EqualTo(10));
            Assert.That(characters.Single(item => item.CharacterId == plan.PartnerId).Wallet.Balance, Is.Zero);
            Assert.That(characters.Single(item => item.CharacterId == plan.InitiatorId).Backpack.Items
                .Where(item => item.ItemId == DefaultMvpIds.Items.Carp).Sum(item => item.Quantity), Is.Zero);
            Assert.That(characters.Single(item => item.CharacterId == plan.PartnerId).Backpack.Items
                .Where(item => item.ItemId == DefaultMvpIds.Items.Carp).Sum(item => item.Quantity), Is.EqualTo(2));
        }

        [TestCase("available", NpcSpeechMode.FreeText, "Completed", 1, 1, 25, 25)]
        [TestCase("available", NpcSpeechMode.StructuredFacts, "Completed", 1, 1, 25, 25)]
        [TestCase("seller_empty", NpcSpeechMode.FreeText, "Declined", 0, 0, 0, 50)]
        [TestCase("seller_empty", NpcSpeechMode.StructuredFacts, "Declined", 0, 0, 0, 50)]
        [TestCase("buyer_poor", NpcSpeechMode.FreeText, "None", 2, 0, 0, 10)]
        [TestCase("buyer_poor", NpcSpeechMode.StructuredFacts, "None", 2, 0, 0, 10)]
        [TestCase("need_satisfied", NpcSpeechMode.FreeText, "None", 2, 1, 0, 50)]
        [TestCase("need_satisfied", NpcSpeechMode.StructuredFacts, "None", 2, 1, 0, 50)]
        public void FixedClient_DrivesAllFourResidentsThroughSocialAndResourceMeetings(string scenarioId,
            NpcSpeechMode mode, string resourceState, int sellerFish, int buyerFish, int sellerCoins, int buyerCoins)
        {
            var configuration = AgentExperimentContent.CreateConfiguration(scenarioId);
            var services = CozyTownCompositionRoot.Create(configuration);
            var world = new NpcAgentWorld(ExperimentSchedules(), (npc, location) => true);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, true, 1));
            var board = new NpcMeetingBoard(world, AgentExperimentContent.CreateMeetingPlans(),
                (npc, location) => NpcMeetingPresence.Arrived, services.ResourceTrading);
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("main-road", "Main road", -10, -10, 10, 10) },
                Array.Empty<NpcObservationEntity>(), radius: 10);
            var bodies = configuration.Npcs.Select((profile, index) => new NpcObservationBody(profile.Id, index, 0)).ToArray();
            var client = new RecordingFixedClient();
            using var scheduler = new NpcDecisionScheduler(world, configuration.Npcs, client,
                new NpcDecisionSettings(maxRequestsPerMinute: 16, maxConcurrentRequests: 2), board,
                request => scene.Read(request.NpcId, request.Self.WorldRunId, world.TotalMinutes, bodies, request.Social,
                    services.ResourceTrading.Inspect(request.Social?.Resources?.Terms, request.NpcId), board.GetMemories(request.NpcId)), mode);
            double realSeconds = 0;
            foreach (int minute in new[] { 720, 735, 780 })
            {
                world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, 1));
                for (int i = 0; i < 15; i++) scheduler.Tick(realSeconds += 0.1);
            }

            CollectionAssert.AreEquivalent(configuration.Npcs.Select(item => item.Id), client.Requests.Select(item => item.NpcId).Distinct());
            foreach (var profile in configuration.Npcs)
            {
                bool socialOnly = profile.Id == DefaultMvpIds.Npcs.Shopkeeper || profile.Id == DefaultMvpIds.Npcs.Farmer;
                Assert.That(board.GetLatest(profile.Id)?.State.ToString() ?? "None",
                    Is.EqualTo(socialOnly ? "Completed" : resourceState), profile.Id);
                Assert.That(board.GetLatest(profile.Id)?.Transcript.Count ?? 0,
                    Is.EqualTo(socialOnly || resourceState == "Completed" ? 2 : 0), profile.Id);
                Assert.That(world.GetState(profile.Id).ActiveActivity, Is.Null, profile.Id);
            }
            Assert.That(board.GetLatest(DefaultMvpIds.Npcs.Cook)?.DeliveryResultCode,
                Is.EqualTo(resourceState == "Completed" ? "resource.delivered" : null));
            var characters = services.EconomyState.CaptureSnapshot().Characters;
            foreach (string npc in new[] { DefaultMvpIds.Npcs.Fisher, DefaultMvpIds.Npcs.Cook })
            {
                bool seller = npc == DefaultMvpIds.Npcs.Fisher;
                var character = characters.Single(item => item.CharacterId == npc);
                Assert.That(character.Backpack.Items.Where(item => item.ItemId == DefaultMvpIds.Items.Carp).Sum(item => item.Quantity),
                    Is.EqualTo(seller ? sellerFish : buyerFish));
                Assert.That(character.Wallet.Balance, Is.EqualTo(seller ? sellerCoins : buyerCoins));
            }
            Assert.That(client.Requests.Count, Is.LessThanOrEqualTo(16));
            Assert.That(scheduler.RequestsStarted, Is.EqualTo(client.Requests.Count));
            Assert.That(client.Replies.Count(item => item.Kind == NpcDecisionKind.Speak), Is.EqualTo(resourceState == "Completed" ? 4 : 2));
            foreach (var reply in client.Replies.Where(item => item.Kind == NpcDecisionKind.Speak))
            {
                Assert.That(reply.Text == null, Is.EqualTo(mode == NpcSpeechMode.StructuredFacts));
                Assert.That(reply.SpeechFrame == null, Is.EqualTo(mode == NpcSpeechMode.FreeText));
            }
        }

        private static NpcDailySchedule[] ExperimentSchedules() => new[] {
            Schedule(DefaultMvpIds.Npcs.Shopkeeper, "shopkeeper_mina", 360, 480, 720, 780, 1020, 1080),
            Schedule(DefaultMvpIds.Npcs.Farmer, "farmer_eli", 330, 450, 690, 750, 990, 1050),
            Schedule(DefaultMvpIds.Npcs.Fisher, "fisher_ren", 345, 465, 735, 795, 1050, 1110),
            Schedule(DefaultMvpIds.Npcs.Cook, "cook_sora", 390, 510, 780, 840, 1080, 1140) };

        private static NpcDailySchedule Schedule(string npc, string suffix, params int[] times)
            => new NpcDailySchedule(npc, "home." + suffix, "home." + suffix + ".doorstep", "home." + suffix + ".entry",
                "work." + suffix, "rest." + suffix, "work." + suffix, times[0], times[1], times[2], times[3], times[4], times[5]);

        private sealed class RecordingFixedClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            private readonly FixedExperimentDecisionClient _inner = new FixedExperimentDecisionClient();
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal readonly List<NpcDecisionReply> Replies = new List<NpcDecisionReply>();
            public string SnapshotConfiguration => _inner.SnapshotConfiguration;
            public async Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                var reply = await _inner.DecideAsync(request, cancellationToken);
                Assert.That(request.AllowedOperations, Does.Contain(reply.Operation));
                if (reply.SpeechFrame != null)
                {
                    Assert.That(request.Observation.Facts.Any(item => item.FactId == reply.SpeechFrame.FactId && item.CanExpress), Is.True);
                    Assert.That(NpcFactSpeech.TryRender(request.Observation, reply.SpeechFrame, out _, out var error), Is.True, error);
                }
                Replies.Add(reply);
                return reply;
            }
        }

        private sealed class HeldFixedClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            private readonly FixedExperimentDecisionClient _inner = new FixedExperimentDecisionClient();
            private readonly Queue<(TaskCompletionSource<NpcDecisionReply> source, NpcDecisionReply reply)> _pending
                = new Queue<(TaskCompletionSource<NpcDecisionReply>, NpcDecisionReply)>();
            internal readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            internal readonly List<double> StartTimes = new List<double>();
            internal double Now;
            internal int PendingCount => _pending.Count;
            internal int MaximumPending { get; private set; }
            public string SnapshotConfiguration => _inner.SnapshotConfiguration;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                var reply = _inner.DecideAsync(request, cancellationToken).GetAwaiter().GetResult();
                Assert.That(request.AllowedOperations, Does.Contain(reply.Operation));
                var source = new TaskCompletionSource<NpcDecisionReply>();
                Requests.Add(request);
                StartTimes.Add(Now);
                _pending.Enqueue((source, reply));
                MaximumPending = Math.Max(MaximumPending, _pending.Count);
                return source.Task;
            }
            internal void ReleaseAll()
            {
                while (_pending.Count > 0)
                {
                    var pending = _pending.Dequeue();
                    pending.source.SetResult(pending.reply);
                }
            }
        }

        [TestCase("seller_empty", 0, 0, 50)]
        [TestCase("buyer_poor", 2, 0, 10)]
        [TestCase("need_satisfied", 2, 1, 50)]
        public void ScenarioConfiguration_ChangesOnlyTheDeclaredFishAndBuyerFunds(
            string scenarioId, int sellerFish, int buyerFish, int buyerCoins)
        {
            var configuration = AgentExperimentContent.CreateConfiguration(scenarioId);
            var ordinary = AgentExperimentContent.CreateConfiguration();
            var ren = configuration.InitialNpcEconomy.Single(item => item.CharacterId == DefaultMvpIds.Npcs.Fisher);
            var sora = configuration.InitialNpcEconomy.Single(item => item.CharacterId == DefaultMvpIds.Npcs.Cook);

            Assert.That(ren.Backpack.Items.Where(item => item.ItemId == DefaultMvpIds.Items.Carp).Sum(item => item.Quantity), Is.EqualTo(sellerFish));
            Assert.That(sora.Backpack.Items.Where(item => item.ItemId == DefaultMvpIds.Items.Carp).Sum(item => item.Quantity), Is.EqualTo(buyerFish));
            Assert.That(sora.Wallet.Balance, Is.EqualTo(buyerCoins));
            Assert.That(ren.Wallet.Balance, Is.Zero);
            Assert.That(sora.Backpack.Items.Single(item => item.ItemId == DefaultMvpIds.Items.Salt).Quantity, Is.EqualTo(1));
            foreach (string npc in new[] { DefaultMvpIds.Npcs.Shopkeeper, DefaultMvpIds.Npcs.Farmer })
            {
                var character = configuration.InitialNpcEconomy.Single(item => item.CharacterId == npc);
                Assert.That(character.Backpack.Items, Is.Empty);
                Assert.That(character.Wallet.Balance, Is.Zero);
            }
            Assert.That(configuration.StartingMinuteOfDay, Is.EqualTo(720));
            Assert.That(configuration.StartingWorldSeed, Is.EqualTo(ordinary.StartingWorldSeed));
            Assert.That(configuration.Npcs.Select(item => item.Id), Is.EqualTo(ordinary.Npcs.Select(item => item.Id)));
            Assert.That(ordinary.InitialNpcEconomy.Single(item => item.CharacterId == DefaultMvpIds.Npcs.Fisher)
                .Backpack.Items.Single().Quantity, Is.EqualTo(2), "A scenario must not alter subsequent fresh worlds.");
            Assert.That(ordinary.InitialNpcEconomy.Single(item => item.CharacterId == DefaultMvpIds.Npcs.Cook)
                .Wallet.Balance, Is.EqualTo(50));
            var validation = MvpContentValidator.Validate(configuration);
            Assert.That(validation.IsSuccess, Is.True, validation.ErrorCode);
        }

        [Test]
        public void AvailableConfiguration_RegistersFourResidentsAndTwoIndependentMeetings()
        {
            var configuration = AgentExperimentContent.CreateConfiguration();
            var plans = AgentExperimentContent.CreateMeetingPlans();

            var validation = MvpContentValidator.Validate(configuration);
            Assert.That(validation.IsSuccess, Is.True, validation.ErrorCode);
            CollectionAssert.AreEqual(new[] { DefaultMvpIds.Npcs.Shopkeeper, DefaultMvpIds.Npcs.Farmer,
                DefaultMvpIds.Npcs.Fisher, DefaultMvpIds.Npcs.Cook }, configuration.Npcs.Select(item => item.Id));
            Assert.That(configuration.StartingWorldSeed, Is.EqualTo(12345));
            Assert.That(configuration.StartingDay, Is.EqualTo(1));
            Assert.That(configuration.StartingMinuteOfDay, Is.EqualTo(720));
            Assert.That(plans.Length, Is.EqualTo(2));
            CollectionAssert.AreEquivalent(configuration.Npcs.Select(item => item.Id),
                plans.SelectMany(item => new[] { item.InitiatorId, item.PartnerId }));
            Assert.That(plans.Select(item => item.PlaceId).Distinct().Count(), Is.EqualTo(2));
            var social = plans.Single(item => item.ResourceTerms == null);
            Assert.That(social.InitiatorId, Is.EqualTo(DefaultMvpIds.Npcs.Shopkeeper));
            Assert.That(social.PartnerId, Is.EqualTo(DefaultMvpIds.Npcs.Farmer));
            Assert.That(social.InitiatorLocationId, Is.EqualTo("road.coop"));
            Assert.That(social.PartnerLocationId, Is.EqualTo("road.east_lane"));
            Assert.That(social.InviteStartMinute, Is.EqualTo(720));
            Assert.That(social.MeetingStartMinute, Is.EqualTo(740));
            Assert.That(social.InviteEndMinute, Is.EqualTo(745));
            Assert.That(social.DurationGameMinutes, Is.EqualTo(60));
            var resource = plans.Single(item => item.ResourceTerms != null);
            Assert.That(resource.Id, Is.EqualTo("sora-ren-fish-supply"));
            Assert.That(resource.ResourceTerms.ItemId, Is.EqualTo(DefaultMvpIds.Items.Carp));
            Assert.That(resource.ResourceTerms.Quantity, Is.EqualTo(1));
            Assert.That(resource.ResourceTerms.TotalPrice, Is.EqualTo(25));
            var ren = configuration.InitialNpcEconomy.Single(item => item.CharacterId == DefaultMvpIds.Npcs.Fisher);
            var sora = configuration.InitialNpcEconomy.Single(item => item.CharacterId == DefaultMvpIds.Npcs.Cook);
            Assert.That(ren.Backpack.Items.Single().Quantity, Is.EqualTo(2));
            Assert.That(ren.Wallet.Balance, Is.Zero);
            Assert.That(sora.Backpack.Items.Single().ItemId, Is.EqualTo(DefaultMvpIds.Items.Salt));
            Assert.That(sora.Wallet.Balance, Is.EqualTo(50));
        }
    }
}
