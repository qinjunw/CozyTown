using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Economy
{
    public sealed class InMemoryShopServiceTests
    {
        [Test]
        public void Buy_WhenFundsAndCapacityAreAvailable_ChangesBothAssetsOnce()
        {
            var inventory = CreateInventory();
            var wallet = new InMemoryWallet(startingBalance: 20);
            var shop = CreateShop(wallet, inventory);

            OperationResult<ShopReceipt> result = shop.Buy("potato-seed", 2);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.ItemId, Is.EqualTo("potato-seed"));
            Assert.That(result.Value.Quantity, Is.EqualTo(2));
            Assert.That(result.Value.TotalPrice, Is.EqualTo(8));
            Assert.That(result.Value.IsPurchase, Is.True);
            Assert.That(wallet.Balance, Is.EqualTo(12));
            Assert.That(inventory.Count("potato-seed"), Is.EqualTo(2));
        }

        [Test]
        public void Buy_WhenFundsAreInsufficient_LeavesBothAssetsUnchanged()
        {
            var inventory = CreateInventory();
            var wallet = new InMemoryWallet(startingBalance: 3);
            var shop = CreateShop(wallet, inventory);

            OperationResult<ShopReceipt> result = shop.Buy("potato-seed", 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("wallet.insufficient_funds"));
            Assert.That(wallet.Balance, Is.EqualTo(3));
            Assert.That(inventory.Count("potato-seed"), Is.Zero);
        }

        [Test]
        public void Buy_WhenInventoryCannotAcceptItem_RefundsWalletAndAddsNothing()
        {
            var inventory = new InMemoryInventory(
                new[]
                {
                    new ItemDefinition("filler", "Filler", ItemCategory.Material, maxStack: 99),
                    new ItemDefinition("potato-seed", "Potato Seed", ItemCategory.Seed, maxStack: 99)
                },
                capacitySlots: 1);
            Assert.That(inventory.Add("filler", 1).IsSuccess, Is.True);
            var wallet = new InMemoryWallet(startingBalance: 10);
            var shop = new InMemoryShopService(
                new[] { new ShopOffer("potato-seed", buyPrice: 4, sellPrice: 2) },
                wallet,
                inventory);

            OperationResult<ShopReceipt> result = shop.Buy("potato-seed", 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("inventory.capacity_exceeded"));
            Assert.That(wallet.Balance, Is.EqualTo(10));
            Assert.That(inventory.Count("potato-seed"), Is.Zero);
            Assert.That(inventory.Count("filler"), Is.EqualTo(1));
        }

        [Test]
        public void Sell_WhenInventoryContainsItem_ChangesBothAssetsOnce()
        {
            var inventory = CreateInventory();
            Assert.That(inventory.Add("potato-seed", 3).IsSuccess, Is.True);
            var wallet = new InMemoryWallet(startingBalance: 10);
            var shop = CreateShop(wallet, inventory);

            OperationResult<ShopReceipt> result = shop.Sell("potato-seed", 2);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.ItemId, Is.EqualTo("potato-seed"));
            Assert.That(result.Value.Quantity, Is.EqualTo(2));
            Assert.That(result.Value.TotalPrice, Is.EqualTo(4));
            Assert.That(result.Value.IsPurchase, Is.False);
            Assert.That(wallet.Balance, Is.EqualTo(14));
            Assert.That(inventory.Count("potato-seed"), Is.EqualTo(1));
        }

        [Test]
        public void Sell_WhenQuantityIsInsufficient_LeavesBothAssetsUnchanged()
        {
            var inventory = CreateInventory();
            Assert.That(inventory.Add("potato-seed", 1).IsSuccess, Is.True);
            var wallet = new InMemoryWallet(startingBalance: 10);
            var shop = CreateShop(wallet, inventory);

            OperationResult<ShopReceipt> result = shop.Sell("potato-seed", 2);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("inventory.insufficient_quantity"));
            Assert.That(wallet.Balance, Is.EqualTo(10));
            Assert.That(inventory.Count("potato-seed"), Is.EqualTo(1));
        }

        [Test]
        public void Buy_WhenInventoryMutatesThenFails_RestoresWalletAndInventory()
        {
            var innerInventory = CreateInventory();
            var inventory = new FailAfterAddInventory(innerInventory);
            var wallet = new TrackingWallet(new InMemoryWallet(startingBalance: 20));
            var shop = CreateShop(wallet, inventory);

            OperationResult<ShopReceipt> result = shop.Buy("potato-seed", 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("injected.add_failure"));
            Assert.That(wallet.Balance, Is.EqualTo(20));
            Assert.That(innerInventory.Count("potato-seed"), Is.Zero);
            Assert.That(wallet.RestoreCallCount, Is.EqualTo(1));
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Sell_WhenWalletMutatesThenFails_RestoresWalletAndInventory()
        {
            var inventory = new TrackingInventory(CreateInventory());
            Assert.That(inventory.Add("potato-seed", 1).IsSuccess, Is.True);
            var innerWallet = new InMemoryWallet(startingBalance: 10);
            var wallet = new FailAfterCreditWallet(innerWallet);
            var shop = CreateShop(wallet, inventory);

            OperationResult<ShopReceipt> result = shop.Sell("potato-seed", 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("injected.credit_failure"));
            Assert.That(innerWallet.Balance, Is.EqualTo(10));
            Assert.That(inventory.Count("potato-seed"), Is.EqualTo(1));
            Assert.That(wallet.RestoreCallCount, Is.EqualTo(1));
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Buy_WhenWalletRollbackFails_ReturnsDiagnosticAndAttemptsInventoryRollback()
        {
            var inventory = new FailAfterAddInventory(CreateInventory());
            var wallet = new FailRestoreWallet(new InMemoryWallet(startingBalance: 20));
            var shop = CreateShop(wallet, inventory);

            OperationResult<ShopReceipt> result = shop.Buy("potato-seed", 1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("shop.rollback_wallet_failed"));
            Assert.That(wallet.RestoreCallCount, Is.EqualTo(1));
            Assert.That(inventory.RestoreCallCount, Is.EqualTo(1));
            Assert.That(inventory.Count("potato-seed"), Is.Zero);
        }

        private static InMemoryInventory CreateInventory()
        {
            return new InMemoryInventory(
                new[]
                {
                    new ItemDefinition(
                        "potato-seed",
                        "Potato Seed",
                        ItemCategory.Seed,
                        maxStack: 99)
                },
                capacitySlots: 4);
        }

        private static InMemoryShopService CreateShop(
            IWallet wallet,
            IInventory inventory)
        {
            return new InMemoryShopService(
                new[] { new ShopOffer("potato-seed", buyPrice: 4, sellPrice: 2) },
                wallet,
                inventory);
        }

        private class TrackingWallet : IWallet
        {
            protected readonly IWallet Inner;

            public TrackingWallet(IWallet inner)
            {
                Inner = inner;
            }

            public int RestoreCallCount { get; protected set; }

            public int Balance => Inner.Balance;

            public virtual OperationResult Credit(int amount) => Inner.Credit(amount);

            public OperationResult Debit(int amount) => Inner.Debit(amount);

            public WalletSnapshot CaptureSnapshot() => Inner.CaptureSnapshot();

            public virtual OperationResult Restore(WalletSnapshot snapshot)
            {
                RestoreCallCount++;
                return Inner.Restore(snapshot);
            }
        }

        private sealed class FailAfterCreditWallet : TrackingWallet
        {
            public FailAfterCreditWallet(IWallet inner)
                : base(inner)
            {
            }

            public override OperationResult Credit(int amount)
            {
                Inner.Credit(amount);
                return OperationResult.Failure("injected.credit_failure");
            }
        }

        private sealed class FailRestoreWallet : TrackingWallet
        {
            public FailRestoreWallet(IWallet inner)
                : base(inner)
            {
            }

            public override OperationResult Restore(WalletSnapshot snapshot)
            {
                RestoreCallCount++;
                return OperationResult.Failure("injected.restore_failure");
            }
        }

        private class TrackingInventory : IInventory
        {
            protected readonly IInventory Inner;

            public TrackingInventory(IInventory inner)
            {
                Inner = inner;
            }

            public int RestoreCallCount { get; protected set; }

            public int CapacitySlots => Inner.CapacitySlots;

            public int Count(string itemId) => Inner.Count(itemId);

            public bool Contains(string itemId, int quantity) => Inner.Contains(itemId, quantity);

            public virtual OperationResult Add(string itemId, int quantity) =>
                Inner.Add(itemId, quantity);

            public OperationResult Remove(string itemId, int quantity) =>
                Inner.Remove(itemId, quantity);

            public InventorySnapshot CaptureSnapshot() => Inner.CaptureSnapshot();

            public virtual OperationResult Restore(InventorySnapshot snapshot)
            {
                RestoreCallCount++;
                return Inner.Restore(snapshot);
            }
        }

        private sealed class FailAfterAddInventory : TrackingInventory
        {
            public FailAfterAddInventory(IInventory inner)
                : base(inner)
            {
            }

            public override OperationResult Add(string itemId, int quantity)
            {
                Inner.Add(itemId, quantity);
                return OperationResult.Failure("injected.add_failure");
            }
        }
    }
}
