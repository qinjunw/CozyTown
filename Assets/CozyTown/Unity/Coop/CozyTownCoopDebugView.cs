using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
using UnityEngine;

namespace CozyTown.Unity.Coop
{
    public sealed class CozyTownCoopDebugView : CozyTownModalDebugViewBase
    {
        public event Action<string> FeedRequested;
        public event Action<string> CollectRequested;
        public LivestockViewState State { get; private set; }

        public void Show(LivestockViewState state, string feedback)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            ShowBase(feedback);
        }

        public void RequestFeed(string id)
        {
            if (IsVisible)
            {
                FeedRequested?.Invoke(id);
            }
        }

        public void RequestCollect(string id)
        {
            if (IsVisible)
            {
                CollectRequested?.Invoke(id);
            }
        }

        private void OnGUI()
        {
            if (!BeginPanel("Chicken Coop") || State == null)
            {
                return;
            }
            foreach (var animal in State.Animals)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{animal.AnimalId} Feed:{animal.OwnedFeedQuantity} Fed:{animal.FedToday} Product:{animal.ProductReady}",
                    LabelStyle);
                if (GUILayout.Button("Feed", ButtonStyle))
                {
                    RequestFeed(animal.AnimalId);
                }
                if (GUILayout.Button("Collect", ButtonStyle))
                {
                    RequestCollect(animal.AnimalId);
                }
                GUILayout.EndHorizontal();
            }
            EndPanel();
        }
    }
}
