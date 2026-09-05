using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.Save;
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
            Assert.That(services.NpcDialogueGameplay, Is.Not.Null);
            Assert.That(services.SaveStorage, Is.Not.Null);
            Assert.That(services.GameSave, Is.Not.Null);
            Assert.That(services.Time.Current.Day, Is.EqualTo(1));
            Assert.That(services.Inventory.CapacitySlots, Is.EqualTo(24));
            Assert.That(services.Wallet.Balance, Is.Zero);
        }

        [Test]
        public void CreateDefault_ShopTradingUsesServicesExposedBySameObjectGraph()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(
                services.EconomyState.TryGetCharacter(
                    DefaultMvpIds.Characters.Player,
                    out var characterBefore),
                Is.True);
            Assert.That(
                services.EconomyState.TryGetShop(
                    DefaultMvpIds.Shops.TownGeneral,
                    out var shopBefore),
                Is.True);
            int stockBefore = shopBefore.Stock.Items.Single(
                item => item.ItemId == DefaultMvpIds.Items.ChickenFeed).Quantity;

            var result = services.ShopTrading.Buy(
                DefaultMvpIds.Shops.TownGeneral,
                DefaultMvpIds.Characters.Player,
                DefaultMvpIds.Items.ChickenFeed,
                2);
            var state = services.ShopTrading.GetCurrentState(
                DefaultMvpIds.Shops.TownGeneral,
                DefaultMvpIds.Characters.Player);
            Assert.That(
                services.EconomyState.TryGetCharacter(
                    DefaultMvpIds.Characters.Player,
                    out var characterAfter),
                Is.True);
            Assert.That(
                services.EconomyState.TryGetShop(
                    DefaultMvpIds.Shops.TownGeneral,
                    out var shopAfter),
                Is.True);

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(state.IsSuccess, Is.True, state.ErrorCode);
            Assert.That(characterBefore.Wallet.Balance, Is.EqualTo(300));
            Assert.That(characterAfter.Wallet.Balance, Is.EqualTo(280));
            Assert.That(shopBefore.Wallet.Balance, Is.EqualTo(10000));
            Assert.That(shopAfter.Wallet.Balance, Is.EqualTo(10020));
            Assert.That(services.Wallet.Balance, Is.EqualTo(280));
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.ChickenFeed), Is.EqualTo(2));
            Assert.That(
                shopAfter.Stock.Items.Single(
                    item => item.ItemId == DefaultMvpIds.Items.ChickenFeed).Quantity,
                Is.EqualTo(stockBefore - 2));
            Assert.That(state.Value.CharacterBalance, Is.EqualTo(280));
            Assert.That(state.Value.ShopBalance, Is.EqualTo(10020));
            Assert.That(
                state.Value.PurchaseItems.Single(
                    item => item.ItemId == DefaultMvpIds.Items.ChickenFeed).Quantity,
                Is.EqualTo(stockBefore - 2));
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
            Assert.That(
                services.ShopTrading.GetCurrentState(
                    DefaultMvpIds.Shops.TownGeneral,
                    DefaultMvpIds.Characters.Player).Value.PurchaseItems,
                Is.Not.Empty);
            Assert.That(services.Cooking.Recipes.Count, Is.EqualTo(5));
        }

        [Test]
        public void CreateDefault_DayTransitionPublishesShopOnExposedEconomyState()
        {
            CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(
                services.EconomyState.TryGetShop(
                    DefaultMvpIds.Shops.TownGeneral,
                    out var before),
                Is.True);

            var result = services.DayTransition.SleepToNextDay();

            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(before.LastRestockedDay, Is.EqualTo(1));
            Assert.That(
                services.EconomyState.TryGetShop(
                    DefaultMvpIds.Shops.TownGeneral,
                    out var after),
                Is.True);
            Assert.That(after.LastRestockedDay, Is.EqualTo(2));
            Assert.That(after.Wallet.Balance, Is.EqualTo(10000));
            Assert.That(
                services.WorldSeed.Value,
                Is.EqualTo(DefaultMvpContent.DefaultWorldSeed));
        }

        [Test]
        public void Create_WithConfiguredShopBalance_SeedsShopWalletFromConfiguration()
        {
            CozyTownConfiguration source = DefaultMvpContent.CreateConfiguration();
            var configuration = new CozyTownConfiguration(
                source.Items,
                source.ShopOffers,
                source.Crops,
                source.FarmPlotIds,
                source.AnimalDefinitions,
                source.Animals,
                source.FishingEntries,
                source.Recipes,
                source.InventoryCapacitySlots,
                source.StartingBalance,
                source.StartingDay,
                source.StartingMinuteOfDay,
                source.FallbackDialogue,
                source.Npcs,
                source.ShopRestockRules,
                source.StartingWorldSeed,
                startingShopBalance: 4321);

            CozyTownServices services = CozyTownCompositionRoot.Create(configuration);

            Assert.That(
                services.EconomyState.TryGetShop(
                    DefaultMvpIds.Shops.TownGeneral,
                    out var shop),
                Is.True);
            Assert.That(shop.Wallet.Balance, Is.EqualTo(4321));
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
        public void Create_WithExternalDialogueAndStorage_UsesProvidedAdapters()
        {
            CozyTownConfiguration configuration = DefaultMvpContent.CreateConfiguration();
            var dialogue = new FixedFallbackDialogueGenerator("External fallback.");
            var storage = new InMemorySaveStorage();

            CozyTownServices services = CozyTownCompositionRoot.Create(
                configuration,
                dialogue,
                storage);

            Assert.That(services.NpcDialogue, Is.SameAs(dialogue));
            Assert.That(services.SaveStorage, Is.SameAs(storage));
            Assert.That(services.NpcDialogueGameplay, Is.Not.Null);
            Assert.That(services.GameSave, Is.Not.Null);
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
                source.Npcs,
                source.ShopRestockRules,
                source.StartingWorldSeed);

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => CozyTownCompositionRoot.Create(invalid));

            Assert.That(exception.Message, Does.Contain("content.animal_invalid"));
        }
    }
}
