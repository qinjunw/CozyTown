using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Economy
{
    public sealed class InMemoryWallet : IWallet
    {
        public InMemoryWallet(int startingBalance = 0)
        {
            Balance = startingBalance < 0 ? 0 : startingBalance;
        }

        public int Balance { get; private set; }

        public OperationResult Credit(int amount)
        {
            if (amount <= 0)
            {
                return OperationResult.Failure("wallet.amount_invalid");
            }

            if (Balance > int.MaxValue - amount)
            {
                return OperationResult.Failure("wallet.balance_overflow");
            }

            Balance += amount;
            return OperationResult.Success();
        }

        public OperationResult Debit(int amount)
        {
            if (amount <= 0)
            {
                return OperationResult.Failure("wallet.amount_invalid");
            }

            if (Balance < amount)
            {
                return OperationResult.Failure("wallet.insufficient_funds");
            }

            Balance -= amount;
            return OperationResult.Success();
        }

        public WalletSnapshot CaptureSnapshot()
        {
            return new WalletSnapshot(Balance);
        }

        public OperationResult Restore(WalletSnapshot snapshot)
        {
            if (snapshot.Balance < 0)
            {
                return OperationResult.Failure("wallet.snapshot_invalid");
            }

            Balance = snapshot.Balance;
            return OperationResult.Success();
        }
    }
}
