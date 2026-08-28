using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Npc;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Core
{
    public sealed class CozyTownCompositionRootTests
    {
        [Test]
        public void CreateEmpty_ReturnsCompleteServiceGraph()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateEmpty();

            Assert.That(services.DayTransition, Is.Not.Null);
            Assert.That(services.Time, Is.Not.Null);
            Assert.That(services.Inventory, Is.Not.Null);
            Assert.That(services.Wallet, Is.Not.Null);
            Assert.That(services.Shop, Is.Not.Null);
            Assert.That(services.Farm, Is.Not.Null);
            Assert.That(services.Livestock, Is.Not.Null);
            Assert.That(services.Fishing, Is.Not.Null);
            Assert.That(services.Cooking, Is.Not.Null);
            Assert.That(services.NpcDialogue, Is.Not.Null);
            Assert.That(services.SaveStorage, Is.Not.Null);
            Assert.That(services.Time.Current.Day, Is.EqualTo(1));
            Assert.That(services.Inventory.CapacitySlots, Is.EqualTo(24));
            Assert.That(services.Wallet.Balance, Is.Zero);
        }

        [Test]
        public void CreateDefault_DayTransitionUsesServicesExposedBySameObjectGraph()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();

            var result = services.DayTransition.SleepToNextDay();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(services.Time.Current.Day, Is.EqualTo(2));
            Assert.That(services.Farm.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            Assert.That(services.Livestock.CaptureSnapshot().LastProcessedDay, Is.EqualTo(2));
            Assert.That(services.Shop.Offers, Is.Not.Empty);
            Assert.That(services.Cooking.Recipes.Count, Is.EqualTo(5));
        }

        [Test]
        public async Task CreateDefault_NpcDialogueUsesConfiguredNpcFallback()
        {
            CozyTownConfiguration configuration = DefaultMvpContent.CreateConfiguration();
            NpcDefinition npc = configuration.Npcs.Single(
                candidate => candidate.Id == DefaultMvpIds.Npcs.Shopkeeper);
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            var context = new NpcDialogueContext(
                npc.Id,
                npc.DisplayName,
                npc.Persona,
                day: 1,
                minuteOfDay: 6 * 60,
                affinity: 0,
                recentActivities: new string[0],
                memories: new string[0]);

            NpcDialogueReply reply = await services.NpcDialogue.GenerateAsync(
                context,
                CancellationToken.None);

            Assert.That(reply.IsFallback, Is.True);
            Assert.That(reply.Text, Is.EqualTo(npc.FallbackDialogue));
        }
    }
}
