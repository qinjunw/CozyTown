using System.Linq;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Inventory
{
    public sealed class CharacterInventoryAdapterTests
    {
        private const string CharacterId = "character.player";

        [Test]
        public void Add_WhenBackpackHasCapacity_CommitsBackpackAndPreservesWallet()
        {
            IEconomyStateStore store = CreateStore(
                balance: 300,
                new ItemStack("item.potato", 1));
            IInventory inventory = CreateAdapter(store, capacitySlots: 3);

            Assert.That(inventory.Add("item.potato", 2).IsSuccess, Is.True);

            Assert.That(store.TryGetCharacter(CharacterId, out CharacterEconomySnapshot stored), Is.True);
            Assert.That(Quantity(stored.Backpack, "item.potato"), Is.EqualTo(3));
            Assert.That(stored.Wallet.Balance, Is.EqualTo(300));
        }

        [Test]
        public void Add_WhenBackpackCapacityWouldBeExceeded_LeavesCharacterUnchanged()
        {
            IEconomyStateStore store = CreateStore(
                balance: 300,
                new ItemStack("item.potato", 2));
            IInventory inventory = CreateAdapter(store, capacitySlots: 1);

            var result = inventory.Add("item.potato", 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("inventory.capacity_exceeded"));
            Assert.That(store.TryGetCharacter(CharacterId, out CharacterEconomySnapshot stored), Is.True);
            Assert.That(Quantity(stored.Backpack, "item.potato"), Is.EqualTo(2));
            Assert.That(stored.Wallet.Balance, Is.EqualTo(300));
        }

        [Test]
        public void Restore_WhenSnapshotIsValid_ReplacesBackpackAndPreservesWallet()
        {
            IEconomyStateStore store = CreateStore(
                balance: 425,
                new ItemStack("item.potato", 1));
            IInventory inventory = CreateAdapter(store, capacitySlots: 3);

            var result = inventory.Restore(
                new InventorySnapshot(new[] { new ItemStack("item.egg", 4) }));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(store.TryGetCharacter(CharacterId, out CharacterEconomySnapshot stored), Is.True);
            Assert.That(Quantity(stored.Backpack, "item.potato"), Is.Zero);
            Assert.That(Quantity(stored.Backpack, "item.egg"), Is.EqualTo(4));
            Assert.That(stored.Wallet.Balance, Is.EqualTo(425));
        }

        [Test]
        public void ReadsAndProjection_WhenStoreChangesExternally_UseLatestCharacterState()
        {
            IEconomyStateStore store = CreateStore(balance: 300);
            var adapter = (CharacterInventoryAdapter)CreateAdapter(store, capacitySlots: 3);
            Assert.That(
                store.CommitCharacter(
                    Character(
                        balance: 280,
                        new ItemStack("item.potato", 3),
                        new ItemStack("item.egg", 1))).IsSuccess,
                Is.True);

            InventoryProjection projection = adapter.CaptureProjection();

            Assert.That(adapter.Count("item.potato"), Is.EqualTo(3));
            Assert.That(adapter.Contains("item.egg", 1), Is.True);
            Assert.That(
                adapter.CaptureSnapshot().Items.Select(item => item.ItemId),
                Is.EqualTo(new[] { "item.egg", "item.potato" }));
            Assert.That(
                projection.Slots.Select(slot => slot.ItemId),
                Is.EqualTo(new[] { "item.potato", "item.potato", "item.egg" }));
            Assert.That(
                projection.Slots.Select(slot => slot.Quantity),
                Is.EqualTo(new[] { 2, 1, 1 }));
        }

        private static IInventory CreateAdapter(
            IEconomyStateStore store,
            int capacitySlots)
        {
            return new CharacterInventoryAdapter(
                Catalog(),
                capacitySlots,
                CharacterId,
                store);
        }

        private static IEconomyStateStore CreateStore(
            int balance,
            params ItemStack[] items)
        {
            return new InMemoryEconomyStateStore(
                new[] { Character(balance, items) },
                new ShopEconomySnapshot[0],
                Catalog(),
                characterBackpackCapacitySlots: 3);
        }

        private static CharacterEconomySnapshot Character(
            int balance,
            params ItemStack[] items)
        {
            return new CharacterEconomySnapshot(
                CharacterId,
                new InventorySnapshot(items),
                new WalletSnapshot(balance));
        }

        private static ItemDefinition[] Catalog()
        {
            return new[]
            {
                new ItemDefinition(
                    "item.potato",
                    "Potato",
                    ItemCategory.Crop,
                    maxStack: 2),
                new ItemDefinition(
                    "item.egg",
                    "Egg",
                    ItemCategory.AnimalProduct,
                    maxStack: 10)
            };
        }

        private static int Quantity(InventorySnapshot snapshot, string itemId)
        {
            return snapshot.Items
                .Where(item => item.ItemId == itemId)
                .Select(item => item.Quantity)
                .DefaultIfEmpty(0)
                .Single();
        }
    }
}
