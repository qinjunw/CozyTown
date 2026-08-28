using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Livestock;
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
            Assert.That(services.ShopTrading, Is.Not.Null);
            Assert.That(services.Farm, Is.Not.Null);
            Assert.That(services.FarmGameplay, Is.Not.Null);
            Assert.That(services.Livestock, Is.Not.Null);
            Assert.That(services.LivestockGameplay, Is.Not.Null);
            Assert.That(services.Fishing, Is.Not.Null);
            Assert.That(services.FishingGameplay, Is.Not.Null);
            Assert.That(services.Cooking, Is.Not.Null);
            Assert.That(services.CookingGameplay, Is.Not.Null);
            Assert.That(services.NpcDialogue, Is.Not.Null);
            Assert.That(services.SaveStorage, Is.Not.Null);
            Assert.That(services.Time.Current.Day, Is.EqualTo(1));
            Assert.That(services.Inventory.CapacitySlots, Is.EqualTo(24));
            Assert.That(services.Wallet.Balance, Is.Zero);
        }

        [Test]
        public void CreateDefault_ShopTradingUsesServicesExposedBySameObjectGraph()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();

            var result = services.ShopTrading.Buy(DefaultMvpIds.Items.PotatoSeed, 2);
            var state = services.ShopTrading.GetCurrentState();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(services.Wallet.Balance, Is.EqualTo(260));
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.PotatoSeed), Is.EqualTo(2));
            Assert.That(state.Balance, Is.EqualTo(260));
            Assert.That(
                state.Items.Single(item => item.ItemId == DefaultMvpIds.Items.PotatoSeed)
                    .OwnedQuantity,
                Is.EqualTo(2));
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

        [Test]
        public void Create_WhenAnimalIsFedAndProductReady_RejectsBeforeServiceConstruction()
        {
            CozyTownConfiguration source = DefaultMvpContent.CreateConfiguration();
            AnimalSnapshot original = source.Animals[0];
            var animals = new[]
            {
                new AnimalSnapshot(
                    original.AnimalId,
                    original.SpeciesId,
                    fedToday: true,
                    productReady: true)
            };
            var invalid = new CozyTownConfiguration(
                source.Items,
                source.ShopOffers,
                source.Crops,
                source.FarmPlotIds,
                source.AnimalDefinitions,
                animals,
                source.FishingEntries,
                source.Recipes,
                source.InventoryCapacitySlots,
                source.StartingBalance,
                source.StartingDay,
                source.StartingMinuteOfDay,
                source.FallbackDialogue,
                source.Npcs);

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => CozyTownCompositionRoot.Create(invalid));

            Assert.That(exception.Message, Does.Contain("content.animal_invalid"));
        }
    }
}
