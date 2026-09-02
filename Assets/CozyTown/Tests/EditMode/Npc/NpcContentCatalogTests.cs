using System;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Npc;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Npc
{
    public sealed class NpcContentCatalogTests
    {
        private const string DefaultFallback = "It is a quiet day in town.";

        [Test]
        public void Create_ValidDefinitions_ProjectsImmutableContentAndContext()
        {
            var definitions = new[]
            {
                new NpcDefinition(
                    "npc.farmer_eli",
                    "Eli",
                    "A patient farmer.",
                    "Keep the soil watered."),
                new NpcDefinition(
                    "npc.fisher_ren",
                    "Ren",
                    "A quiet fisher.",
                    "The pond is calm today.")
            };

            OperationResult<NpcContentCatalog> result = NpcContentCatalog.Create(
                DefaultFallback,
                definitions);
            definitions[0] = null;

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value.Definitions, Has.Count.EqualTo(2));
            Assert.That(
                result.Value.TryGetDefinition("npc.farmer_eli", out NpcDefinition eli),
                Is.True);
            Assert.That(eli.DisplayName, Is.EqualTo("Eli"));

            NpcDialogueContext context = result.Value.CreateDialogueContext(
                "npc.fisher_ren",
                day: 4,
                minuteOfDay: 9 * 60);

            Assert.That(context.NpcId, Is.EqualTo("npc.fisher_ren"));
            Assert.That(context.DisplayName, Is.EqualTo("Ren"));
            Assert.That(context.Persona, Is.EqualTo("A quiet fisher."));
            Assert.That(context.Day, Is.EqualTo(4));
            Assert.That(context.MinuteOfDay, Is.EqualTo(9 * 60));
            Assert.That(context.Affinity, Is.Zero);
            Assert.That(context.RecentActivities, Is.Empty);
            Assert.That(context.Memories, Is.Empty);
            Assert.That(
                result.Value.ResolveFallback("npc.fisher_ren"),
                Is.EqualTo("The pond is calm today."));
            Assert.That(
                result.Value.ResolveFallback("npc.unknown"),
                Is.EqualTo(DefaultFallback));
        }

        [TestCase(InvalidContent.EmptyGlobalFallback, "content.configuration_invalid")]
        [TestCase(InvalidContent.EmptyPersona, "content.npc_invalid")]
        [TestCase(InvalidContent.EmptyNpcFallback, "content.npc_invalid")]
        [TestCase(InvalidContent.DuplicateId, "content.npc_id_duplicate")]
        public void Create_InvalidContent_RejectsCatalog(
            InvalidContent invalidContent,
            string expectedError)
        {
            string defaultFallback = DefaultFallback;
            NpcDefinition[] definitions;
            switch (invalidContent)
            {
                case InvalidContent.EmptyGlobalFallback:
                    defaultFallback = " ";
                    definitions = ValidDefinitions();
                    break;
                case InvalidContent.EmptyPersona:
                    definitions = new[]
                    {
                        new NpcDefinition(
                            "npc.farmer_eli",
                            "Eli",
                            " ",
                            "Keep the soil watered.")
                    };
                    break;
                case InvalidContent.EmptyNpcFallback:
                    definitions = new[]
                    {
                        new NpcDefinition(
                            "npc.farmer_eli",
                            "Eli",
                            "A patient farmer.",
                            " ")
                    };
                    break;
                case InvalidContent.DuplicateId:
                    definitions = new[]
                    {
                        ValidDefinitions()[0],
                        new NpcDefinition(
                            "npc.farmer_eli",
                            "Second Eli",
                            "Another farmer.",
                            "Another fallback.")
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(invalidContent));
            }

            OperationResult<NpcContentCatalog> result = NpcContentCatalog.Create(
                defaultFallback,
                definitions);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
        }

        [Test]
        public void CreateDialogueContext_UnknownNpc_RejectsProjection()
        {
            NpcContentCatalog catalog = NpcContentCatalog.Create(
                DefaultFallback,
                ValidDefinitions()).Value;

            Assert.Throws<ArgumentException>(() =>
                catalog.CreateDialogueContext("npc.unknown", day: 1, minuteOfDay: 360));
        }

        private static NpcDefinition[] ValidDefinitions()
        {
            return new[]
            {
                new NpcDefinition(
                    "npc.farmer_eli",
                    "Eli",
                    "A patient farmer.",
                    "Keep the soil watered.")
            };
        }

        public enum InvalidContent
        {
            EmptyGlobalFallback,
            EmptyPersona,
            EmptyNpcFallback,
            DuplicateId
        }
    }
}
