using System;

namespace CozyTown.Runtime.Farming
{
    [Serializable]
    public sealed class CropDefinition
    {
        public CropDefinition(
            string id,
            string seedItemId,
            string harvestItemId,
            int growthDays,
            int harvestQuantity)
        {
            Id = id;
            SeedItemId = seedItemId;
            HarvestItemId = harvestItemId;
            GrowthDays = growthDays;
            HarvestQuantity = harvestQuantity;
        }

        public string Id { get; }

        public string SeedItemId { get; }

        public string HarvestItemId { get; }

        public int GrowthDays { get; }

        public int HarvestQuantity { get; }
    }
}
