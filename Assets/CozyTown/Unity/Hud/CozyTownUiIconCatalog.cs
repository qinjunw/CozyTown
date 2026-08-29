using System;
using System.Collections.Generic;
using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownUiIconCatalog : MonoBehaviour
    {
        [SerializeField] private string[] itemIds = Array.Empty<string>();
        [SerializeField] private Sprite[] itemSprites = Array.Empty<Sprite>();
        [SerializeField] private string[] npcIds = Array.Empty<string>();
        [SerializeField] private Sprite[] npcSprites = Array.Empty<Sprite>();

        public IReadOnlyList<Sprite> ItemSprites => Array.AsReadOnly(itemSprites);

        public IReadOnlyList<Sprite> NpcSprites => Array.AsReadOnly(npcSprites);

        public void Configure(
            string[] configuredItemIds,
            Sprite[] configuredItemSprites,
            string[] configuredNpcIds,
            Sprite[] configuredNpcSprites)
        {
            ValidateEntries(configuredItemIds, configuredItemSprites, nameof(configuredItemIds));
            ValidateEntries(configuredNpcIds, configuredNpcSprites, nameof(configuredNpcIds));

            itemIds = (string[])configuredItemIds.Clone();
            itemSprites = (Sprite[])configuredItemSprites.Clone();
            npcIds = (string[])configuredNpcIds.Clone();
            npcSprites = (Sprite[])configuredNpcSprites.Clone();
        }

        public Sprite GetItemSprite(string itemId) => FindSprite(itemIds, itemSprites, itemId);

        public Sprite GetNpcSprite(string npcId) => FindSprite(npcIds, npcSprites, npcId);

        private static Sprite FindSprite(string[] ids, Sprite[] sprites, string stableId)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                return null;
            }

            for (var index = 0; index < ids.Length; index++)
            {
                if (string.Equals(ids[index], stableId, StringComparison.Ordinal))
                {
                    return sprites[index];
                }
            }

            return null;
        }

        private static void ValidateEntries(string[] ids, Sprite[] sprites, string parameterName)
        {
            if (ids == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (sprites == null)
            {
                throw new ArgumentNullException(nameof(sprites));
            }

            if (ids.Length != sprites.Length)
            {
                throw new ArgumentException(
                    "Stable ID and Sprite arrays must have the same length.",
                    parameterName);
            }

            var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < ids.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(ids[index]))
                {
                    throw new ArgumentException("Stable IDs must not be empty.", parameterName);
                }

                if (!uniqueIds.Add(ids[index]))
                {
                    throw new ArgumentException("Stable IDs must be unique.", parameterName);
                }

                if (sprites[index] == null)
                {
                    throw new ArgumentException("Sprite entries must not be null.", parameterName);
                }
            }
        }
    }
}
