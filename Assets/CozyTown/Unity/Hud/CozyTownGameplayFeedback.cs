using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public static class CozyTownGameplayFeedback
    {
        public static string Failure(string action, string errorCode, Object context)
        {
            switch (errorCode)
            {
                case "fishing.roll_has_no_catch":
                    return "No bite this time. Try casting again.";
                case "inventory.capacity_exceeded":
                    return "Your backpack is full. Sell or use some items, then try again.";
                case "wallet.insufficient_funds" when action == "Buy":
                    return "You need more coins to buy this item.";
                case "wallet.insufficient_funds" when action == "Sell":
                    return "The shop does not have enough coins to buy this item.";
                case "inventory.insufficient_quantity" when action == "Buy":
                    return "This item is sold out. Check the shop after the next morning restock.";
                case "inventory.insufficient_quantity" when action == "Sell":
                    return "You no longer have enough of this item to sell.";
                case "inventory.insufficient_quantity" when action == "Plant":
                    return "You need this seed in your backpack. Buy it at the shop first.";
                case "inventory.insufficient_quantity" when action == "Feed":
                    return "You need chicken feed in your backpack. Buy it at the shop first.";
                case "livestock.product_pending":
                    return "Collect the waiting product before feeding this animal again.";
                case "livestock.product_not_ready":
                    return "No product is ready. Feed the animal and wait until the next morning.";
                case "cooking.ingredients_missing":
                    return "You do not have all the ingredients. Gather the missing ingredients and try again.";
                default:
                    Debug.LogWarning($"{action} failed: {errorCode}", context);
                    return "This action is unavailable right now. Close this panel and try again.";
            }
        }
    }
}
