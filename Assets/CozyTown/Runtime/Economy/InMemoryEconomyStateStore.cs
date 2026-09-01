using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Economy
{
    public sealed class InMemoryEconomyStateStore : IEconomyStateStore
    {
        private Dictionary<string, CharacterEconomySnapshot> _characters =
            new Dictionary<string, CharacterEconomySnapshot>(StringComparer.Ordinal);
        private Dictionary<string, ShopEconomySnapshot> _shops =
            new Dictionary<string, ShopEconomySnapshot>(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, ItemDefinition> _catalog;
        private readonly int? _characterBackpackCapacitySlots;

        public InMemoryEconomyStateStore(
            IEnumerable<CharacterEconomySnapshot> characters,
            IEnumerable<ShopEconomySnapshot> shops,
            IEnumerable<ItemDefinition> catalog = null,
            int? characterBackpackCapacitySlots = null)
        {
            _catalog = BuildCatalog(catalog);
            if (characterBackpackCapacitySlots.HasValue
                && (characterBackpackCapacitySlots.Value <= 0 || _catalog == null))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(characterBackpackCapacitySlots));
            }

            _characterBackpackCapacitySlots = characterBackpackCapacitySlots;
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

        public EconomyStateSnapshot CaptureSnapshot()
        {
            CharacterEconomySnapshot[] characters = _characters.Values
                .OrderBy(character => character.CharacterId, StringComparer.Ordinal)
                .Select(Copy)
                .ToArray();
            ShopEconomySnapshot[] shops = _shops.Values
                .OrderBy(shop => shop.ShopId, StringComparer.Ordinal)
                .Select(Copy)
                .ToArray();
            return new EconomyStateSnapshot(characters, shops);
        }

        public OperationResult Restore(EconomyStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return OperationResult.Failure("economy.snapshot_invalid");
            }

            var characters = new Dictionary<string, CharacterEconomySnapshot>(
                StringComparer.Ordinal);
            foreach (CharacterEconomySnapshot character in snapshot.Characters)
            {
                if (!IsValid(character)
                    || !characters.TryAdd(character.CharacterId, Copy(character)))
                {
                    return OperationResult.Failure("economy.character_invalid");
                }
            }

            var shops = new Dictionary<string, ShopEconomySnapshot>(StringComparer.Ordinal);
            foreach (ShopEconomySnapshot shop in snapshot.Shops)
            {
                if (!IsValid(shop) || !shops.TryAdd(shop.ShopId, Copy(shop)))
                {
                    return OperationResult.Failure("economy.shop_invalid");
                }
            }

            if (!characters.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(_characters.Keys)
                || !shops.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(_shops.Keys))
            {
                return OperationResult.Failure("economy.identity_set_mismatch");
            }

            _characters = characters;
            _shops = shops;
            return OperationResult.Success();
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

        public OperationResult CommitShop(ShopEconomySnapshot shopCandidate)
        {
            if (!IsValid(shopCandidate))
            {
                return OperationResult.Failure("economy.shop_invalid");
            }

            if (!_shops.ContainsKey(shopCandidate.ShopId))
            {
                return OperationResult.Failure("economy.shop_unknown");
            }

            ShopEconomySnapshot shop = Copy(shopCandidate);
            _shops[shop.ShopId] = shop;
            return OperationResult.Success();
        }

        public OperationResult CommitCharacter(
            CharacterEconomySnapshot characterCandidate)
        {
            if (!IsValid(characterCandidate))
            {
                return OperationResult.Failure("economy.character_invalid");
            }

            if (!_characters.ContainsKey(characterCandidate.CharacterId))
            {
                return OperationResult.Failure("economy.character_unknown");
            }

            CharacterEconomySnapshot character = Copy(characterCandidate);
            _characters[character.CharacterId] = character;
            return OperationResult.Success();
        }

        private bool IsValid(CharacterEconomySnapshot snapshot)
        {
            return snapshot != null
                && !string.IsNullOrWhiteSpace(snapshot.CharacterId)
                && snapshot.Wallet.Balance >= 0
                && IsValid(snapshot.Backpack, enforceCharacterCapacity: true);
        }

        private bool IsValid(ShopEconomySnapshot snapshot)
        {
            return snapshot != null
                && !string.IsNullOrWhiteSpace(snapshot.ShopId)
                && snapshot.Wallet.Balance >= 0
                && snapshot.LastRestockedDay > 0
                && snapshot.RestockAlgorithmVersion > 0
                && IsValid(snapshot.Stock, enforceCharacterCapacity: false);
        }

        private bool IsValid(
            InventorySnapshot snapshot,
            bool enforceCharacterCapacity)
        {
            if (snapshot == null)
            {
                return false;
            }

            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            long usedSlots = 0;
            foreach (ItemStack item in snapshot.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ItemId)
                    || item.Quantity <= 0
                    || (_catalog != null && !_catalog.ContainsKey(item.ItemId))
                    || !itemIds.Add(item.ItemId))
                {
                    return false;
                }

                if (enforceCharacterCapacity && _catalog != null)
                {
                    int maxStack = _catalog[item.ItemId].MaxStack;
                    usedSlots += ((long)item.Quantity + maxStack - 1) / maxStack;
                }
            }

            return !enforceCharacterCapacity
                || !_characterBackpackCapacitySlots.HasValue
                || usedSlots <= _characterBackpackCapacitySlots.Value;
        }

        private static IReadOnlyDictionary<string, ItemDefinition> BuildCatalog(
            IEnumerable<ItemDefinition> catalog)
        {
            if (catalog == null)
            {
                return null;
            }

            var result = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            foreach (ItemDefinition definition in catalog)
            {
                if (definition == null
                    || string.IsNullOrWhiteSpace(definition.Id)
                    || definition.MaxStack <= 0
                    || !result.TryAdd(definition.Id, definition))
                {
                    throw new ArgumentException(
                        "Economy catalog entries must be valid and unique.",
                        nameof(catalog));
                }
            }

            return result;
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
