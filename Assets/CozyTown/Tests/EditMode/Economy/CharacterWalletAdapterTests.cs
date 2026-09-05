using System.Linq;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Economy
{
    public sealed class CharacterWalletAdapterTests
    {
        private const string CharacterId = "character.player";

        [Test]
        public void CreditAndDebit_WhenAmountsAreValid_CommitBalanceAndPreserveBackpack()
        {
            IEconomyStateStore store = CreateStore(balance: 300);
            IWallet wallet = new CharacterWalletAdapter(CharacterId, store);

            Assert.That(wallet.Credit(25).IsSuccess, Is.True);
            Assert.That(wallet.Debit(10).IsSuccess, Is.True);

            Assert.That(store.TryGetCharacter(CharacterId, out CharacterEconomySnapshot stored), Is.True);
            Assert.That(stored.Wallet.Balance, Is.EqualTo(315));
            Assert.That(Quantity(stored.Backpack, "item.potato"), Is.EqualTo(2));
        }

        [Test]
        public void Debit_WhenFundsAreInsufficient_LeavesCharacterUnchanged()
        {
            IEconomyStateStore store = CreateStore(balance: 5);
            IWallet wallet = new CharacterWalletAdapter(CharacterId, store);

            var result = wallet.Debit(6);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("wallet.insufficient_funds"));
            Assert.That(store.TryGetCharacter(CharacterId, out CharacterEconomySnapshot stored), Is.True);
            Assert.That(stored.Wallet.Balance, Is.EqualTo(5));
            Assert.That(Quantity(stored.Backpack, "item.potato"), Is.EqualTo(2));
        }

        [Test]
        public void Restore_WhenSnapshotIsValid_ReplacesBalanceAndPreservesBackpack()
        {
            IEconomyStateStore store = CreateStore(balance: 300);
            IWallet wallet = new CharacterWalletAdapter(CharacterId, store);

            var result = wallet.Restore(new WalletSnapshot(425));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(store.TryGetCharacter(CharacterId, out CharacterEconomySnapshot stored), Is.True);
            Assert.That(stored.Wallet.Balance, Is.EqualTo(425));
            Assert.That(Quantity(stored.Backpack, "item.potato"), Is.EqualTo(2));
        }

        [Test]
        public void BalanceAndSnapshot_WhenStoreChangesExternally_UseLatestCharacterState()
        {
            IEconomyStateStore store = CreateStore(balance: 300);
            IWallet wallet = new CharacterWalletAdapter(CharacterId, store);
            Assert.That(
                store.CommitCharacter(
                    new CharacterEconomySnapshot(
                        CharacterId,
                        new InventorySnapshot(new[] { new ItemStack("item.potato", 1) }),
                        new WalletSnapshot(280))).IsSuccess,
                Is.True);

            Assert.That(wallet.Balance, Is.EqualTo(280));
            Assert.That(wallet.CaptureSnapshot().Balance, Is.EqualTo(280));
        }

        private static IEconomyStateStore CreateStore(int balance)
        {
            return new InMemoryEconomyStateStore(
                new[]
                {
                    new CharacterEconomySnapshot(
                        CharacterId,
                        new InventorySnapshot(
                            new[] { new ItemStack("item.potato", 2) }),
                        new WalletSnapshot(balance))
                },
                new ShopEconomySnapshot[0]);
        }

        private static int Quantity(InventorySnapshot snapshot, string itemId)
        {
            return snapshot.Items.Single(item => item.ItemId == itemId).Quantity;
        }
    }
}
