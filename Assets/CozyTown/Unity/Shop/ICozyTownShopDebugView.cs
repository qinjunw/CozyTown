using System;
using CozyTown.Runtime.Application;

namespace CozyTown.Unity.Shop
{
    public interface ICozyTownShopDebugView
    {
        event Action<string> BuyRequested;

        event Action<string> SellRequested;

        event Action CloseRequested;

        bool IsVisible { get; }

        void Show(ShopViewState state, string feedback);

        void Hide();
    }
}
