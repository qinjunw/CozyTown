using System;
using System.Collections.Generic;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Economy
{
    public sealed class InMemoryEconomyStateStore : IEconomyStateStore
    {
        private readonly Dictionary<string, CharacterEconomySnapshot> _characters =
            new Dictionary<string, CharacterEconomySnapshot>(StringComparer.Ordinal);
        private readonly Dictionary<string, ShopEconomySnapshot> _shops =
            new Dictionary<string, ShopEconomySnapshot>(StringComparer.Ordinal);

        public InMemoryEconomyStateStore(
            IEnumerable<CharacterEconomySnapshot> characters,
            IEnumerable<ShopEconomySnapshot> shops)
        {
            foreach (CharacterEconomySnapshot character in
                characters ?? Array.Empty<CharacterEconomySnapshot>())
            {
                if (!IsValid(character) || _characters.ContainsKey(character.CharacterId))
                {
                    throw new ArgumentException(
                        "Character economy state is invalid or duplicated.",
                        nameof(characters));
                }

                _characters.Add(character.CharacterId, Copy(character));
            }

            foreach (ShopEconomySnapshot shop in
                shops ?? Array.Empty<ShopEconomySnapshot>())
            {
                if (!IsValid(shop) || _shops.ContainsKey(shop.ShopId))
                {
                    throw new ArgumentException(
                        "Shop economy state is invalid or duplicated.",
                        nameof(shops));
                }

                _shops.Add(shop.ShopId, Copy(shop));
            }
        }

        public bool TryGetCharacter(
            string characterId,
            out CharacterEconomySnapshot snapshot)
        {
            if (characterId != null
                && _characters.TryGetValue(characterId, out CharacterEconomySnapshot stored))
            {
                snapshot = Copy(stored);
                return true;
            }

            snapshot = null;
            return false;
        }

        public bool TryGetShop(
            string shopId,
            out ShopEconomySnapshot snapshot)
        {
            if (shopId != null && _shops.TryGetValue(shopId, out ShopEconomySnapshot stored))
            {
                snapshot = Copy(stored);
                return true;
            }

            snapshot = null;
            return false;
        }

        public OperationResult Commit(
            CharacterEconomySnapshot characterCandidate,
            ShopEconomySnapshot shopCandidate)
        {
            if (!IsValid(characterCandidate))
            {
                return OperationResult.Failure("economy.character_invalid");
            }

            if (!IsValid(shopCandidate))
            {
                return OperationResult.Failure("economy.shop_invalid");
            }

            if (!_characters.ContainsKey(characterCandidate.CharacterId))
            {
                return OperationResult.Failure("economy.character_unknown");
            }

            if (!_shops.ContainsKey(shopCandidate.ShopId))
            {
                return OperationResult.Failure("economy.shop_unknown");
            }

            CharacterEconomySnapshot character = Copy(characterCandidate);
            ShopEconomySnapshot shop = Copy(shopCandidate);
            _characters[character.CharacterId] = character;
            _shops[shop.ShopId] = shop;
            return OperationResult.Success();
        }

        private static bool IsValid(CharacterEconomySnapshot snapshot)
        {
            return snapshot != null
                && !string.IsNullOrWhiteSpace(snapshot.CharacterId)
                && snapshot.Wallet.Balance >= 0
                && IsValid(snapshot.Backpack);
        }

        private static bool IsValid(ShopEconomySnapshot snapshot)
        {
            return snapshot != null
                && !string.IsNullOrWhiteSpace(snapshot.ShopId)
                && snapshot.Wallet.Balance >= 0
                && snapshot.LastRestockedDay > 0
                && snapshot.RestockAlgorithmVersion > 0
                && IsValid(snapshot.Stock);
        }

        private static bool IsValid(InventorySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemStack item in snapshot.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ItemId)
                    || item.Quantity <= 0
                    || !itemIds.Add(item.ItemId))
                {
                    return false;
                }
            }

            return true;
        }

        private static CharacterEconomySnapshot Copy(CharacterEconomySnapshot snapshot)
        {
            return new CharacterEconomySnapshot(
                snapshot.CharacterId,
                snapshot.Backpack,
                snapshot.Wallet);
        }

        private static ShopEconomySnapshot Copy(ShopEconomySnapshot snapshot)
        {
            return new ShopEconomySnapshot(
                snapshot.ShopId,
                snapshot.Stock,
                snapshot.Wallet,
                snapshot.LastRestockedDay,
                snapshot.RestockAlgorithmVersion);
        }
    }
}
