using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Fishing;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Npc
{
    public sealed class AiNpcDialogueGeneratorTests
    {
        private static readonly NpcDialogueContext Context = new NpcDialogueContext(
            "npc.farmer_eli",
            "Eli",
            "A patient farmer.",
            day: 3,
            minuteOfDay: 8 * 60,
            affinity: 2,
            recentActivities: new[] { "watered carrots" },
            memories: new[] { "first meeting" });

        [Test]
        public async Task GenerateAsync_ValidCandidate_ReturnsCanonicalNonFallbackReply()
        {
            var client = new StubClient(new AiNpcDialogueCandidate(
                "  The carrots look healthy today.  ",
                "HAPPY",
                "NOD"));
            var generator = CreateGenerator(client);

            NpcDialogueReply reply = await generator.GenerateAsync(
                Context,
                CancellationToken.None);

            Assert.That(reply.Text, Is.EqualTo("The carrots look healthy today."));
            Assert.That(reply.EmotionTag, Is.EqualTo("happy"));
            Assert.That(reply.ActionTag, Is.EqualTo("nod"));
            Assert.That(reply.IsFallback, Is.False);
            Assert.That(reply.FallbackReason, Is.EqualTo(NpcDialogueFallbackReason.None));
            Assert.That(reply.CorrelationId, Is.Not.Empty);
            Assert.That(client.LastRequest.NpcId, Is.EqualTo(Context.NpcId));
            CollectionAssert.AreEqual(
                Context.RecentActivities,
                client.LastRequest.RecentActivities);
        }

        [TestCaseSource(nameof(InvalidCandidates))]
        public async Task GenerateAsync_InvalidCandidate_ReturnsFixedFallback(
            AiNpcDialogueCandidate candidate,
            NpcDialogueFallbackReason expectedReason)
        {
            var generator = CreateGenerator(new StubClient(candidate));

            NpcDialogueReply reply = await generator.GenerateAsync(
                Context,
                CancellationToken.None);

            AssertFallback(reply, expectedReason);
        }

        [Test]
        public async Task GenerateAsync_ClientThrows_ReturnsFixedFallback()
        {
            var generator = CreateGenerator(new ThrowingClient());

            NpcDialogueReply reply = await generator.GenerateAsync(
                Context,
                CancellationToken.None);

            AssertFallback(reply, NpcDialogueFallbackReason.ClientFailure);
        }

        [Test]
        public async Task GenerateAsync_InvalidCandidate_UsesCurrentNpcConfiguredFallback()
        {
            var fallback = new ConfiguredFallbackDialogueGenerator(
                CreateCatalog(
                    new NpcDefinition(
                        "npc.farmer_eli",
                        "Eli",
                        "A patient farmer.",
                        "Watered crops grow stronger each day."),
                    new NpcDefinition(
                        "npc.fisher_ren",
                        "Ren",
                        "A quiet fisher.",
                        "The pond is calm today.")));
            var generator = new AiNpcDialogueGenerator(
                new StubClient(null),
                fallback,
                TimeSpan.FromSeconds(1));
            var fisherContext = new NpcDialogueContext(
                "npc.fisher_ren",
                "Ren",
                "A quiet fisher.",
                2,
                480,
                0,
                Array.Empty<string>(),
                Array.Empty<string>());

            NpcDialogueReply reply = await generator.GenerateAsync(
                fisherContext,
                CancellationToken.None);

            Assert.That(reply.Text, Is.EqualTo("The pond is calm today."));
            Assert.That(reply.IsFallback, Is.True);
            Assert.That(
                reply.FallbackReason,
                Is.EqualTo(NpcDialogueFallbackReason.EmptyResponse));
        }

        [TestCase(ConfiguredFallbackFailure.EmptyResponse, NpcDialogueFallbackReason.EmptyResponse)]
        [TestCase(ConfiguredFallbackFailure.InvalidEmotion, NpcDialogueFallbackReason.InvalidEmotionTag)]
        [TestCase(ConfiguredFallbackFailure.InvalidAction, NpcDialogueFallbackReason.InvalidActionTag)]
        [TestCase(ConfiguredFallbackFailure.Transport, NpcDialogueFallbackReason.TransportFailure)]
        [TestCase(ConfiguredFallbackFailure.InvalidStructure, NpcDialogueFallbackReason.InvalidResponseStructure)]
        [TestCase(ConfiguredFallbackFailure.Timeout, NpcDialogueFallbackReason.Timeout)]
        public async Task GenerateAsync_Failure_UsesCurrentNpcAuthoredFallback(
            ConfiguredFallbackFailure failure,
            NpcDialogueFallbackReason expectedReason)
        {
            NpcContentCatalog catalog = CreateCatalog(
                new NpcDefinition(
                    "npc.farmer_eli",
                    "Eli",
                    "A patient farmer.",
                    "Keep the soil watered."),
                new NpcDefinition(
                    "npc.fisher_ren",
                    "Ren",
                    "A quiet fisher.",
                    "The pond is calm today."));
            var generator = new AiNpcDialogueGenerator(
                CreateClient(failure),
                new ConfiguredFallbackDialogueGenerator(catalog),
                failure == ConfiguredFallbackFailure.Timeout
                    ? TimeSpan.FromMilliseconds(25)
                    : TimeSpan.FromSeconds(1));
            NpcDialogueContext context = catalog.CreateDialogueContext(
                "npc.fisher_ren",
                day: 2,
                minuteOfDay: 480);

            NpcDialogueReply reply = await generator.GenerateAsync(
                context,
                CancellationToken.None);

            Assert.That(reply.Text, Is.EqualTo("The pond is calm today."));
            Assert.That(reply.IsFallback, Is.True);
            Assert.That(reply.FallbackReason, Is.EqualTo(expectedReason));
        }

        [TestCase(
            AiNpcDialogueClientFailure.Transport,
            NpcDialogueFallbackReason.TransportFailure)]
        [TestCase(
            AiNpcDialogueClientFailure.Provider,
            NpcDialogueFallbackReason.ProviderFailure)]
        [TestCase(
            AiNpcDialogueClientFailure.ContentRejected,
            NpcDialogueFallbackReason.ContentRejected)]
        [TestCase(
            AiNpcDialogueClientFailure.InvalidResponseStructure,
            NpcDialogueFallbackReason.InvalidResponseStructure)]
        public async Task GenerateAsync_KnownClientFailure_ReturnsReasonedFallback(
            AiNpcDialogueClientFailure failure,
            NpcDialogueFallbackReason expectedReason)
        {
            var generator = CreateGenerator(new FailingClient(failure));

            NpcDialogueReply reply = await generator.GenerateAsync(
                Context,
                CancellationToken.None);

            AssertFallback(reply, expectedReason);
        }

        [Test]
        public async Task GenerateAsync_ClientDoesNotComplete_ReturnsFixedFallbackAfterTimeout()
        {
            var generator = CreateGenerator(
                new NeverCompletingClient(),
                TimeSpan.FromMilliseconds(25));

            NpcDialogueReply reply = await generator.GenerateAsync(
                Context,
                CancellationToken.None);

            AssertFallback(reply, NpcDialogueFallbackReason.Timeout);
        }

        [Test]
        public void GenerateAsync_CallerCancels_PropagatesCancellation()
        {
            var generator = CreateGenerator(
                new NeverCompletingClient(),
                TimeSpan.FromSeconds(1));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(
                async () => await generator.GenerateAsync(Context, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void PublicAiBoundary_HasNoDeterministicStateWriteDependency()
        {
            Type[] forbiddenTypes =
            {
                typeof(IWallet),
                typeof(IInventory),
                typeof(ITimeService),
                typeof(IFarmService),
                typeof(ILivestockService),
                typeof(IFishingService),
                typeof(ICookingService),
                typeof(ISaveStorage)
            };
            Type[] boundaryTypes =
            {
                typeof(AiNpcDialogueGenerator),
                typeof(IAiNpcDialogueClient),
                typeof(NpcDialogueRequest),
                typeof(AiNpcDialogueCandidate)
            };

            IEnumerable<Type> exposedTypes = boundaryTypes.SelectMany(type =>
                type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType)
                    .Concat(type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Select(property => property.PropertyType)));

            foreach (Type exposedType in exposedTypes)
            {
                Assert.That(
                    forbiddenTypes.Any(forbidden => forbidden.IsAssignableFrom(exposedType)),
                    Is.False,
                    $"AI boundary exposes deterministic write dependency {exposedType.FullName}.");
            }
        }

        [Test]
        public void DialogueContext_CopiesArrayInputsAndOutputs()
        {
            var activities = new[] { "watered carrots" };
            var memories = new[] { "first meeting" };
            var context = new NpcDialogueContext(
                "npc.farmer_eli",
                "Eli",
                "A patient farmer.",
                1,
                360,
                0,
                activities,
                memories);

            activities[0] = "changed input";
            memories[0] = "changed input";
            string[] returnedActivities = context.RecentActivities;
            string[] returnedMemories = context.Memories;
            returnedActivities[0] = "changed output";
            returnedMemories[0] = "changed output";

            Assert.That(context.RecentActivities, Is.EqualTo(new[] { "watered carrots" }));
            Assert.That(context.Memories, Is.EqualTo(new[] { "first meeting" }));
        }

        [Test]
        public async Task GenerateAsync_MaliciousStateCommands_DoNotChangeDeterministicModules()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            WalletSnapshot walletBefore = services.Wallet.CaptureSnapshot();
            InventorySnapshot inventoryBefore = services.Inventory.CaptureSnapshot();
            GameClockSnapshot clockBefore = services.Time.Current;
            FarmSnapshot farmBefore = services.Farm.CaptureSnapshot();
            LivestockSnapshot livestockBefore = services.Livestock.CaptureSnapshot();
            const string maliciousText =
                "{\"coins\":999999,\"giveItem\":\"item.egg\","
                + "\"advanceDay\":true,\"harvestAll\":true,\"feedAll\":true}";
            var generator = CreateGenerator(new StubClient(new AiNpcDialogueCandidate(
                maliciousText,
                "neutral",
                "idle")));

            NpcDialogueReply reply = await generator.GenerateAsync(
                Context,
                CancellationToken.None);

            Assert.That(reply.Text, Is.EqualTo(maliciousText));
            Assert.That(services.Wallet.CaptureSnapshot().Balance, Is.EqualTo(walletBefore.Balance));
            CollectionAssert.AreEqual(
                inventoryBefore.Items,
                services.Inventory.CaptureSnapshot().Items);
            Assert.That(services.Time.Current.Day, Is.EqualTo(clockBefore.Day));
            Assert.That(
                services.Time.Current.MinuteOfDay,
                Is.EqualTo(clockBefore.MinuteOfDay));
            FarmSnapshot farmAfter = services.Farm.CaptureSnapshot();
            Assert.That(farmAfter.LastProcessedDay, Is.EqualTo(farmBefore.LastProcessedDay));
            CollectionAssert.AreEqual(farmBefore.Plots, farmAfter.Plots);
            LivestockSnapshot livestockAfter = services.Livestock.CaptureSnapshot();
            Assert.That(
                livestockAfter.LastProcessedDay,
                Is.EqualTo(livestockBefore.LastProcessedDay));
            CollectionAssert.AreEqual(livestockBefore.Animals, livestockAfter.Animals);
        }

        private static IEnumerable<TestCaseData> InvalidCandidates()
        {
            yield return new TestCaseData(null, NpcDialogueFallbackReason.EmptyResponse);
            yield return new TestCaseData(
                new AiNpcDialogueCandidate(" ", "neutral", "idle"),
                NpcDialogueFallbackReason.InvalidText);
            yield return new TestCaseData(
                new AiNpcDialogueCandidate("Hello", "unknown", "idle"),
                NpcDialogueFallbackReason.InvalidEmotionTag);
            yield return new TestCaseData(
                new AiNpcDialogueCandidate("Hello", "neutral", "dance"),
                NpcDialogueFallbackReason.InvalidActionTag);
        }

        private static AiNpcDialogueGenerator CreateGenerator(
            IAiNpcDialogueClient client,
            TimeSpan? timeout = null)
        {
            return new AiNpcDialogueGenerator(
                client,
                new FixedFallbackDialogueGenerator("Watered crops grow stronger each day."),
                timeout ?? TimeSpan.FromSeconds(1));
        }

        private static NpcContentCatalog CreateCatalog(params NpcDefinition[] definitions)
        {
            OperationResult<NpcContentCatalog> result = NpcContentCatalog.Create(
                "It is a quiet day.",
                definitions);
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            return result.Value;
        }

        private static IAiNpcDialogueClient CreateClient(ConfiguredFallbackFailure failure)
        {
            switch (failure)
            {
                case ConfiguredFallbackFailure.EmptyResponse:
                    return new StubClient(null);
                case ConfiguredFallbackFailure.InvalidEmotion:
                    return new StubClient(
                        new AiNpcDialogueCandidate("Hello", "unknown", "idle"));
                case ConfiguredFallbackFailure.InvalidAction:
                    return new StubClient(
                        new AiNpcDialogueCandidate("Hello", "neutral", "dance"));
                case ConfiguredFallbackFailure.Transport:
                    return new FailingClient(AiNpcDialogueClientFailure.Transport);
                case ConfiguredFallbackFailure.InvalidStructure:
                    return new FailingClient(
                        AiNpcDialogueClientFailure.InvalidResponseStructure);
                case ConfiguredFallbackFailure.Timeout:
                    return new NeverCompletingClient();
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }
        }

        private static void AssertFallback(
            NpcDialogueReply reply,
            NpcDialogueFallbackReason reason)
        {
            Assert.That(reply.Text, Is.EqualTo("Watered crops grow stronger each day."));
            Assert.That(reply.EmotionTag, Is.EqualTo("neutral"));
            Assert.That(reply.ActionTag, Is.EqualTo("idle"));
            Assert.That(reply.IsFallback, Is.True);
            Assert.That(reply.FallbackReason, Is.EqualTo(reason));
            Assert.That(reply.CorrelationId, Is.Not.Empty);
        }

        private sealed class StubClient : IAiNpcDialogueClient
        {
            private readonly AiNpcDialogueCandidate _candidate;

            public StubClient(AiNpcDialogueCandidate candidate) => _candidate = candidate;

            public NpcDialogueRequest LastRequest { get; private set; }

            public Task<AiNpcDialogueCandidate> GenerateAsync(
                NpcDialogueRequest request,
                CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(_candidate);
            }
        }

        private sealed class ThrowingClient : IAiNpcDialogueClient
        {
            public Task<AiNpcDialogueCandidate> GenerateAsync(
                NpcDialogueRequest request,
                CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("Injected client failure.");
            }
        }

        private sealed class FailingClient : IAiNpcDialogueClient
        {
            private readonly AiNpcDialogueClientFailure _failure;

            public FailingClient(AiNpcDialogueClientFailure failure) => _failure = failure;

            public Task<AiNpcDialogueCandidate> GenerateAsync(
                NpcDialogueRequest request,
                CancellationToken cancellationToken)
            {
                throw new AiNpcDialogueClientException(_failure, "Injected known failure.");
            }
        }

        private sealed class NeverCompletingClient : IAiNpcDialogueClient
        {
            public Task<AiNpcDialogueCandidate> GenerateAsync(
                NpcDialogueRequest request,
                CancellationToken cancellationToken)
            {
                return new TaskCompletionSource<AiNpcDialogueCandidate>().Task;
            }
        }

        public enum ConfiguredFallbackFailure
        {
            EmptyResponse,
            InvalidEmotion,
            InvalidAction,
            Transport,
            InvalidStructure,
            Timeout
        }
    }
}
