using System;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class NpcDialogueCoordinatorTests
    {
        [Test]
        public async Task GenerateAsync_KnownNpc_BuildsReadOnlyContextFromCurrentClock()
        {
            var generator = new CapturingGenerator();
            var clock = new GameClockSnapshot(day: 4, minuteOfDay: 9 * 60);
            var coordinator = new NpcDialogueCoordinator(
                CreateCatalog(
                    new NpcDefinition(
                        "npc.farmer_eli",
                        "Eli",
                        "A patient farmer.",
                        "Keep the soil watered.")),
                generator,
                () => clock);

            NpcDialogueViewState state = await coordinator.GenerateAsync(
                "npc.farmer_eli",
                CancellationToken.None);

            Assert.That(generator.Context.NpcId, Is.EqualTo("npc.farmer_eli"));
            Assert.That(generator.Context.DisplayName, Is.EqualTo("Eli"));
            Assert.That(generator.Context.Persona, Is.EqualTo("A patient farmer."));
            Assert.That(generator.Context.Day, Is.EqualTo(4));
            Assert.That(generator.Context.MinuteOfDay, Is.EqualTo(9 * 60));
            Assert.That(state.NpcId, Is.EqualTo("npc.farmer_eli"));
            Assert.That(state.DisplayName, Is.EqualTo("Eli"));
            Assert.That(state.Text, Is.EqualTo("Good morning."));
            Assert.That(state.IsFallback, Is.False);
            Assert.That(coordinator.Npcs, Has.Count.EqualTo(1));
            Assert.That(coordinator.Npcs[0].NpcId, Is.EqualTo("npc.farmer_eli"));
        }

        [Test]
        public void GenerateAsync_UnknownNpc_RejectsBeforeCallingGenerator()
        {
            var generator = new CapturingGenerator();
            var coordinator = new NpcDialogueCoordinator(
                CreateCatalog(),
                generator,
                () => new GameClockSnapshot(1, 360));

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await coordinator.GenerateAsync("npc.unknown", CancellationToken.None));
            Assert.That(generator.Context, Is.Null);
        }

        [Test]
        public async Task GenerateAsync_NullGeneratorReply_ReturnsCurrentNpcFallback()
        {
            var coordinator = new NpcDialogueCoordinator(
                CreateCatalog(
                    new NpcDefinition(
                        "npc.farmer_eli",
                        "Eli",
                        "A patient farmer.",
                        "Keep the soil watered.")),
                new NullGenerator(),
                () => new GameClockSnapshot(1, 360));

            NpcDialogueViewState state = await coordinator.GenerateAsync(
                "npc.farmer_eli",
                CancellationToken.None);

            Assert.That(state.Text, Is.EqualTo("Keep the soil watered."));
            Assert.That(state.IsFallback, Is.True);
            Assert.That(
                state.FallbackReason,
                Is.EqualTo(NpcDialogueFallbackReason.EmptyResponse));
        }

        private static NpcContentCatalog CreateCatalog(params NpcDefinition[] definitions)
        {
            var result = NpcContentCatalog.Create(
                "It is a quiet day in town.",
                definitions);
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            return result.Value;
        }

        private sealed class CapturingGenerator : INpcDialogueGenerator
        {
            public NpcDialogueContext Context { get; private set; }

            public Task<NpcDialogueReply> GenerateAsync(
                NpcDialogueContext context,
                CancellationToken cancellationToken)
            {
                Context = context;
                return Task.FromResult(new NpcDialogueReply(
                    "Good morning.",
                    "happy",
                    "wave",
                    isFallback: false));
            }
        }

        private sealed class NullGenerator : INpcDialogueGenerator
        {
            public Task<NpcDialogueReply> GenerateAsync(
                NpcDialogueContext context,
                CancellationToken cancellationToken)
            {
                return Task.FromResult<NpcDialogueReply>(null);
            }
        }
    }
}
