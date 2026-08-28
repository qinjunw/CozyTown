using System;

namespace CozyTown.Runtime.Economy
{
    [Serializable]
    public readonly struct WalletSnapshot
    {
        public WalletSnapshot(int balance)
        {
            Balance = balance;
        }

        public int Balance { get; }
    }
}
