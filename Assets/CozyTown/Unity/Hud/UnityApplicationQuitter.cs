using UnityEngine;

namespace CozyTown.Unity.Hud
{
    public sealed class UnityApplicationQuitter : IApplicationQuitter
    {
        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
