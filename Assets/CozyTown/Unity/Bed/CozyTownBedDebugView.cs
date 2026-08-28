using System;
using CozyTown.Unity.Hud;
using UnityEngine;

namespace CozyTown.Unity.Bed
{
    public sealed class CozyTownBedDebugView : CozyTownModalDebugViewBase
    {
        public event Action SleepRequested;

        public void Show(string feedback) => ShowBase(feedback);

        public void RequestSleep()
        {
            if (IsVisible)
            {
                SleepRequested?.Invoke();
            }
        }

        private void OnGUI()
        {
            if (!BeginPanel("Bed — sleep until tomorrow?"))
            {
                return;
            }

            if (GUILayout.Button("Sleep", ButtonStyle))
            {
                RequestSleep();
            }

            EndPanel();
        }
    }
}
