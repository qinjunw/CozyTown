using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Economy
{
    public interface IWallet
    {
        int Balance { get; }

        OperationResult Credit(int amount);

        OperationResult Debit(int amount);

        WalletSnapshot CaptureSnapshot();

        OperationResult Restore(WalletSnapshot snapshot);
    }
}
