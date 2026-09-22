using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Cooking;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Save;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class ResidentEconomySaveTests
    {
        [Test]
        public void PartiallyMissingResidents_AreRejectedWithoutResettingAnyAssets()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            services.GameSave.Save();
            var saved = services.SaveStorage.Load("main").Value;
            services.SaveStorage.Save("main", new GameSaveSnapshot(saved.SchemaVersion, saved.WorldSeed, saved.Clock,
                saved.Characters.Where(item => item.CharacterId != DefaultMvpIds.Npcs.Fisher).ToArray(), saved.Shops, saved.Farm, saved.Livestock));
            services.Wallet.Debit(10);
            Assert.That(services.GameSave.Load().IsSuccess, Is.False);
            Assert.That(services.Wallet.Balance, Is.EqualTo(290));
            Assert.That(services.EconomyState.CaptureSnapshot().Characters.Length, Is.EqualTo(5));
        }

        [Test]
        public void DefaultResidents_OwnIndependentAssetsAndPersistARealTrade()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            var config = DefaultMvpContent.CreateConfiguration();
            Assert.That(services.EconomyState.TryGetCharacter(DefaultMvpIds.Npcs.Fisher, out var ren), Is.True);
            Assert.That(ren.Backpack.Items.Single().Quantity, Is.EqualTo(2));
            var trading = new CharacterResourceTrading(services.EconomyState, config.Items, config.InventoryCapacitySlots);
            var terms = new CharacterTradeTerms(DefaultMvpIds.Npcs.Fisher, DefaultMvpIds.Npcs.Cook, DefaultMvpIds.Items.Carp, 1, 25);
            Assert.That(trading.Exchange(terms).IsSuccess, Is.True);
            Assert.That(services.GameSave.Save().IsSuccess, Is.True);
            var sora = new CharacterInventoryAdapter(config.Items, config.InventoryCapacitySlots, DefaultMvpIds.Npcs.Cook, services.EconomyState);
            Assert.That(sora.Remove(DefaultMvpIds.Items.Carp, 1).IsSuccess, Is.True);
            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(sora.Count(DefaultMvpIds.Items.Carp), Is.EqualTo(1));
            Assert.That(trading.Inspect(terms, DefaultMvpIds.Npcs.Cook).Balance, Is.EqualTo(25));
            Assert.That(trading.Inspect(terms, DefaultMvpIds.Npcs.Fisher).OwnedQuantity, Is.EqualTo(1));
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.Carp), Is.Zero);
            Assert.That(services.Wallet.Balance, Is.EqualTo(300));
        }

        [Test]
        public void PlayerOnlySave_UpgradesWithoutReplacingTheSavedPlayer()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            Assert.That(services.Wallet.Debit(17).IsSuccess, Is.True);
            services.GameSave.Save();
            var saved = services.SaveStorage.Load("main").Value;
            services.SaveStorage.Save("main", new GameSaveSnapshot(saved.SchemaVersion, saved.WorldSeed, saved.Clock,
                saved.Characters.Where(item => item.CharacterId == DefaultMvpIds.Characters.Player).ToArray(), saved.Shops, saved.Farm, saved.Livestock));
            Assert.That(services.GameSave.Load().IsSuccess, Is.True);
            Assert.That(services.Wallet.Balance, Is.EqualTo(283));
            Assert.That(services.EconomyState.TryGetCharacter(DefaultMvpIds.Npcs.Fisher, out var ren), Is.True);
            Assert.That(ren.Backpack.Items.Single().Quantity, Is.EqualTo(2));
        }

        [Test]
        public void DeliveredFish_CooksThroughTheExistingRecipeInSorasBackpack()
        {
            var services = CozyTownCompositionRoot.CreateDefault();
            var config = DefaultMvpContent.CreateConfiguration();
            var trading = new CharacterResourceTrading(services.EconomyState, config.Items, config.InventoryCapacitySlots);
            Assert.That(trading.Exchange(new CharacterTradeTerms(DefaultMvpIds.Npcs.Fisher, DefaultMvpIds.Npcs.Cook, DefaultMvpIds.Items.Carp, 1, 25)).IsSuccess, Is.True);
            var inventory = new CharacterInventoryAdapter(config.Items, config.InventoryCapacitySlots, DefaultMvpIds.Npcs.Cook, services.EconomyState);
            Assert.That(new InMemoryCookingService(config.Recipes, inventory).Cook(DefaultMvpIds.Recipes.GrilledFish).IsSuccess, Is.True);
            Assert.That(inventory.Count(DefaultMvpIds.Items.Carp), Is.Zero);
            Assert.That(inventory.Count(DefaultMvpIds.Items.Salt), Is.Zero);
            Assert.That(inventory.Count(DefaultMvpIds.Items.GrilledFish), Is.EqualTo(1));
            Assert.That(services.Inventory.Count(DefaultMvpIds.Items.GrilledFish), Is.Zero);
        }
    }
}
