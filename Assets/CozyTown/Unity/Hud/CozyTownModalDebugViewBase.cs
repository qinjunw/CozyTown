using System;
using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public abstract class CozyTownModalDebugViewBase : MonoBehaviour
    {
        public event Action CloseRequested;
        public bool IsVisible { get; private set; }
        public string Feedback { get; private set; } = string.Empty;

        public void Hide() => IsVisible = false;

        public void RequestClose()
        {
            if (IsVisible)
            {
                CloseRequested?.Invoke();
            }
        }

        protected void ShowBase(string feedback)
        {
            Feedback = feedback ?? string.Empty;
            IsVisible = true;
        }
    }
}
