using System;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Application;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Application
{
    public sealed class CharacterResourceTradingTests
    {
        [TestCase(0, 50, 0, 2, "inventory.insufficient_quantity")]
        [TestCase(2, 24, 0, 2, "wallet.insufficient_funds")]
        [TestCase(2, 50, int.MaxValue, 2, "wallet.balance_overflow")]
        [TestCase(2, 50, 0, 1, "inventory.capacity_exceeded")]
        public void FailedExchange_LeavesBothAssetsUnchanged(int fish, int funds, int sellerFunds, int slots, string expected)
        {
            var buyer = new CharacterEconomySnapshot("sora", new InventorySnapshot(new[] { new ItemStack("salt", 1) }), new WalletSnapshot(funds));
            var store = new InMemoryEconomyStateStore(new[] { Character("ren", fish, sellerFunds), buyer }, Array.Empty<ShopEconomySnapshot>());
            var trading = new CharacterResourceTrading(store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99), new ItemDefinition("salt", "Salt", ItemCategory.Material, 99) }, slots);
            var result = trading.Exchange(new CharacterTradeTerms("ren", "sora", "fish", 1, 25));
            Assert.That(result.ErrorCode, Is.EqualTo(expected));
            store.TryGetCharacter("ren", out var ren);
            store.TryGetCharacter("sora", out var sora);
            Assert.That(ren.Backpack.Items, Is.EqualTo(Character("ren", fish, sellerFunds).Backpack.Items));
            Assert.That(ren.Wallet.Balance, Is.EqualTo(sellerFunds));
            Assert.That(sora.Backpack.Items, Is.EqualTo(buyer.Backpack.Items));
            Assert.That(sora.Wallet.Balance, Is.EqualTo(funds));
        }

        [TestCase("sora", -1)]
        [TestCase("stranger", 0)]
        [TestCase("ren", 0)]
        public void InvalidSecondCandidate_DoesNotPublishTheFirst(string secondId, int balance)
        {
            var store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) }, Array.Empty<ShopEconomySnapshot>());
            Assert.That(store.CommitCharacters(Character("ren", 1, 25), Character(secondId, 1, balance)).IsSuccess, Is.False);
            store.TryGetCharacter("ren", out var ren);
            store.TryGetCharacter("sora", out var sora);
            Assert.That(ren.Backpack.Items[0].Quantity, Is.EqualTo(2));
            Assert.That(ren.Wallet.Balance, Is.Zero);
            Assert.That(sora.Backpack.Items, Is.Empty);
            Assert.That(sora.Wallet.Balance, Is.EqualTo(50));
        }

        [Test]
        public void Exchange_ConservesFishAndMoneyAcrossOwnedBackpacks()
        {
            var store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) }, Array.Empty<ShopEconomySnapshot>());
            var trading = new CharacterResourceTrading(store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            Assert.That(trading.Exchange(new CharacterTradeTerms("ren", "sora", "fish", 1, 25)).IsSuccess, Is.True);
            store.TryGetCharacter("ren", out var ren);
            store.TryGetCharacter("sora", out var sora);
            Assert.That(ren.Backpack.Items[0].Quantity, Is.EqualTo(1));
            Assert.That(sora.Backpack.Items[0].Quantity, Is.EqualTo(1));
            Assert.That(ren.Wallet.Balance, Is.EqualTo(25));
            Assert.That(sora.Wallet.Balance, Is.EqualTo(25));
        }

        [Test]
        public void CommitTwoCharacters_PublishesBothCandidates()
        {
            var store = new InMemoryEconomyStateStore(new[] { Character("ren", 2, 0), Character("sora", 0, 50) }, Array.Empty<ShopEconomySnapshot>());
            Assert.That(store.CommitCharacters(Character("ren", 1, 25), Character("sora", 1, 25)).IsSuccess, Is.True);
            store.TryGetCharacter("ren", out var ren);
            store.TryGetCharacter("sora", out var sora);
            Assert.That(ren.Backpack.Items[0].Quantity, Is.EqualTo(1));
            Assert.That(sora.Backpack.Items[0].Quantity, Is.EqualTo(1));
            Assert.That(ren.Wallet.Balance + sora.Wallet.Balance, Is.EqualTo(50));
        }

        private static CharacterEconomySnapshot Character(string id, int fish, int balance)
            => new CharacterEconomySnapshot(id, new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>() : new[] { new ItemStack("fish", fish) }), new WalletSnapshot(balance));
    }
}
