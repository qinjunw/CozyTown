using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public sealed class UnityApplicationQuitter : IApplicationQuitter
    {
        public void Quit() => Application.Quit();
    }
}
