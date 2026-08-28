using System;

namespace CozyTown.Runtime.Inventory
{
    [Serializable]
    public sealed class ItemDefinition
    {
        public ItemDefinition(string id, string displayName, ItemCategory category, int maxStack)
        {
            Id = id;
            DisplayName = displayName;
            Category = category;
            MaxStack = maxStack;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public ItemCategory Category { get; }

        public int MaxStack { get; }
    }
}
