using System;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.NpcAgents;

namespace CozyTown.Runtime.Content
{
    public static class AgentExperimentContent
    {
        public static CozyTownConfiguration CreateConfiguration(string scenarioId = "available")
        {
            if (scenarioId != "available" && scenarioId != "seller_empty"
                && scenarioId != "buyer_poor" && scenarioId != "need_satisfied")
                throw new ArgumentOutOfRangeException(nameof(scenarioId));
            var source = DefaultMvpContent.CreateConfiguration();
            var characters = source.InitialNpcEconomy;
            for (int i = 0; i < characters.Length; i++)
            {
                var character = characters[i];
                bool seller = character.CharacterId == DefaultMvpIds.Npcs.Fisher;
                if (!seller && character.CharacterId != DefaultMvpIds.Npcs.Cook) continue;
                int fish = seller ? (scenarioId == "seller_empty" ? 0 : 2) : (scenarioId == "need_satisfied" ? 1 : 0);
                int coins = seller ? 0 : (scenarioId == "buyer_poor" ? 10 : 50);
                var items = character.Backpack.Items.Where(item => item.ItemId != DefaultMvpIds.Items.Carp).ToList();
                if (fish > 0) items.Add(new ItemStack(DefaultMvpIds.Items.Carp, fish));
                characters[i] = new CharacterEconomySnapshot(character.CharacterId,
                    new InventorySnapshot(items.ToArray()), new WalletSnapshot(coins));
            }
            return new CozyTownConfiguration(source.Items, source.ShopOffers, source.Crops, source.FarmPlotIds,
                source.AnimalDefinitions, source.Animals, source.FishingEntries, source.Recipes,
                source.InventoryCapacitySlots, source.StartingBalance, source.StartingDay, 720,
                source.FallbackDialogue, source.Npcs, source.ShopRestockRules, source.StartingWorldSeed,
                source.StartingShopBalance, characters);
        }

        public static NpcMeetingPlan[] CreateMeetingPlans()
            => new[] {
                new NpcMeetingPlan("mina-eli-main-road-chat", DefaultMvpIds.Npcs.Shopkeeper,
                    DefaultMvpIds.Npcs.Farmer, "main-road-chat", "road.coop", "road.east_lane",
                    720, 740, 745, durationGameMinutes: 60),
                DefaultNpcResourcePlans.Create()[0] };
    }
}
