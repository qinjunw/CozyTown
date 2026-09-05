using System;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Save
{
    [Serializable]
    public sealed class GameSaveSnapshot
    {
        public const int CurrentSchemaVersion = 3;

        private readonly CharacterEconomySnapshot[] _characters;
        private readonly ShopEconomySnapshot[] _shops;

        public GameSaveSnapshot(
            int schemaVersion,
            int worldSeed,
            GameClockSnapshot clock,
            CharacterEconomySnapshot[] characters,
            ShopEconomySnapshot[] shops,
            FarmSnapshot farm,
            LivestockSnapshot livestock)
        {
            SchemaVersion = schemaVersion;
            WorldSeed = worldSeed;
            Clock = clock;
            _characters = Copy(characters);
            _shops = Copy(shops);
            Farm = farm;
            Livestock = livestock;
        }

        public int SchemaVersion { get; }

        public int WorldSeed { get; }

        public GameClockSnapshot Clock { get; }

        public CharacterEconomySnapshot[] Characters => Copy(_characters);

        public ShopEconomySnapshot[] Shops => Copy(_shops);

        public FarmSnapshot Farm { get; }

        public LivestockSnapshot Livestock { get; }

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
