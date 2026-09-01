using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Inventory;

namespace CozyTown.Runtime.Economy
{
    public sealed class DeterministicShopStockReplacementPolicy
        : IShopStockReplacementPolicy
    {
        public const int VersionOne = 1;

        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;
        private const uint NonZeroFallbackState = 0x6D2B79F5u;

        private readonly ShopRestockRule[] _rules;
        private readonly int _minimumDistinctItems;

        public DeterministicShopStockReplacementPolicy(
            IEnumerable<ShopRestockRule> rules,
            int minimumDistinctItems)
        {
            _rules = ValidateAndCopy(rules);
            if (minimumDistinctItems <= 0
                || minimumDistinctItems > _rules.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumDistinctItems));
            }

            _minimumDistinctItems = minimumDistinctItems;
        }

        public OperationResult<ShopEconomySnapshot> CreateCandidate(
            int worldSeed,
            ShopEconomySnapshot current,
            int targetDay)
        {
            if (!IsValid(current))
            {
                return OperationResult<ShopEconomySnapshot>.Failure(
                    "shop_restock.shop_invalid");
            }

            if (targetDay <= 0)
            {
                return OperationResult<ShopEconomySnapshot>.Failure(
                    "shop_restock.day_invalid");
            }

            if (targetDay < current.LastRestockedDay)
            {
                return OperationResult<ShopEconomySnapshot>.Failure(
                    "shop_restock.day_regressed");
            }

            if (current.RestockAlgorithmVersion != VersionOne)
            {
                return OperationResult<ShopEconomySnapshot>.Failure(
                    "shop_restock.algorithm_unsupported");
            }

            if (targetDay == current.LastRestockedDay)
            {
                return OperationResult<ShopEconomySnapshot>.Success(Copy(current));
            }

            uint seed = CreateSeed(
                worldSeed,
                current.ShopId,
                targetDay,
                current.RestockAlgorithmVersion);
            var random = new XorShift32(seed);
            var candidates = new Candidate[_rules.Length];
            var selected = new bool[_rules.Length];
            int selectedCount = 0;
            for (int index = 0; index < _rules.Length; index++)
            {
                ShopRestockRule rule = _rules[index];
                int appearanceRoll = random.Next(1000);
                int quantity = rule.MinQuantity
                    + random.Next(rule.MaxQuantity - rule.MinQuantity + 1);
                candidates[index] = new Candidate(rule.ItemId, quantity);
                selected[index] = appearanceRoll < rule.AppearancePermille;
                if (selected[index])
                {
                    selectedCount++;
                }
            }

            while (selectedCount < _minimumDistinctItems)
            {
                var unselectedIndices = new List<int>();
                for (int index = 0; index < selected.Length; index++)
                {
                    if (!selected[index])
                    {
                        unselectedIndices.Add(index);
                    }
                }

                int selectedIndex = unselectedIndices[
                    random.Next(unselectedIndices.Count)];
                selected[selectedIndex] = true;
                selectedCount++;
            }

            ItemStack[] stock = candidates
                .Where((candidate, index) => selected[index])
                .Select(candidate => new ItemStack(
                    candidate.ItemId,
                    candidate.Quantity))
                .ToArray();
            return OperationResult<ShopEconomySnapshot>.Success(
                new ShopEconomySnapshot(
                    current.ShopId,
                    new InventorySnapshot(stock),
                    current.Wallet,
                    targetDay,
                    current.RestockAlgorithmVersion));
        }

        private static ShopRestockRule[] ValidateAndCopy(
            IEnumerable<ShopRestockRule> rules)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            ShopRestockRule[] copy = rules.ToArray();
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            if (copy.Length == 0
                || copy.Any(rule =>
                    rule == null
                    || string.IsNullOrWhiteSpace(rule.ItemId)
                    || rule.AppearancePermille < 0
                    || rule.AppearancePermille > 1000
                    || rule.MinQuantity <= 0
                    || rule.MaxQuantity < rule.MinQuantity
                    || !itemIds.Add(rule.ItemId)))
            {
                throw new ArgumentException(
                    "Restock rules must be valid and have unique item IDs.",
                    nameof(rules));
            }

            return copy;
        }

        private static bool IsValid(ShopEconomySnapshot snapshot)
        {
            if (snapshot == null
                || string.IsNullOrWhiteSpace(snapshot.ShopId)
                || snapshot.Stock == null
                || snapshot.Wallet.Balance < 0
                || snapshot.LastRestockedDay < 0)
            {
                return false;
            }

            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            return snapshot.Stock.Items.All(stack =>
                !string.IsNullOrWhiteSpace(stack.ItemId)
                && stack.Quantity > 0
                && itemIds.Add(stack.ItemId));
        }

        private static ShopEconomySnapshot Copy(ShopEconomySnapshot source)
        {
            return new ShopEconomySnapshot(
                source.ShopId,
                source.Stock,
                source.Wallet,
                source.LastRestockedDay,
                source.RestockAlgorithmVersion);
        }

        private static uint CreateSeed(
            int worldSeed,
            string shopId,
            int targetDay,
            int algorithmVersion)
        {
            uint hash = FnvOffsetBasis;
            AddInt32(ref hash, worldSeed);
            byte[] shopBytes = Encoding.UTF8.GetBytes(shopId);
            AddInt32(ref hash, shopBytes.Length);
            foreach (byte value in shopBytes)
            {
                AddByte(ref hash, value);
            }

            AddInt32(ref hash, targetDay);
            AddInt32(ref hash, algorithmVersion);
            return hash == 0 ? NonZeroFallbackState : hash;
        }

        private static void AddInt32(ref uint hash, int value)
        {
            uint unsigned = unchecked((uint)value);
            AddByte(ref hash, (byte)unsigned);
            AddByte(ref hash, (byte)(unsigned >> 8));
            AddByte(ref hash, (byte)(unsigned >> 16));
            AddByte(ref hash, (byte)(unsigned >> 24));
        }

        private static void AddByte(ref uint hash, byte value)
        {
            hash = unchecked((hash ^ value) * FnvPrime);
        }

        private readonly struct Candidate
        {
            public Candidate(string itemId, int quantity)
            {
                ItemId = itemId;
                Quantity = quantity;
            }

            public string ItemId { get; }

            public int Quantity { get; }
        }

        private sealed class XorShift32
        {
            private uint _state;

            public XorShift32(uint seed)
            {
                _state = seed == 0 ? NonZeroFallbackState : seed;
            }

            public int Next(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
                }

                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return (int)(_state % (uint)exclusiveMaximum);
            }
        }
    }
}
