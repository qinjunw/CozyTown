using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
using UnityEngine;

namespace CozyTown.Unity.Pond
{
    public sealed class CozyTownPondDebugView : CozyTownModalDebugViewBase
    {
        public event Action CatchRequested;
        public FishingViewState State { get; private set; }

        public void Show(FishingViewState state, string feedback)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            ShowBase(feedback);
        }

        public void RequestCatch()
        {
            if (IsVisible)
            {
                CatchRequested?.Invoke();
            }
        }

        private void OnGUI()
        {
            if (!BeginPanel("Fishing Pond") || State == null)
            {
                return;
            }
            foreach (var fish in State.Entries)
            {
                GUILayout.Label($"{fish.DisplayName} Owned:{fish.OwnedQuantity}", LabelStyle);
            }
            if (GUILayout.Button("Cast", ButtonStyle))
            {
                RequestCatch();
            }
            EndPanel();
        }
    }
}
