using System;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Economy
{
    public sealed class CharacterWalletAdapter : IWallet
    {
        private readonly string _characterId;
        private readonly IEconomyStateStore _stateStore;

        public CharacterWalletAdapter(
            string characterId,
            IEconomyStateStore stateStore)
        {
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            if (string.IsNullOrWhiteSpace(characterId))
            {
                throw new ArgumentException(
                    "Character ID must be provided.",
                    nameof(characterId));
            }

            if (!_stateStore.TryGetCharacter(characterId, out _))
            {
                throw new ArgumentException(
                    "Character economy state must exist before creating a wallet adapter.",
                    nameof(characterId));
            }

            _characterId = characterId;
        }

        public int Balance => CurrentCharacter().Wallet.Balance;

        public OperationResult Credit(int amount)
        {
            return Mutate(wallet => wallet.Credit(amount));
        }

        public OperationResult Debit(int amount)
        {
            return Mutate(wallet => wallet.Debit(amount));
        }

        public WalletSnapshot CaptureSnapshot()
        {
            return CurrentCharacter().Wallet;
        }

        public OperationResult Restore(WalletSnapshot snapshot)
        {
            return Mutate(wallet => wallet.Restore(snapshot));
        }

        private OperationResult Mutate(Func<InMemoryWallet, OperationResult> mutation)
        {
            if (!_stateStore.TryGetCharacter(
                    _characterId,
                    out CharacterEconomySnapshot character))
            {
                return OperationResult.Failure("economy.character_unknown");
            }

            var wallet = new InMemoryWallet(character.Wallet.Balance);
            OperationResult result = mutation(wallet);
            if (!result.IsSuccess)
            {
                return result;
            }

            return _stateStore.CommitCharacter(
                new CharacterEconomySnapshot(
                    character.CharacterId,
                    character.Backpack,
                    wallet.CaptureSnapshot()));
        }

        private CharacterEconomySnapshot CurrentCharacter()
        {
            if (_stateStore.TryGetCharacter(
                    _characterId,
                    out CharacterEconomySnapshot character))
            {
                return character;
            }

            throw new InvalidOperationException(
                "The character economy state is no longer available.");
        }
    }
}
