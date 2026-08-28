using UnityEngine;

namespace CozyTown.Unity.Hud
{
    internal static class CozyTownDebugGuiStyles
    {
        public static int CalculateFontSize(int screenHeight)
        {
            return Mathf.Clamp(Mathf.RoundToInt(screenHeight / 35f), 22, 36);
        }

        public static GUIStyle CreateLabelStyle(int fontSize)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft
            };
        }

        public static GUIStyle CreateBoxStyle()
        {
            return new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(16, 16, 12, 12)
            };
        }
    }
}
