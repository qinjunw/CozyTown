using System;

namespace CozyTown.Runtime.Economy
{
    [Serializable]
    public sealed class EconomyStateSnapshot
    {
        public EconomyStateSnapshot(
            CharacterEconomySnapshot[] characters,
            ShopEconomySnapshot[] shops)
        {
            _characters = Copy(characters);
            _shops = Copy(shops);
        }

        private readonly CharacterEconomySnapshot[] _characters;
        private readonly ShopEconomySnapshot[] _shops;

        public CharacterEconomySnapshot[] Characters => Copy(_characters);

        public ShopEconomySnapshot[] Shops => Copy(_shops);

        private static CharacterEconomySnapshot[] Copy(
            CharacterEconomySnapshot[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<CharacterEconomySnapshot>();
            }

            var copy = new CharacterEconomySnapshot[source.Length];
            for (int index = 0; index < copy.Length; index++)
            {
                CharacterEconomySnapshot character = source[index];
                copy[index] = character == null
                    ? null
                    : new CharacterEconomySnapshot(
                        character.CharacterId,
                        character.Backpack,
                        character.Wallet);
            }

            return copy;
        }

        private static ShopEconomySnapshot[] Copy(ShopEconomySnapshot[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<ShopEconomySnapshot>();
            }

            var copy = new ShopEconomySnapshot[source.Length];
            for (int index = 0; index < copy.Length; index++)
            {
                ShopEconomySnapshot shop = source[index];
                copy[index] = shop == null
                    ? null
                    : new ShopEconomySnapshot(
                        shop.ShopId,
                        shop.Stock,
                        shop.Wallet,
                        shop.LastRestockedDay,
                        shop.RestockAlgorithmVersion);
            }

            return copy;
        }
    }
}
